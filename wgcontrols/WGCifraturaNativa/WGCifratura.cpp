// WGCifratura: libreria nativa di cifratura di WinGabriel (si compila a 32 e a 64 bit: WGCifratura32.dll e WGCifratura64.dll).
//
// Il codice Visual Basic .NET si decompila in un attimo: per questo la cifratura vive qui, in codice nativo, e il programma
// gestito si limita a chiamarla. Usa soltanto CNG di Windows (bcrypt.dll), senza altre dipendenze.
//
// Due modi:
//   senza password  -> la chiave deriva da un segreto incorporato in questa libreria e dallo "scopo" (una parola che dice a cosa serve
//                      il dato). Il dato si legge su qualunque computer e con qualunque utente di Windows, ma solo con questa libreria.
//   con password    -> la chiave deriva dalla password (PBKDF2-HMAC-SHA256 con sale casuale). Senza la password non si apre.
// In entrambi i casi la cifratura e AES-256-GCM: se i dati sono alterati o la chiave e sbagliata la lettura fallisce.
//
// Formati:
//   blob senza password: "WGN1" | nonce(12) | cifrato | tag(16)         (autenticato insieme a "WGN1" e allo scopo)
//   blob con password:   "WGP1" | iterazioni(4) | sale(16) | nonce(12) | cifrato | tag(16)
//   file:                "WGFN"/"WGFP" | iterazioni(4) | sale(16) | prefisso(4) | blocchi (lunghezza(4) + cifrato + tag) da 64 KB
//
// Codici di ritorno negativi: -1 parametri, -2 buffer troppo piccolo, -3 errore di cifratura, -4 autenticazione fallita
// (password sbagliata o dati alterati), -5 errore di file, -6 formato sconosciuto o modo diverso (serve o non serve la password).

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#pragma comment(lib, "bcrypt.lib")

#ifndef STATUS_AUTH_TAG_MISMATCH
#define STATUS_AUTH_TAG_MISMATCH ((NTSTATUS)0xC000A002L)
#endif

#define OK(s) ((s) >= 0)
#define API extern "C" int __stdcall

// Ultimo errore di questo thread: quale passo e fallito e con quale codice (NTSTATUS di CNG o errore di Windows).
static __declspec(thread) LONG g_stato = 0;
static __declspec(thread) char g_passo[96] = "";

static void Fallito(const char* passo, LONG stato)
{
    g_stato = stato;
    strncpy_s(g_passo, sizeof(g_passo), passo, _TRUNCATE);
}

static const int LungChiave = 32;
static const int LungNonce = 12;
static const int LungTag = 16;
static const int LungSale = 16;
static const int IterazioniPredefinite = 200000;
static const int DimBlocco = 65536;

// Segreto incorporato, spezzato in due parti che si ricompongono solo in memoria. Sta in WGSegreto.h, che non è nel
// repository pubblico: chi compila da sé ne riceve uno nuovo a caso.
#include "WGSegreto.h"

static void Segreto(BYTE out[32])
{
    for (int i = 0; i < 32; i++) out[i] = (BYTE)(PA[i] ^ PB[i] ^ (BYTE)((i * 37 + 11) & 0xFF));
}

static void ScriviLe32(BYTE* p, unsigned int v)
{
    p[0] = (BYTE)v; p[1] = (BYTE)(v >> 8); p[2] = (BYTE)(v >> 16); p[3] = (BYTE)(v >> 24);
}

static unsigned int LeggiLe32(const BYTE* p)
{
    return (unsigned int)p[0] | ((unsigned int)p[1] << 8) | ((unsigned int)p[2] << 16) | ((unsigned int)p[3] << 24);
}

static int Casuali(BYTE* buf, int n)
{
    if (n <= 0) return 0;
    NTSTATUS s = BCryptGenRandom(NULL, buf, (ULONG)n, BCRYPT_USE_SYSTEM_PREFERRED_RNG);
    if (OK(s)) return 0;
    Fallito("BCryptGenRandom", s);
    return -3;
}

static int HmacSha256(const BYTE* chiave, int lungChiave, const BYTE* dati, int lungDati, BYTE out[32])
{
    BCRYPT_ALG_HANDLE alg = NULL;
    BCRYPT_HASH_HANDLE hash = NULL;
    int r = -3;
    NTSTATUS s = BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, NULL, BCRYPT_ALG_HANDLE_HMAC_FLAG);
    if (!OK(s)) { Fallito("HMAC: BCryptOpenAlgorithmProvider(SHA256)", s); return -3; }
    s = BCryptCreateHash(alg, &hash, NULL, 0, (PUCHAR)chiave, (ULONG)lungChiave, 0);
    if (OK(s))
    {
        s = BCryptHashData(hash, (PUCHAR)dati, (ULONG)lungDati, 0);
        if (OK(s))
        {
            s = BCryptFinishHash(hash, out, 32, 0);
            if (OK(s)) r = 0; else Fallito("HMAC: BCryptFinishHash", s);
        }
        else Fallito("HMAC: BCryptHashData", s);
        BCryptDestroyHash(hash);
    }
    else Fallito("HMAC: BCryptCreateHash", s);
    BCryptCloseAlgorithmProvider(alg, 0);
    return r;
}

// Chiave per uno scopo: HMAC-SHA256(segreto incorporato, "WinGabriel.Scopo:" + scopo).
static int ChiaveScopo(const char* scopo, BYTE out[32])
{
    BYTE segreto[32];
    char msg[256];
    const char* prefisso = "WinGabriel.Scopo:";
    size_t lp = strlen(prefisso);
    size_t ls = scopo ? strlen(scopo) : 0;
    if (ls > 200) return -1;
    memcpy(msg, prefisso, lp);
    if (ls) memcpy(msg + lp, scopo, ls);
    Segreto(segreto);
    int r = HmacSha256(segreto, 32, (const BYTE*)msg, (int)(lp + ls), out);
    SecureZeroMemory(segreto, sizeof(segreto));
    return r;
}

static int Pbkdf2(const BYTE* pw, int pwLen, const BYTE* sale, int saleLen, int iter, BYTE* out, int outLen)
{
    BCRYPT_ALG_HANDLE alg = NULL;
    NTSTATUS s = BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, NULL, BCRYPT_ALG_HANDLE_HMAC_FLAG);
    if (!OK(s)) { Fallito("PBKDF2: BCryptOpenAlgorithmProvider(SHA256)", s); return -3; }
    s = BCryptDeriveKeyPBKDF2(alg, (PUCHAR)pw, (ULONG)pwLen, (PUCHAR)sale, (ULONG)saleLen, (ULONGLONG)iter, out, (ULONG)outLen, 0);
    BCryptCloseAlgorithmProvider(alg, 0);
    if (OK(s)) return 0;
    Fallito("PBKDF2: BCryptDeriveKeyPBKDF2", s);
    return -3;
}

// AES-256-GCM. Cifra: out = cifrato (inLen byte), tag = 16 byte. Decifra: in = cifrato, tag = tag atteso.
static int Gcm(bool cifra, const BYTE* chiave, const BYTE* nonce, const BYTE* aad, int aadLen, const BYTE* in, int inLen, BYTE* out, BYTE* tag)
{
    BCRYPT_ALG_HANDLE alg = NULL;
    BCRYPT_KEY_HANDLE key = NULL;
    int r = -3;
    BYTE vuoto = 0;
    NTSTATUS st = BCryptOpenAlgorithmProvider(&alg, BCRYPT_AES_ALGORITHM, NULL, 0);
    if (!OK(st)) { Fallito("GCM: BCryptOpenAlgorithmProvider(AES)", st); return -3; }
    st = BCryptSetProperty(alg, BCRYPT_CHAINING_MODE, (PUCHAR)BCRYPT_CHAIN_MODE_GCM, sizeof(BCRYPT_CHAIN_MODE_GCM), 0);
    if (!OK(st)) Fallito("GCM: BCryptSetProperty(ChainingModeGCM)", st);
    if (OK(st))
    {
        st = BCryptGenerateSymmetricKey(alg, &key, NULL, 0, (PUCHAR)chiave, LungChiave, 0);
        if (!OK(st)) Fallito("GCM: BCryptGenerateSymmetricKey", st);
    }
    if (OK(st))
    {
        BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO info;
        BCRYPT_INIT_AUTH_MODE_INFO(info);
        info.pbNonce = (PUCHAR)nonce;
        info.cbNonce = LungNonce;
        info.pbAuthData = aadLen > 0 ? (PUCHAR)aad : NULL;
        info.cbAuthData = (ULONG)aadLen;
        info.pbTag = tag;
        info.cbTag = LungTag;
        ULONG fatti = 0;
        NTSTATUS s;
        if (cifra) s = BCryptEncrypt(key, inLen > 0 ? (PUCHAR)in : &vuoto, (ULONG)inLen, &info, NULL, 0, inLen > 0 ? out : &vuoto, (ULONG)inLen, &fatti, 0);
        else s = BCryptDecrypt(key, inLen > 0 ? (PUCHAR)in : &vuoto, (ULONG)inLen, &info, NULL, 0, inLen > 0 ? out : &vuoto, (ULONG)inLen, &fatti, 0);
        if (OK(s)) r = 0;
        else if (s == STATUS_AUTH_TAG_MISMATCH) r = -4;
        else Fallito(cifra ? "GCM: BCryptEncrypt" : "GCM: BCryptDecrypt", s);
        BCryptDestroyKey(key);
    }
    BCryptCloseAlgorithmProvider(alg, 0);
    return r;
}

// ------------------------------------------------------------------------------------------------------------------
// Funzioni esportate
// ------------------------------------------------------------------------------------------------------------------

API WGC_Versione(void)
{
    return 1;
}

// Descrive l'ultimo errore interno di questo thread ("passo; codice 0x..."). Restituisce i caratteri scritti.
API WGC_UltimoErrore(char* buf, int cap)
{
    if (!buf || cap < 2) return -1;
    char tmp[160];
    _snprintf_s(tmp, sizeof(tmp), _TRUNCATE, "%s; codice 0x%08X", g_passo[0] ? g_passo : "nessun passo registrato", (unsigned int)g_stato);
    strncpy_s(buf, (size_t)cap, tmp, _TRUNCATE);
    return (int)strlen(buf);
}

API WGC_Casuali(BYTE* buf, int n)
{
    if (!buf || n < 0) return -1;
    return Casuali(buf, n);
}

// PBKDF2-HMAC-SHA256 (stessi risultati di Rfc2898DeriveBytes con SHA256).
API WGC_Pbkdf2(const BYTE* pw, int pwLen, const BYTE* sale, int saleLen, int iter, BYTE* out, int outLen)
{
    if (!pw || pwLen <= 0 || !sale || saleLen <= 0 || iter < 1 || !out || outLen <= 0) return -1;
    return Pbkdf2(pw, pwLen, sale, saleLen, iter, out, outLen);
}

API WGC_ChiaveIncorporata(const char* scopo, BYTE* out32)
{
    if (!out32) return -1;
    return ChiaveScopo(scopo, out32);
}

// Con una chiave di 32 byte gia pronta: out = nonce(12) | cifrato | tag(16). out deve avere inLen + 28 byte.
API WGC_GcmCifra(const BYTE* chiave, const BYTE* in, int inLen, const BYTE* aad, int aadLen, BYTE* out, int outCap)
{
    if (!chiave || inLen < 0 || (inLen > 0 && !in) || !out || aadLen < 0) return -1;
    if (outCap < inLen + LungNonce + LungTag) return -2;
    if (Casuali(out, LungNonce) != 0) return -3;
    int r = Gcm(true, chiave, out, aad, aadLen, in, inLen, out + LungNonce, out + LungNonce + inLen);
    return r == 0 ? inLen + LungNonce + LungTag : r;
}

API WGC_GcmDecifra(const BYTE* chiave, const BYTE* in, int inLen, const BYTE* aad, int aadLen, BYTE* out, int outCap)
{
    if (!chiave || !in || !out || aadLen < 0 || inLen < LungNonce + LungTag) return -1;
    int n = inLen - LungNonce - LungTag;
    if (outCap < n) return -2;
    BYTE tag[LungTag];
    memcpy(tag, in + LungNonce + n, LungTag);
    int r = Gcm(false, chiave, in, aad, aadLen, in + LungNonce, n, out, tag);
    return r == 0 ? n : r;
}

// Blob. pwLen == 0: senza password, con la chiave dello scopo. pwLen > 0: con password.
// out deve avere almeno inLen + 64 byte.
API WGC_Proteggi(const char* scopo, const BYTE* pw, int pwLen, const BYTE* in, int inLen, BYTE* out, int outCap)
{
    if (inLen < 0 || (inLen > 0 && !in) || !out || pwLen < 0) return -1;
    BYTE chiave[LungChiave];
    BYTE intest[4 + 4 + LungSale];
    int lenIntest;
    BYTE aad[sizeof(intest) + 200];
    int lenAad;
    int r;

    if (pwLen > 0)
    {
        if (!pw) return -1;
        memcpy(intest, "WGP1", 4);
        ScriviLe32(intest + 4, IterazioniPredefinite);
        if (Casuali(intest + 8, LungSale) != 0) return -3;
        lenIntest = 4 + 4 + LungSale;
        r = Pbkdf2(pw, pwLen, intest + 8, LungSale, IterazioniPredefinite, chiave, LungChiave);
        memcpy(aad, intest, lenIntest);
        lenAad = lenIntest;
    }
    else
    {
        memcpy(intest, "WGN1", 4);
        lenIntest = 4;
        size_t ls = scopo ? strlen(scopo) : 0;
        if (ls > 200) return -1;
        r = ChiaveScopo(scopo, chiave);
        memcpy(aad, intest, 4);
        if (ls) memcpy(aad + 4, scopo, ls);
        lenAad = 4 + (int)ls;
    }
    if (r != 0) { SecureZeroMemory(chiave, sizeof(chiave)); return r; }
    if (outCap < lenIntest + LungNonce + inLen + LungTag) { SecureZeroMemory(chiave, sizeof(chiave)); return -2; }
    memcpy(out, intest, lenIntest);
    r = WGC_GcmCifra(chiave, in, inLen, aad, lenAad, out + lenIntest, outCap - lenIntest);
    SecureZeroMemory(chiave, sizeof(chiave));
    return r < 0 ? r : lenIntest + r;
}

API WGC_Rivela(const char* scopo, const BYTE* pw, int pwLen, const BYTE* in, int inLen, BYTE* out, int outCap)
{
    if (!in || !out || inLen < 4 || pwLen < 0) return -1;
    BYTE chiave[LungChiave];
    BYTE aad[8 + LungSale + 200];
    int lenIntest, lenAad, r;

    if (memcmp(in, "WGP1", 4) == 0)
    {
        if (pwLen <= 0 || !pw) return -6;
        lenIntest = 4 + 4 + LungSale;
        if (inLen < lenIntest + LungNonce + LungTag) return -6;
        unsigned int iter = LeggiLe32(in + 4);
        if (iter < 1000 || iter > 10000000) return -6;
        r = Pbkdf2(pw, pwLen, in + 8, LungSale, (int)iter, chiave, LungChiave);
        memcpy(aad, in, lenIntest);
        lenAad = lenIntest;
    }
    else if (memcmp(in, "WGN1", 4) == 0)
    {
        if (pwLen > 0) return -6;
        lenIntest = 4;
        if (inLen < lenIntest + LungNonce + LungTag) return -6;
        size_t ls = scopo ? strlen(scopo) : 0;
        if (ls > 200) return -1;
        r = ChiaveScopo(scopo, chiave);
        memcpy(aad, in, 4);
        if (ls) memcpy(aad + 4, scopo, ls);
        lenAad = 4 + (int)ls;
    }
    else return -6;

    if (r != 0) { SecureZeroMemory(chiave, sizeof(chiave)); return r; }
    r = WGC_GcmDecifra(chiave, in + lenIntest, inLen - lenIntest, aad, lenAad, out, outCap);
    SecureZeroMemory(chiave, sizeof(chiave));
    return r;
}

// ------------------------------------------------------------------------------------------------------------------
// File (per le esportazioni): a blocchi, cosi non serve tenere tutto in memoria. Ogni blocco ha il proprio nonce
// (prefisso casuale + numero del blocco) e l'ultimo e segnato: un file tagliato o con blocchi spostati non si apre.
// ------------------------------------------------------------------------------------------------------------------

static const int LungIntestFile = 4 + 4 + LungSale + 4;

static int ChiaveFile(const BYTE* intest, const BYTE* pw, int pwLen, BYTE* chiave)
{
    if (memcmp(intest, "WGFP", 4) == 0)
    {
        if (pwLen <= 0 || !pw) return -6;
        unsigned int iter = LeggiLe32(intest + 4);
        if (iter < 1000 || iter > 10000000) return -6;
        return Pbkdf2(pw, pwLen, intest + 8, LungSale, (int)iter, chiave, LungChiave);
    }
    if (memcmp(intest, "WGFN", 4) == 0)
    {
        if (pwLen > 0) return -6;
        return ChiaveScopo("file", chiave);
    }
    return -6;
}

static void NonceBlocco(const BYTE* prefisso, unsigned long long indice, BYTE* nonce)
{
    memcpy(nonce, prefisso, 4);
    for (int i = 0; i < 8; i++) nonce[4 + i] = (BYTE)(indice >> (8 * i));
}

static bool Leggi(HANDLE f, BYTE* buf, DWORD n)
{
    DWORD letti = 0;
    return ReadFile(f, buf, n, &letti, NULL) && letti == n;
}

static bool Scrivi(HANDLE f, const BYTE* buf, DWORD n)
{
    DWORD scritti = 0;
    return WriteFile(f, buf, n, &scritti, NULL) && scritti == n;
}

API WGC_CifraFile(const wchar_t* origine, const wchar_t* destinazione, const BYTE* pw, int pwLen)
{
    if (!origine || !destinazione || pwLen < 0) return -1;
    HANDLE fin = CreateFileW(origine, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (fin == INVALID_HANDLE_VALUE) { Fallito("File: apertura del file di origine", (LONG)GetLastError()); return -5; }
    HANDLE fout = CreateFileW(destinazione, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (fout == INVALID_HANDLE_VALUE) { Fallito("File: creazione del file di destinazione", (LONG)GetLastError()); CloseHandle(fin); return -5; }

    int r = 0;
    BYTE chiave[LungChiave];
    BYTE intest[LungIntestFile];
    BYTE* chiaro = (BYTE*)malloc(DimBlocco);
    BYTE* cifrato = (BYTE*)malloc(DimBlocco + LungTag);
    BYTE aad[LungIntestFile + 1];
    LARGE_INTEGER dimensione;
    dimensione.QuadPart = 0;
    if (!chiaro || !cifrato || !GetFileSizeEx(fin, &dimensione)) r = -5;

    if (r == 0)
    {
        if (pwLen > 0)
        {
            memcpy(intest, "WGFP", 4);
            ScriviLe32(intest + 4, IterazioniPredefinite);
            r = Casuali(intest + 8, LungSale);
        }
        else
        {
            memcpy(intest, "WGFN", 4);
            ScriviLe32(intest + 4, 0);
            memset(intest + 8, 0, LungSale);
        }
        if (r == 0) r = Casuali(intest + 8 + LungSale, 4);
        if (r == 0) r = ChiaveFile(intest, pw, pwLen, chiave);
        if (r == 0 && !Scrivi(fout, intest, LungIntestFile)) r = -5;
    }
    if (r == 0)
    {
        memcpy(aad, intest, LungIntestFile);
        unsigned long long indice = 0;
        long long rimasti = dimensione.QuadPart;
        for (;;)
        {
            DWORD n = rimasti > DimBlocco ? DimBlocco : (DWORD)rimasti;
            if (n > 0 && !Leggi(fin, chiaro, n)) { r = -5; break; }
            rimasti -= n;
            bool ultimo = rimasti <= 0;
            aad[LungIntestFile] = ultimo ? 1 : 0;
            BYTE nonce[LungNonce];
            NonceBlocco(intest + 8 + LungSale, indice, nonce);
            r = Gcm(true, chiave, nonce, aad, LungIntestFile + 1, chiaro, (int)n, cifrato, cifrato + n);
            if (r != 0) break;
            BYTE lung[4];
            ScriviLe32(lung, n + LungTag);
            if (!Scrivi(fout, lung, 4) || !Scrivi(fout, cifrato, n + LungTag)) { r = -5; break; }
            indice++;
            if (ultimo) break;
        }
    }

    SecureZeroMemory(chiave, sizeof(chiave));
    if (chiaro) { SecureZeroMemory(chiaro, DimBlocco); free(chiaro); }
    if (cifrato) free(cifrato);
    CloseHandle(fin);
    CloseHandle(fout);
    if (r != 0) DeleteFileW(destinazione);
    return r;
}

API WGC_DecifraFile(const wchar_t* origine, const wchar_t* destinazione, const BYTE* pw, int pwLen)
{
    if (!origine || !destinazione || pwLen < 0) return -1;
    HANDLE fin = CreateFileW(origine, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (fin == INVALID_HANDLE_VALUE) { Fallito("File: apertura del file di origine", (LONG)GetLastError()); return -5; }
    HANDLE fout = CreateFileW(destinazione, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (fout == INVALID_HANDLE_VALUE) { Fallito("File: creazione del file di destinazione", (LONG)GetLastError()); CloseHandle(fin); return -5; }

    int r = 0;
    BYTE chiave[LungChiave];
    BYTE intest[LungIntestFile];
    BYTE* chiaro = (BYTE*)malloc(DimBlocco);
    BYTE* cifrato = (BYTE*)malloc(DimBlocco + LungTag);
    BYTE aad[LungIntestFile + 1];
    LARGE_INTEGER dimensione;
    dimensione.QuadPart = 0;
    if (!chiaro || !cifrato || !GetFileSizeEx(fin, &dimensione)) r = -5;

    if (r == 0 && !Leggi(fin, intest, LungIntestFile)) r = -6;
    if (r == 0) r = ChiaveFile(intest, pw, pwLen, chiave);
    if (r == 0)
    {
        memcpy(aad, intest, LungIntestFile);
        unsigned long long indice = 0;
        long long posizione = LungIntestFile;
        for (;;)
        {
            BYTE lung[4];
            if (!Leggi(fin, lung, 4)) { r = -6; break; }
            unsigned int c = LeggiLe32(lung);
            if (c < (unsigned int)LungTag || c > (unsigned int)(DimBlocco + LungTag)) { r = -6; break; }
            if (!Leggi(fin, cifrato, c)) { r = -6; break; }
            posizione += 4 + c;
            bool ultimo = posizione >= dimensione.QuadPart;
            aad[LungIntestFile] = ultimo ? 1 : 0;
            BYTE nonce[LungNonce];
            NonceBlocco(intest + 8 + LungSale, indice, nonce);
            int n = (int)c - LungTag;
            BYTE tag[LungTag];
            memcpy(tag, cifrato + n, LungTag);
            r = Gcm(false, chiave, nonce, aad, LungIntestFile + 1, cifrato, n, chiaro, tag);
            if (r != 0) break;
            if (n > 0 && !Scrivi(fout, chiaro, (DWORD)n)) { r = -5; break; }
            indice++;
            if (ultimo) break;
        }
    }

    SecureZeroMemory(chiave, sizeof(chiave));
    if (chiaro) { SecureZeroMemory(chiaro, DimBlocco); free(chiaro); }
    if (cifrato) free(cifrato);
    CloseHandle(fin);
    CloseHandle(fout);
    if (r != 0) DeleteFileW(destinazione);
    return r;
}
