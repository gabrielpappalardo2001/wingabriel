// WGLettoreVoce: il motore vocale proprio del lettore di schermo, fatto come WGSapi.
//
// Serve quando il lettore gira da solo, senza wgcontrols.dll e senza WGSapi.exe. Ha la stessa struttura di WGSapi, ed e' questa
// che toglie la latenza: SAPI non suona da solo, ma sintetizza dentro uno stream in memoria (CWasapiStream); un thread WASAPI a
// priorita' massima, in modalita' condivisa, preleva da quello stream ogni due millisecondi e consegna i campioni al dispositivo.
// Il volume si applica sui campioni al momento della consegna, con una piccola rampa; uno stop sfuma gli ultimi campioni invece di
// tagliarli. Il thread segue il dispositivo predefinito di Windows: se si collega una cuffia, riparte da solo.
//
// Si comanda con righe di testo UTF-8 sullo standard input, una per comando:
//   PARLA 0|1 testo   dice il testo; con 1 interrompe prima quello che sta dicendo
//   FERMA             interrompe la voce
//   VELOCITA n        da -10 a 10
//   VOLUME n          da 0 a 100
//   VOCE nome         sceglie la voce il cui nome contiene il testo; vuoto = la voce predefinita di Windows
//   ESCI              termina
// Risponde sullo standard output con STATO 1 quando comincia a parlare e, quando l'ultimo campione e' uscito dal dispositivo,
// con STATO 0 e FINE. Non scrive alcun file: niente log.

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sapi.h>
#include <sphelper.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <wrl/client.h>
#include <io.h>
#include <fcntl.h>
#include <cstdio>
#include <string>
#include <queue>
#include <thread>
#include <atomic>
#include <mutex>
#include <algorithm>

#pragma comment(lib, "sapi.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "user32.lib")

#ifndef AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
#define AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM 0x80000000
#endif
#ifndef AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY
#define AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY 0x08000000
#endif

using Microsoft::WRL::ComPtr;

static std::atomic<bool> g_continua(true);
static std::atomic<int> g_volume(100);
static std::atomic<bool> g_cambioDispositivo(false);
static std::atomic<bool> g_riavviaWasapi(false);
static std::atomic<int> g_frasiInCorso(0);      // frasi date a SAPI e non ancora finite di sintetizzare
static std::atomic<bool> g_attendeFine(false);   // SAPI ha finito: manca solo che l'ultimo campione esca dal dispositivo
static std::atomic<bool> g_staParlando(false);
static WAVEFORMATEX g_formato = {};
static HANDLE g_eventoComandi = NULL;

static std::mutex g_uscita;
static std::mutex g_codaMutex;
static std::queue<std::wstring> g_coda;

static void Scrivi(const char* riga) {
    try {
        std::lock_guard<std::mutex> blocco(g_uscita);
        fputs(riga, stdout);
        fputc('\n', stdout);
        fflush(stdout);
    } catch (...) {
    }
}

static std::wstring DaUtf8(const std::string& s) {
    if (s.empty()) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), NULL, 0);
    if (n <= 0) return std::wstring();
    std::wstring r(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), &r[0], n);
    return r;
}

static std::wstring Minuscolo(std::wstring s) {
    std::transform(s.begin(), s.end(), s.begin(), [](wchar_t c) { return (wchar_t)towlower(c); });
    return s;
}

static bool ELettera(wchar_t c) { return IsCharAlphaW(c) != FALSE; }
static bool EMaiuscola(wchar_t c) { return IsCharUpperW(c) != FALSE; }
static bool EMinuscola(wchar_t c) { return IsCharLowerW(c) != FALSE; }
static bool ECifra(wchar_t c) { return c >= L'0' && c <= L'9'; }

static wchar_t InMinuscolo(wchar_t c) {
    return (wchar_t)(ULONG_PTR)CharLowerW((LPWSTR)(ULONG_PTR)c);
}

static bool EVocale(wchar_t c) {
    static const std::wstring vocali = L"aeiouy\u00e0\u00e1\u00e8\u00e9\u00ec\u00ed\u00f2\u00f3\u00f9\u00fa";
    return vocali.find(InMinuscolo(c)) != std::wstring::npos;
}

static const wchar_t* NomeLettera(wchar_t c) {
    switch (c) {
    case L'a': return L"a";
    case L'b': return L"bi";
    case L'c': return L"ci";
    case L'd': return L"di";
    case L'e': return L"e";
    case L'f': return L"effe";
    case L'g': return L"gi";
    case L'h': return L"acca";
    case L'i': return L"i";
    case L'j': return L"i lunga";
    case L'k': return L"cappa";
    case L'l': return L"elle";
    case L'm': return L"emme";
    case L'n': return L"enne";
    case L'o': return L"o";
    case L'p': return L"pi";
    case L'q': return L"cu";
    case L'r': return L"erre";
    case L's': return L"esse";
    case L't': return L"ti";
    case L'u': return L"u";
    case L'v': return L"vu";
    case L'w': return L"doppia vu";
    case L'x': return L"ics";
    case L'y': return L"ipsilon";
    case L'z': return L"zeta";
    default: return nullptr;
    }
}

static std::wstring LeggiSegmento(const std::wstring& s) {
    if (s.size() < 2) return s;
    bool tutteMaiuscole = true;
    bool tutteMinuscole = true;
    for (wchar_t c : s) {
        if (EVocale(c)) return s;
        if (!EMaiuscola(c)) tutteMaiuscole = false;
        if (!EMinuscola(c)) tutteMinuscole = false;
    }
    if (!tutteMaiuscole && !tutteMinuscole) return s;
    std::wstring r;
    for (wchar_t c : s) {
        const wchar_t* nome = NomeLettera(InMinuscolo(c));
        if (!nome) return s;
        if (!r.empty()) r += L' ';
        r += nome;
    }
    return r;
}

static std::wstring SostituisciPunti(const std::wstring& t) {
    std::wstring r;
    r.reserve(t.size() + 16);
    for (size_t i = 0; i < t.size(); i++) {
        wchar_t c = t[i];
        if (c == L'.' && i + 1 < t.size() && ELettera(t[i + 1]) && (i == 0 || t[i - 1] != L'.')) {
            bool attaccato = i > 0 && (ELettera(t[i - 1]) || ECifra(t[i - 1]));
            r += attaccato ? L" punto " : L"punto ";
            continue;
        }
        r += c;
    }
    return r;
}

static std::wstring SeparaParole(const std::wstring& t) {
    std::wstring r;
    r.reserve(t.size() + 32);
    size_t i = 0;
    size_t n = t.size();
    while (i < n) {
        if (!ELettera(t[i])) {
            r += t[i];
            i++;
            continue;
        }
        size_t inizio = i;
        while (i < n && ELettera(t[i])) i++;
        std::wstring parola = t.substr(inizio, i - inizio);
        std::wstring segmento;
        bool primo = true;
        for (size_t k = 0; k < parola.size(); k++) {
            wchar_t c = parola[k];
            bool taglia = false;
            if (!segmento.empty()) {
                wchar_t precedente = parola[k - 1];
                if (EMaiuscola(c) && EMinuscola(precedente)) taglia = true;
                else if (EMaiuscola(c) && EMaiuscola(precedente) && k + 1 < parola.size() && EMinuscola(parola[k + 1])) taglia = true;
            }
            if (taglia) {
                if (!primo) r += L' ';
                r += LeggiSegmento(segmento);
                primo = false;
                segmento.clear();
            }
            segmento += c;
        }
        if (!segmento.empty()) {
            if (!primo) r += L' ';
            r += LeggiSegmento(segmento);
        }
    }
    return r;
}

static std::wstring PreparaTesto(const std::wstring& testo) {
    try {
        // Come nella sintesi di WinGabriel: le parole composte le separa chi manda il testo (CodaVoce) e le sigle
        // le legge la voce a modo suo, senza nomi di lettere scritti qui.
        return SostituisciPunti(testo);
    } catch (...) {
        return testo;
    }
}

// ---------------------------------------------------------------------------------------------------------
// Lo stream in cui SAPI scrive l'audio e da cui il thread WASAPI lo preleva
// ---------------------------------------------------------------------------------------------------------

class CFlusso : public IStream {
    LONG m_rif = 1;
    std::mutex m_mutex;
    std::string m_buffer;
    size_t m_lettura = 0;
    double m_guadagnoApplicato = 1.0;
public:
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_ISequentialStream || riid == IID_IStream) {
            *ppv = static_cast<IStream*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = NULL;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&m_rif); }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG r = InterlockedDecrement(&m_rif);
        if (r == 0) delete this;
        return r;
    }
    HRESULT STDMETHODCALLTYPE Read(void*, ULONG, ULONG*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Write(const void* pv, ULONG cb, ULONG* scritti) override {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (scritti) *scritti = cb;
            if (pv && cb > 0) m_buffer.append((const char*)pv, cb);
            return S_OK;
        } catch (...) {
            if (scritti) *scritti = 0;
            return E_OUTOFMEMORY;
        }
    }
    HRESULT STDMETHODCALLTYPE Seek(LARGE_INTEGER, DWORD, ULARGE_INTEGER*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetSize(ULARGE_INTEGER) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE CopyTo(IStream*, ULARGE_INTEGER, ULARGE_INTEGER*, ULARGE_INTEGER*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Commit(DWORD) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Revert() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE LockRegion(ULARGE_INTEGER, ULARGE_INTEGER, DWORD) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE UnlockRegion(ULARGE_INTEGER, ULARGE_INTEGER, DWORD) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Stat(STATSTG* st, DWORD) override {
        if (st) memset(st, 0, sizeof(STATSTG));
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Clone(IStream**) override { return E_NOTIMPL; }

    // Ferma l'audio: rimane solo una brevissima coda sfumata, cosi' lo stop non fa "clic".
    void InterrompiESfuma() {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (m_lettura >= m_buffer.size()) {
                m_buffer.clear();
                m_lettura = 0;
                return;
            }
            size_t disponibili = m_buffer.size() - m_lettura;
            if (disponibili >= 4) {
                size_t byteSfumo = 441 * 2;
                if (byteSfumo > disponibili) byteSfumo = disponibili - (disponibili % 2);
                size_t campioni = byteSfumo / 2;
                int16_t* s = reinterpret_cast<int16_t*>(&m_buffer[m_lettura]);
                for (size_t i = 0; i < campioni; i++) {
                    double guadagno = 1.0 - (double)i / (double)campioni;
                    int32_t v = (int32_t)(s[i] * guadagno);
                    s[i] = (int16_t)std::max(-32768, std::min(32767, v));
                }
                m_buffer.resize(m_lettura + byteSfumo);
            } else {
                m_buffer.clear();
                m_lettura = 0;
            }
        } catch (...) {
        }
    }

    size_t Disponibili() {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            return m_lettura >= m_buffer.size() ? 0 : m_buffer.size() - m_lettura;
        } catch (...) {
            return 0;
        }
    }

    size_t Preleva(BYTE* dati, size_t byteRichiesti) {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (m_lettura >= m_buffer.size()) {
                m_buffer.clear();
                m_lettura = 0;
                return 0;
            }
            size_t disponibili = m_buffer.size() - m_lettura;
            if (byteRichiesti > disponibili) byteRichiesti = disponibili;
            if (byteRichiesti > 0 && dati) {
                memcpy(dati, m_buffer.data() + m_lettura, byteRichiesti);
                // Il volume si applica qui, con una rampa lineare: cambia subito anche mentre la voce sta parlando.
                double obiettivo = std::max(0, std::min(100, g_volume.load())) / 100.0;
                size_t campioni = byteRichiesti / 2;
                if (campioni > 0 && (obiettivo != 1.0 || m_guadagnoApplicato != 1.0)) {
                    int16_t* s = reinterpret_cast<int16_t*>(dati);
                    double iniziale = m_guadagnoApplicato;
                    size_t rampa = 0;
                    double passo = 0.0;
                    if (iniziale != obiettivo) {
                        rampa = std::min<size_t>(campioni, 441);
                        passo = (obiettivo - iniziale) / (double)rampa;
                    }
                    for (size_t i = 0; i < campioni; i++) {
                        double g = (i < rampa) ? iniziale + passo * (double)i : obiettivo;
                        int32_t v = (int32_t)(s[i] * g);
                        s[i] = (int16_t)std::max(-32768, std::min(32767, v));
                    }
                    m_guadagnoApplicato = obiettivo;
                }
                m_lettura += byteRichiesti;
                if (m_lettura >= m_buffer.size()) {
                    m_buffer.clear();
                    m_lettura = 0;
                } else if (m_lettura > 524288) {
                    m_buffer.erase(0, m_lettura);
                    m_lettura = 0;
                }
            }
            return byteRichiesti;
        } catch (...) {
            return 0;
        }
    }
};

static CFlusso* g_flusso = nullptr;

// ---------------------------------------------------------------------------------------------------------
// Notifiche dei dispositivi audio: se cambia il predefinito o si collega una cuffia, il thread WASAPI riparte sul dispositivo giusto
// ---------------------------------------------------------------------------------------------------------

class CNotificheDispositivi : public IMMNotificationClient {
    LONG m_rif = 1;
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IMMNotificationClient)) {
            *ppv = static_cast<IMMNotificationClient*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&m_rif); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG r = InterlockedDecrement(&m_rif);
        if (r == 0) delete this;
        return r;
    }
    STDMETHODIMP OnDeviceStateChanged(LPCWSTR, DWORD) override { g_cambioDispositivo = true; return S_OK; }
    STDMETHODIMP OnDeviceAdded(LPCWSTR) override { g_cambioDispositivo = true; return S_OK; }
    STDMETHODIMP OnDeviceRemoved(LPCWSTR) override { g_cambioDispositivo = true; return S_OK; }
    STDMETHODIMP OnDefaultDeviceChanged(EDataFlow flusso, ERole ruolo, LPCWSTR) override {
        if (flusso == eRender) g_cambioDispositivo = true;
        return S_OK;
    }
    STDMETHODIMP OnPropertyValueChanged(LPCWSTR, const PROPERTYKEY) override { return S_OK; }
};

// Rampa di circa un terzo di millisecondo sul solo stacco silenzio/voce, senza allungare i blocchi: la latenza non cambia.
static void AmmorbidisciStacco(BYTE* dati, size_t byteAudio, bool inizia, bool finisce) {
    if (!dati || g_formato.wBitsPerSample != 16 || g_formato.nChannels != 1 || byteAudio < 4) return;
    size_t campioni = byteAudio / 2;
    int16_t* s = reinterpret_cast<int16_t*>(dati);
    size_t rampa = (size_t)(g_formato.nSamplesPerSec / 3000);
    if (rampa < 8) rampa = 8;
    if (rampa > campioni) rampa = campioni;
    if (inizia) {
        for (size_t i = 0; i < rampa; ++i) s[i] = (int16_t)((int32_t)s[i] * (int32_t)(i + 1) / (int32_t)rampa);
    }
    if (finisce && campioni > 1) {
        for (size_t i = 0; i < rampa; ++i) {
            size_t idx = campioni - rampa + i;
            s[idx] = (int16_t)((int32_t)s[idx] * (int32_t)(rampa - i) / (int32_t)rampa);
        }
    }
}

static void CicloWasapi() {
    ComPtr<IMMDeviceEnumerator> enumeratore;
    CNotificheDispositivi* notifiche = nullptr;
    bool registrato = false;
    if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_ALL, IID_PPV_ARGS(&enumeratore)))) {
        notifiche = new CNotificheDispositivi();
        if (SUCCEEDED(enumeratore->RegisterEndpointNotificationCallback(notifiche))) registrato = true;
    }

    while (g_continua) {
        try {
            if (enumeratore) {
                // Il flag si azzera PRIMA di interrogare il sistema, come in WGSapi: una notifica arrivata nel frattempo non va persa.
                g_cambioDispositivo = false;
                ComPtr<IMMDevice> dispositivo;
                enumeratore->GetDefaultAudioEndpoint(eRender, eConsole, &dispositivo);
                if (dispositivo) {
                    ComPtr<IAudioClient> client;
                    if (SUCCEEDED(dispositivo->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (void**)client.GetAddressOf()))) {
                        if (SUCCEEDED(client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, 0, 0, &g_formato, NULL))) {
                            ComPtr<IAudioRenderClient> render;
                            if (SUCCEEDED(client->GetService(__uuidof(IAudioRenderClient), (void**)render.GetAddressOf()))) {
                                client->Start();
                                bool dopoBuco = true;
                                while (g_continua) {
                                    if (g_cambioDispositivo.load()) break;
                                    if (g_riavviaWasapi.exchange(false)) {
                                        client->Stop();
                                        client->Reset();
                                        client->Start();
                                        dopoBuco = true;
                                    }
                                    UINT32 dimensione = 0, riempimento = 0;
                                    if (FAILED(client->GetBufferSize(&dimensione))) { Sleep(2); continue; }
                                    HRESULT hr = client->GetCurrentPadding(&riempimento);
                                    if (hr == AUDCLNT_E_DEVICE_INVALIDATED) break;
                                    if (FAILED(hr) || riempimento > dimensione) { Sleep(2); continue; }

                                    UINT32 liberi = dimensione - riempimento;
                                    size_t disponibili = g_flusso ? g_flusso->Disponibili() : 0;
                                    UINT32 allineamento = g_formato.nBlockAlign;
                                    if (liberi > 0 && disponibili > 0 && allineamento > 0) {
                                        UINT32 frame = liberi;
                                        size_t byteDaScrivere = (size_t)frame * allineamento;
                                        if (byteDaScrivere > disponibili) {
                                            byteDaScrivere = disponibili - (disponibili % allineamento);
                                            frame = (UINT32)(byteDaScrivere / allineamento);
                                        }
                                        if (frame > 0) {
                                            BYTE* dati = nullptr;
                                            HRESULT hrBuf = render->GetBuffer(frame, &dati);
                                            if (hrBuf == AUDCLNT_E_DEVICE_INVALIDATED) break;
                                            if (SUCCEEDED(hrBuf) && dati) {
                                                size_t letti = g_flusso->Preleva(dati, byteDaScrivere);
                                                bool fineNetta = letti < byteDaScrivere;
                                                if (fineNetta) memset(dati + letti, 0, byteDaScrivere - letti);
                                                if (letti >= 2) AmmorbidisciStacco(dati, letti, dopoBuco, fineNetta);
                                                dopoBuco = fineNetta;
                                                render->ReleaseBuffer(frame, 0);
                                            }
                                        }
                                    } else if (liberi > 0 && disponibili == 0) {
                                        if (riempimento * 50 < g_formato.nSamplesPerSec) dopoBuco = true;
                                        // SAPI ha finito e il buffer del dispositivo si e' svuotato: l'ultimo campione e' uscito.
                                        if (g_attendeFine.load() && riempimento == 0) {
                                            g_attendeFine = false;
                                            if (g_staParlando.exchange(false)) {
                                                Scrivi("STATO 0");
                                                Scrivi("FINE");
                                            }
                                        }
                                    }
                                    Sleep(2);
                                }
                                client->Stop();
                            }
                        }
                    }
                }
            }
        } catch (...) {
        }
        // Attesa prima di riprovare, a fette da dieci millisecondi, uscendo appena Windows segnala un cambio di dispositivo.
        for (int attesa = 0; attesa < 50 && g_continua; attesa++) {
            if (g_cambioDispositivo.load()) break;
            Sleep(10);
        }
    }

    if (enumeratore) {
        if (registrato && notifiche) enumeratore->UnregisterEndpointNotificationCallback(notifiche);
    }
    if (notifiche) notifiche->Release();
}

static void ThreadWasapi() {
    HRESULT hrCom = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);
    try {
        CicloWasapi();
    } catch (...) {
    }
    if (SUCCEEDED(hrCom)) CoUninitialize();
}

// ---------------------------------------------------------------------------------------------------------
// Comandi
// ---------------------------------------------------------------------------------------------------------

static void AccodaComando(const std::wstring& comando) {
    try {
        std::lock_guard<std::mutex> lock(g_codaMutex);
        g_coda.push(comando);
        if (g_coda.size() > 1000) g_coda.pop();
    } catch (...) {
    }
    if (g_eventoComandi) SetEvent(g_eventoComandi);
}

static void ThreadIngresso() {
    try {
        std::string riga;
        int c;
        while (g_continua) {
            riga.clear();
            bool fine = false;
            while ((c = fgetc(stdin)) != EOF) {
                if (c == '\n') { fine = true; break; }
                if (c != '\r') riga.push_back((char)c);
            }
            if (!fine && riga.empty()) {
                // Lo standard input si e' chiuso: chi ci comandava non c'e' piu'.
                AccodaComando(L"esci");
                return;
            }
            AccodaComando(DaUtf8(riga));
            if (!fine) return;
        }
    } catch (...) {
        AccodaComando(L"esci");
    }
}

// Sceglie la voce per nome. Un nome vuoto riporta alla voce predefinita di Windows.
static void ImpostaVoce(ISpVoice* voce, const std::wstring& nome) {
    if (nome.empty()) {
        ComPtr<ISpObjectToken> predefinita;
        if (SUCCEEDED(SpGetDefaultTokenFromCategoryId(SPCAT_VOICES, &predefinita)) && predefinita) voce->SetVoice(predefinita.Get());
        return;
    }
    ComPtr<IEnumSpObjectTokens> elenco;
    if (FAILED(SpEnumTokens(SPCAT_VOICES, NULL, NULL, &elenco)) || !elenco) return;
    std::wstring cercato = Minuscolo(nome);
    ULONG conto = 0;
    elenco->GetCount(&conto);
    for (ULONG i = 0; i < conto; i++) {
        ComPtr<ISpObjectToken> token;
        if (FAILED(elenco->Item(i, &token)) || !token) continue;
        WCHAR* descrizione = nullptr;
        if (SUCCEEDED(SpGetDescription(token.Get(), &descrizione)) && descrizione) {
            bool trovato = Minuscolo(descrizione).find(cercato) != std::wstring::npos;
            CoTaskMemFree(descrizione);
            if (trovato) {
                voce->SetVoice(token.Get());
                return;
            }
        }
    }
}

static void Interrompi(ISpVoice* voce) {
    voce->Pause();
    voce->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
    voce->Resume();
    if (g_flusso) g_flusso->InterrompiESfuma();
    g_frasiInCorso = 0;
    g_attendeFine = true;
}

static void EseguiComando(ISpVoice* voce, const std::wstring& testo) {
    try {
        size_t spazio = testo.find(L' ');
        std::wstring comando = Minuscolo(spazio == std::wstring::npos ? testo : testo.substr(0, spazio));
        std::wstring resto = spazio == std::wstring::npos ? L"" : testo.substr(spazio + 1);
        if (comando == L"parla") {
            bool interrompi = !resto.empty() && resto[0] == L'1';
            std::wstring frase = resto.size() > 2 ? resto.substr(2) : L"";
            if (frase.empty()) return;
            frase = PreparaTesto(frase);
            if (frase.empty()) return;
            if (interrompi) Interrompi(voce);
            g_attendeFine = false;
            if (SUCCEEDED(voce->Speak(frase.c_str(), SPF_ASYNC | SPF_IS_NOT_XML, NULL))) g_frasiInCorso++;
        } else if (comando == L"ferma") {
            Interrompi(voce);
        } else if (comando == L"velocita") {
            voce->SetRate((long)std::max(-10, std::min(10, _wtoi(resto.c_str()))));
        } else if (comando == L"volume") {
            g_volume = std::max(0, std::min(100, _wtoi(resto.c_str())));
        } else if (comando == L"voce") {
            ImpostaVoce(voce, resto);
        } else if (comando == L"esci") {
            g_continua = false;
        }
    } catch (...) {
    }
}

static int Esegui() {
    // Formato di uscita: mono a 16 bit alla frequenza del dispositivo predefinito, come in WGSapi.
    g_formato.wFormatTag = WAVE_FORMAT_PCM;
    g_formato.nChannels = 1;
    g_formato.nSamplesPerSec = 44100;
    g_formato.wBitsPerSample = 16;
    g_formato.nBlockAlign = (g_formato.nChannels * g_formato.wBitsPerSample) / 8;
    g_formato.nAvgBytesPerSec = g_formato.nSamplesPerSec * g_formato.nBlockAlign;
    {
        ComPtr<IMMDeviceEnumerator> enumeratore;
        ComPtr<IMMDevice> dispositivo;
        ComPtr<IAudioClient> client;
        if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_ALL, IID_PPV_ARGS(&enumeratore))) &&
            SUCCEEDED(enumeratore->GetDefaultAudioEndpoint(eRender, eConsole, &dispositivo)) &&
            SUCCEEDED(dispositivo->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (void**)client.GetAddressOf()))) {
            WAVEFORMATEX* nativo = nullptr;
            if (SUCCEEDED(client->GetMixFormat(&nativo)) && nativo) {
                if (nativo->nSamplesPerSec >= 8000) {
                    g_formato.nSamplesPerSec = nativo->nSamplesPerSec;
                    g_formato.nAvgBytesPerSec = g_formato.nSamplesPerSec * g_formato.nBlockAlign;
                }
                CoTaskMemFree(nativo);
            }
        }
    }

    g_flusso = new CFlusso();
    ComPtr<ISpVoice> voce;
    ComPtr<ISpStream> flussoSapi;
    if (FAILED(CoCreateInstance(CLSID_SpVoice, NULL, CLSCTX_ALL, IID_ISpVoice, (void**)voce.GetAddressOf())) ||
        FAILED(CoCreateInstance(CLSID_SpStream, NULL, CLSCTX_ALL, IID_ISpStream, (void**)flussoSapi.GetAddressOf()))) {
        g_flusso->Release();
        g_flusso = nullptr;
        return 2;
    }
    flussoSapi->SetBaseStream(g_flusso, SPDFID_WaveFormatEx, &g_formato);
    voce->SetOutput(flussoSapi.Get(), TRUE);
    voce->SetPriority(SPVPRI_OVER);
    voce->SetVolume(100);   // SAPI sintetizza a volume pieno; l'attenuazione la applica il renderer, cosi' ha effetto subito
    voce->SetNotifyWin32Event();
    HANDLE eventoSapi = voce->GetNotifyEventHandle();
    voce->SetInterest(SPFEI(SPEI_START_INPUT_STREAM) | SPFEI(SPEI_END_INPUT_STREAM), SPFEI(SPEI_START_INPUT_STREAM) | SPFEI(SPEI_END_INPUT_STREAM));

    g_eventoComandi = CreateEvent(NULL, FALSE, FALSE, NULL);
    std::thread wasapi(ThreadWasapi);
    std::thread ingresso(ThreadIngresso);
    ingresso.detach();

    HANDLE attese[2] = { g_eventoComandi, eventoSapi };
    DWORD quante = eventoSapi ? 2 : 1;
    while (g_continua) {
        try {
            DWORD r = MsgWaitForMultipleObjects(quante, attese, FALSE, 500, QS_ALLINPUT);
            MSG msg;
            while (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE)) {
                TranslateMessage(&msg);
                DispatchMessage(&msg);
            }
            // Eventi di SAPI: l'inizio di una frase e la fine della sintesi (non ancora dell'audio, che deve uscire dal dispositivo).
            CSpEvent evento;
            while (evento.GetFrom(voce.Get()) == S_OK) {
                if (evento.eEventId == SPEI_START_INPUT_STREAM) {
                    if (!g_staParlando.exchange(true)) Scrivi("STATO 1");
                } else if (evento.eEventId == SPEI_END_INPUT_STREAM) {
                    if (g_frasiInCorso.load() > 0) g_frasiInCorso--;
                    if (g_frasiInCorso.load() == 0) g_attendeFine = true;
                }
                evento.Clear();
            }
            if (r == WAIT_OBJECT_0) {
                while (g_continua) {
                    std::wstring comando;
                    {
                        std::lock_guard<std::mutex> lock(g_codaMutex);
                        if (g_coda.empty()) break;
                        comando = g_coda.front();
                        g_coda.pop();
                    }
                    EseguiComando(voce.Get(), comando);
                }
            }
        } catch (...) {
            Sleep(10);
        }
    }

    voce->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
    g_continua = false;
    if (wasapi.joinable()) wasapi.join();
    voce.Reset();
    flussoSapi.Reset();
    g_flusso->Release();
    g_flusso = nullptr;
    return 0;
}

int wmain() {
    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);
    if (FAILED(CoInitializeEx(NULL, COINIT_APARTMENTTHREADED))) return 1;
    int esito = 3;
    try {
        esito = Esegui();
    } catch (...) {
        g_continua = false;
        esito = 3;
    }
    CoUninitialize();
    return esito;
}
