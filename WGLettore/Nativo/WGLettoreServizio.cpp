// WGLettoreServizio: porta il lettore di schermo sulla schermata di accesso, su quella di blocco e sul controllo
// account utente (UAC), senza firma digitale e senza UIAccess.
//
// Queste schermate stanno sul "desktop sicuro" (Winlogon), che un programma dell'utente non può aprire. Questo servizio gira
// come SYSTEM, avviato da Windows: quando compare una di quelle schermate lancia nella stessa sessione, sul desktop Winlogon,
// una seconda copia del lettore (WGLettore.exe /sicuro). Quando la schermata sparisce, chiude quella copia.
//   - consent.exe  esiste soltanto mentre Windows mostra il prompt UAC;
//   - LogonUI.exe  esiste mentre c'è la schermata di accesso o di blocco.
// Il servizio non legge niente e non scrive file: tutto il lavoro lo fa il lettore. Non produce log.
//
// Uso (da amministratore, e solo dalla cartella Programmi):
//   WGLettoreServizio.exe --install     registra e avvia il servizio
//   WGLettoreServizio.exe --uninstall   lo ferma e lo toglie
// Il programma deve stare in Programmi\WGLettore, scrivibile solo dagli amministratori: un servizio SYSTEM non deve mai partire
// da una cartella che un utente normale può modificare. L'installazione rifiuta qualunque altra posizione.

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <userenv.h>
#include <wtsapi32.h>
#include <string>

#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "userenv.lib")
#pragma comment(lib, "wtsapi32.lib")

static const wchar_t* NOME_SERVIZIO = L"WGLettoreServizio";
static const wchar_t* NOME_VISIBILE = L"WinGabriel, lettore di schermo su accesso, blocco e UAC";
static const wchar_t* ESEGUIBILE_LETTORE = L"WGLettore.exe";

static std::wstring PercorsoProprio() {
    wchar_t buf[MAX_PATH] = {0};
    GetModuleFileNameW(NULL, buf, MAX_PATH);
    return buf;
}

static std::wstring CartellaProgramma() {
    std::wstring p = PercorsoProprio();
    size_t i = p.find_last_of(L"\\/");
    return i == std::wstring::npos ? L"." : p.substr(0, i);
}

// Il primo processo con quel nome nella sessione indicata (0xFFFFFFFF = qualunque sessione).
static DWORD TrovaPid(const wchar_t* nome, DWORD sessione) {
    DWORD trovato = 0;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W e;
    e.dwSize = sizeof(e);
    if (Process32FirstW(snap, &e)) {
        do {
            if (_wcsicmp(e.szExeFile, nome) != 0) continue;
            if (sessione != 0xFFFFFFFF) {
                DWORD s = 0;
                if (!ProcessIdToSessionId(e.th32ProcessID, &s) || s != sessione) continue;
            }
            trovato = e.th32ProcessID;
            break;
        } while (Process32NextW(snap, &e));
    }
    CloseHandle(snap);
    return trovato;
}

static void AbilitaPrivilegio(const wchar_t* nome) {
    HANDLE tok = NULL;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &tok)) return;
    TOKEN_PRIVILEGES tp = {};
    tp.PrivilegeCount = 1;
    if (LookupPrivilegeValueW(NULL, nome, &tp.Privileges[0].Luid)) {
        tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
        AdjustTokenPrivileges(tok, FALSE, &tp, 0, NULL, NULL);
    }
    CloseHandle(tok);
}

// Lancia WGLettore.exe /sicuro nella sessione indicata, sul desktop Winlogon, con il token di winlogon.exe di quella sessione.
static HANDLE LanciaLettore(DWORD sessione) {
    DWORD pidWinlogon = TrovaPid(L"winlogon.exe", sessione);
    if (!pidWinlogon) return NULL;
    AbilitaPrivilegio(SE_DEBUG_NAME);
    HANDLE processoWinlogon = OpenProcess(PROCESS_QUERY_INFORMATION, FALSE, pidWinlogon);
    if (!processoWinlogon) return NULL;

    HANDLE tokenOriginale = NULL, token = NULL, risultato = NULL;
    if (OpenProcessToken(processoWinlogon, TOKEN_DUPLICATE | TOKEN_QUERY | TOKEN_ASSIGN_PRIMARY, &tokenOriginale) &&
        DuplicateTokenEx(tokenOriginale, MAXIMUM_ALLOWED, NULL, SecurityImpersonation, TokenPrimary, &token)) {
        LPVOID ambiente = NULL;
        CreateEnvironmentBlock(&ambiente, token, FALSE);
        STARTUPINFOW si = {};
        si.cb = sizeof(si);
        si.lpDesktop = const_cast<LPWSTR>(L"winsta0\\Winlogon");
        PROCESS_INFORMATION pi = {};
        std::wstring cartella = CartellaProgramma();
        std::wstring comando = L"\"" + cartella + L"\\" + ESEGUIBILE_LETTORE + L"\" /sicuro";
        if (CreateProcessAsUserW(token, NULL, &comando[0], NULL, NULL, FALSE, CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, ambiente, cartella.c_str(), &si, &pi)) {
            CloseHandle(pi.hThread);
            risultato = pi.hProcess;
        }
        if (ambiente) DestroyEnvironmentBlock(ambiente);
    }
    if (token) CloseHandle(token);
    if (tokenOriginale) CloseHandle(tokenOriginale);
    CloseHandle(processoWinlogon);
    return risultato;
}

// ---------------------------------------------------------------------------------------------------------
// Servizio
// ---------------------------------------------------------------------------------------------------------

static SERVICE_STATUS_HANDLE g_stato = NULL;
static SERVICE_STATUS g_info = {};
static HANDLE g_fermo = NULL;

static void ImpostaStato(DWORD stato, DWORD codice = NO_ERROR) {
    g_info.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
    g_info.dwCurrentState = stato;
    g_info.dwWin32ExitCode = codice;
    g_info.dwControlsAccepted = (stato == SERVICE_START_PENDING) ? 0 : (SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN);
    g_info.dwWaitHint = (stato == SERVICE_START_PENDING || stato == SERVICE_STOP_PENDING) ? 5000 : 0;
    SetServiceStatus(g_stato, &g_info);
}

static DWORD WINAPI GestoreServizio(DWORD controllo, DWORD, LPVOID, LPVOID) {
    switch (controllo) {
    case SERVICE_CONTROL_STOP:
    case SERVICE_CONTROL_SHUTDOWN:
        ImpostaStato(SERVICE_STOP_PENDING);
        SetEvent(g_fermo);
        return NO_ERROR;
    case SERVICE_CONTROL_INTERROGATE:
        return NO_ERROR;
    default:
        return ERROR_CALL_NOT_IMPLEMENTED;
    }
}

static void ChiudiLettore(HANDLE lettore) {
    // Il lettore si chiude da solo quando riceve ESCI dalla sua pipe; qui, senza complicazioni, si termina il processo.
    TerminateProcess(lettore, 0);
    CloseHandle(lettore);
}

static void WINAPI PrincipaleServizio(DWORD, LPWSTR*) {
    g_stato = RegisterServiceCtrlHandlerExW(NOME_SERVIZIO, GestoreServizio, NULL);
    if (!g_stato) return;
    g_fermo = CreateEventW(NULL, TRUE, FALSE, NULL);
    ImpostaStato(SERVICE_START_PENDING);
    ImpostaStato(SERVICE_RUNNING);

    HANDLE lettore = NULL;
    ULONGLONG ultimoLancio = 0, ultimaPresenza = 0;
    while (WaitForSingleObject(g_fermo, 150) == WAIT_TIMEOUT) {
        try {
            if (lettore && WaitForSingleObject(lettore, 0) == WAIT_OBJECT_0) {
                CloseHandle(lettore);
                lettore = NULL;
            }
            DWORD sessione = WTSGetActiveConsoleSessionId();
            bool schermataSicura = false;
            if (sessione != 0xFFFFFFFF) {
                schermataSicura = TrovaPid(L"consent.exe", sessione) != 0 || TrovaPid(L"LogonUI.exe", sessione) != 0;
            }
            ULONGLONG adesso = GetTickCount64();
            if (schermataSicura) {
                ultimaPresenza = adesso;
                if (!lettore && adesso - ultimoLancio > 1000) {
                    ultimoLancio = adesso;
                    lettore = LanciaLettore(sessione);
                }
            } else if (lettore && adesso - ultimaPresenza > 1500) {
                ChiudiLettore(lettore);
                lettore = NULL;
            }
        } catch (...) {
        }
    }
    if (lettore) ChiudiLettore(lettore);
    CloseHandle(g_fermo);
    ImpostaStato(SERVICE_STOPPED);
}

// ---------------------------------------------------------------------------------------------------------
// Installazione
// ---------------------------------------------------------------------------------------------------------

static bool EAmministratore() {
    BOOL ok = FALSE;
    HANDLE tok = NULL;
    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &tok)) {
        TOKEN_ELEVATION el = {};
        DWORD n = 0;
        if (GetTokenInformation(tok, TokenElevation, &el, sizeof(el), &n)) ok = el.TokenIsElevated;
        CloseHandle(tok);
    }
    return ok != FALSE;
}

// Vero se questo programma sta dentro la cartella Programmi (o Programmi x86).
static bool DentroProgrammi() {
    std::wstring percorso = PercorsoProprio();
    const wchar_t* variabili[] = { L"ProgramFiles", L"ProgramFiles(x86)", L"ProgramW6432" };
    for (const wchar_t* v : variabili) {
        wchar_t buf[MAX_PATH] = {0};
        if (!GetEnvironmentVariableW(v, buf, MAX_PATH)) continue;
        std::wstring base = std::wstring(buf) + L"\\";
        if (percorso.size() > base.size() && _wcsnicmp(percorso.c_str(), base.c_str(), base.size()) == 0) return true;
    }
    return false;
}

static void FermaServizio(SC_HANDLE servizio) {
    SERVICE_STATUS st = {};
    if (ControlService(servizio, SERVICE_CONTROL_STOP, &st)) {
        for (int i = 0; i < 50; i++) {
            if (!QueryServiceStatus(servizio, &st) || st.dwCurrentState == SERVICE_STOPPED) break;
            Sleep(100);
        }
    }
}

static int Installa() {
    if (!EAmministratore()) return 5;
    if (!DentroProgrammi()) return 6;
    SC_HANDLE gestore = OpenSCManagerW(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!gestore) return 1;
    std::wstring comando = L"\"" + PercorsoProprio() + L"\" --service";
    SC_HANDLE servizio = OpenServiceW(gestore, NOME_SERVIZIO, SERVICE_ALL_ACCESS);
    if (servizio) {
        FermaServizio(servizio);
        ChangeServiceConfigW(servizio, SERVICE_NO_CHANGE, SERVICE_AUTO_START, SERVICE_NO_CHANGE, comando.c_str(), NULL, NULL, NULL, NULL, NULL, NOME_VISIBILE);
    } else {
        servizio = CreateServiceW(gestore, NOME_SERVIZIO, NOME_VISIBILE, SERVICE_ALL_ACCESS, SERVICE_WIN32_OWN_PROCESS,
                                  SERVICE_AUTO_START, SERVICE_ERROR_NORMAL, comando.c_str(), NULL, NULL, NULL, NULL, NULL);
    }
    if (!servizio) {
        CloseServiceHandle(gestore);
        return 1;
    }
    SERVICE_DESCRIPTIONW descrizione = { const_cast<LPWSTR>(L"Porta il lettore di schermo sulla schermata di accesso, su quella di blocco e sul controllo account utente.") };
    ChangeServiceConfig2W(servizio, SERVICE_CONFIG_DESCRIPTION, &descrizione);
    SC_ACTION azioni[2] = { {SC_ACTION_RESTART, 5000}, {SC_ACTION_RESTART, 15000} };
    SERVICE_FAILURE_ACTIONSW ripartenza = {};
    ripartenza.dwResetPeriod = 86400;
    ripartenza.cActions = 2;
    ripartenza.lpsaActions = azioni;
    ChangeServiceConfig2W(servizio, SERVICE_CONFIG_FAILURE_ACTIONS, &ripartenza);
    int esito = 0;
    if (!StartServiceW(servizio, 0, NULL) && GetLastError() != ERROR_SERVICE_ALREADY_RUNNING) esito = 1;
    CloseServiceHandle(servizio);
    CloseServiceHandle(gestore);
    return esito;
}

static int Disinstalla() {
    if (!EAmministratore()) return 5;
    SC_HANDLE gestore = OpenSCManagerW(NULL, NULL, SC_MANAGER_ALL_ACCESS);
    if (!gestore) return 1;
    int esito = 0;
    SC_HANDLE servizio = OpenServiceW(gestore, NOME_SERVIZIO, SERVICE_ALL_ACCESS | DELETE);
    if (servizio) {
        FermaServizio(servizio);
        if (!DeleteService(servizio) && GetLastError() != ERROR_SERVICE_MARKED_FOR_DELETE) esito = 1;
        CloseServiceHandle(servizio);
    }
    CloseServiceHandle(gestore);
    return esito;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, LPWSTR, int) {
    std::wstring riga = GetCommandLineW();
    if (riga.find(L"--install") != std::wstring::npos) return Installa();
    if (riga.find(L"--uninstall") != std::wstring::npos) return Disinstalla();
    SERVICE_TABLE_ENTRYW tabella[] = { { const_cast<LPWSTR>(NOME_SERVIZIO), PrincipaleServizio }, { NULL, NULL } };
    if (!StartServiceCtrlDispatcherW(tabella)) return 1;
    return 0;
}
