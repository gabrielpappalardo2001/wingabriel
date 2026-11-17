#define WIN32_LEAN_AND_MEAN
#define _WINSOCK_DEPRECATED_NO_WARNINGS
#include <winsock2.h>
#include <ws2tcpip.h>
#include <mstcpip.h>
#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <sapi.h>
#include <sphelper.h>
#include <string>
#include <mutex>
#include <thread>
#include <atomic>
#include <queue>
#include <deque>
#include <vector>
#include <algorithm>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <fstream>
#include <chrono>
#include <ctime>
#include <cstdlib>
#include <wrl/client.h>
#include <stdexcept>
#include <Functiondiscoverykeys_devpkey.h>
#include <psapi.h>
#include <eh.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <map>
#include <memory>

#ifndef NT_SUCCESS
#define NT_SUCCESS(Status) (((NTSTATUS)(Status)) >= 0)
#endif

#ifndef AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
#define AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM 0x80000000
#endif
#ifndef AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY
#define AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY 0x08000000
#endif

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "bcrypt.lib")
#pragma comment(lib, "crypt32.lib")
#pragma comment(lib, "psapi.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mf.lib")
#pragma comment(lib, "mfreadwrite.lib")
#pragma comment(lib, "mfuuid.lib")

constexpr size_t MAX_LOG_FILE_SIZE = 5242880;
constexpr int MAX_INVALID_PACKETS = 5;
constexpr int TCP_BUFFER_SIZE = 65536;
constexpr int KEEPALIVE_TIME = 5000;
constexpr int KEEPALIVE_INTERVAL = 1000;
constexpr int HANDSHAKE_TIMEOUT_MS = 5000;
constexpr const char* BIND_IP = "127.0.0.1";
constexpr const wchar_t* SAPI_ONECORE_REG_KEY = L"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Speech_OneCore\\Voices";

enum class TransportType { None, TCP, Pipe, SHM };
std::atomic<TransportType> activeTransport{TransportType::None};
std::mutex transportMutex;

SOCKET hSocket = INVALID_SOCKET;
HANDLE globalPipe = INVALID_HANDLE_VALUE;

HANDLE hShmMap = NULL;
LPVOID pShmC2S = NULL;
LPVOID pShmS2C = NULL;
HANDLE hShmEventC2S = NULL;
HANDLE hShmEventC2SAck = NULL;
HANDLE hShmEventS2C = NULL;
HANDLE hShmEventS2CAck = NULL;

std::atomic<bool> keepRunning{true};
std::atomic<bool> needsWasapiReset{false};

struct SpeechItem {
    int msgId;
    int seq;
    int isLast;
    int offsetGlobale;
    std::wstring text;
};

std::deque<SpeechItem> speechQueue;
std::mutex speechQueueMutex;

std::atomic<int> currentMsgId{-1};
std::atomic<int> currentSeq{-1};
std::atomic<int> currentIsLast{0};
std::atomic<int> currentOffsetGlobale{0};
std::atomic<bool> sapiIsSpeaking{false};
std::atomic<int> pendingDoneMsgId{-1};
std::atomic<ULONGLONG> tickBufferVuotoDaQuando{0};
std::atomic<int> lastEnqueuedMsgId{-1};
std::atomic<int> latenzaDispositivoMs{60};

// --- Rilevamento preciso di fine riproduzione basato sull'orologio hardware ---
// totalFramesRenderedToDevice: conteggio cumulativo dei frame realmente consegnati
// al buffer condiviso WASAPI dall'avvio dello stream corrente (si azzera solo
// quando lo stream viene ricreato/resettato, cioè quando si azzera anche l'orologio
// hardware corrispondente).
std::atomic<ULONGLONG> totalFramesRenderedToDevice{0};
// pendingDoneTargetHns: istante (in unita' di 100ns, stessa scala di REFERENCE_TIME)
// dell'orologio hardware in cui l'ULTIMO campione del messaggio "pendingDoneMsgId"
// diventera' realmente udibile. -1 = nessun target valido impostato.
std::atomic<LONGLONG> pendingDoneTargetHns{-1};

HANDLE hCommandEvent = NULL;

class CommandQueues {
private:
    std::queue<std::wstring> tQueue;
    std::queue<std::wstring> cQueue;
    std::mutex m;
    HANDLE hEvent = NULL;
public:
    void Init(HANDLE h) { hEvent = h; }
    void PushText(const std::wstring& cmd) {
        std::lock_guard<std::mutex> lock(m);
        if (tQueue.size() > 1000) {
            std::queue<std::wstring> empty;
            std::swap(tQueue, empty);
        }
        tQueue.push(cmd);
        if (hEvent) SetEvent(hEvent);
    }
    void PushCtrl(const std::wstring& cmd) {
        std::lock_guard<std::mutex> lock(m);
        cQueue.push(cmd);
        if (hEvent) SetEvent(hEvent);
    }
    bool PopCommand(std::wstring& outCmd) {
        std::lock_guard<std::mutex> lock(m);
        if (!cQueue.empty()) {
            outCmd = cQueue.front();
            cQueue.pop();
            return true;
        }
        if (!tQueue.empty()) {
            outCmd = tQueue.front();
            tQueue.pop();
            return true;
        }
        return false;
    }
    void ClearText() {
        std::lock_guard<std::mutex> lock(m);
        std::queue<std::wstring> empty;
        std::swap(tQueue, empty);
    }
};

CommandQueues globalQueues;

WAVEFORMATEX globalWfx = {};

std::atomic<std::time_t> lastDisconnectTime{ std::time(nullptr) };
std::atomic<std::time_t> lastPingTime{ 0 };
std::atomic<bool> isClientConnected{ false };
std::string globalAuthToken = "";
int globalPort = 44445;

std::wstring globalTargetAudioDevice = L"";
std::mutex deviceMutex;

// Impostato a true quando Windows notifica un cambio nella topologia audio:
// dispositivo predefinito cambiato, dispositivo aggiunto/rimosso, o stato
// del dispositivo attualmente selezionato non piu' attivo. Il thread WASAPI
// lo controlla per forzare una riselezione completa del device (non un
// semplice Reset, ma una nuova GetDevice/GetDefaultAudioEndpoint).
std::atomic<bool> deviceTopologyChanged{false};

struct CryptoState {
    bool isEncrypted = false;
    BCRYPT_ALG_HANDLE hAesAlg = NULL;
    BCRYPT_KEY_HANDLE hKey = NULL;
    std::vector<BYTE> pbKeyObject;
    std::vector<BYTE> globalIV;
    std::vector<BYTE> sessionKey;

    CryptoState() {
        globalIV.resize(16, 0);
        sessionKey.resize(32, 0);
    }

    ~CryptoState() {
        Reset();
    }

    void Reset() {
        isEncrypted = false;
        if (hKey) {
            BCryptDestroyKey(hKey);
            hKey = NULL;
        }
        if (hAesAlg) {
            BCryptCloseAlgorithmProvider(hAesAlg, 0);
            hAesAlg = NULL;
        }
        SecureZeroMemory(sessionKey.data(), sessionKey.size());
        SecureZeroMemory(globalIV.data(), globalIV.size());
        pbKeyObject.clear();
    }
};

CryptoState globalCrypto;

std::mutex logMutex;
std::ofstream logStream;
std::string currentLogPath;
std::string oldLogPath;

void SecureClearStr(std::string& str) {
    str.resize(str.capacity(), 0);
    SecureZeroMemory(&str[0], str.size());
    str.clear();
}

std::wstring Utf8ToWide(const std::string& str) {
    if(str.empty()) return std::wstring();
    int size_needed = MultiByteToWideChar(CP_UTF8, 0, str.c_str(), (int)str.length(), NULL, 0);
    std::wstring wstr(size_needed, 0);
    MultiByteToWideChar(CP_UTF8, 0, str.c_str(), (int)str.length(), &wstr[0], size_needed);
    return wstr;
}

std::string WideToUtf8(const std::wstring& wstr) {
    if(wstr.empty()) return std::string();
    int size_needed = WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.length(), NULL, 0, NULL, NULL);
    std::string str(size_needed, 0);
    WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.length(), &str[0], size_needed, NULL, NULL);
    return str;
}

std::wstring SanitizeTestoParlato(const std::wstring& input) {
    std::wstring output;
    output.reserve(input.size());
    for (wchar_t c : input) {
        if (c == L'\t' || c == L'\n' || c == L'\r') {
            output += L' ';
        } else if (c < 0x20 || c == 0x7F) {
            continue;
        } else {
            output += c;
        }
    }
    return output;
}

std::string HexEncode(const std::string& input) {
    static const char* const lut = "0123456789ABCDEF";
    std::string output;
    output.reserve(2 * input.length());
    for (unsigned char c : input) {
        output.push_back(lut[c >> 4]);
        output.push_back(lut[c & 15]);
    }
    return output;
}

std::string HexDecode(const std::string& input) {
    std::string output;
    if (input.length() % 2 != 0) return output;
    
    for (char c : input) {
        if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))) {
            return "";
        }
    }

    output.reserve(input.length() / 2);
    for (size_t i = 0; i < input.length(); i += 2) {
        std::string byteString = input.substr(i, 2);
        char byte = (char)strtol(byteString.c_str(), NULL, 16);
        output.push_back(byte);
    }
    return output;
}

std::string ExtractXmlTag(const std::string& xml, const std::string& tag) {
    std::string startTag = "<" + tag + ">";
    std::string endTag = "</" + tag + ">";
    size_t start = xml.find(startTag);
    if (start == std::string::npos) return "";
    start += startTag.length();
    size_t end = xml.find(endTag, start);
    if (end == std::string::npos) return "";
    return xml.substr(start, end - start);
}

void LogToFile(const std::string& msg) {
    try {
        std::lock_guard<std::mutex> lock(logMutex);
        if (currentLogPath.empty()) {
            char path[MAX_PATH];
            GetModuleFileNameA(NULL, path, MAX_PATH);
            std::string exePath(path);
            size_t pos = exePath.find_last_of("\\/");
            std::string baseDir = (pos != std::string::npos) ? exePath.substr(0, pos) : "";
            std::string datiDir = baseDir + "\\dati";
            std::string logsDir = datiDir + "\\logs";
            CreateDirectoryA(datiDir.c_str(), NULL);
            CreateDirectoryA(logsDir.c_str(), NULL);
            currentLogPath = logsDir + "\\logsapi.txt";
            oldLogPath = logsDir + "\\logsapi_old.txt";
        }

        if (!logStream.is_open()) {
            logStream.open(currentLogPath, std::ios_base::app | std::ios_base::ate);
        }

        if (logStream.is_open()) {
            logStream.seekp(0, std::ios::end);
            if (logStream.tellp() > MAX_LOG_FILE_SIZE) {
                logStream.close();
                remove(oldLogPath.c_str());
                rename(currentLogPath.c_str(), oldLogPath.c_str());
                logStream.open(currentLogPath, std::ios_base::app);
            }

            auto now = std::chrono::system_clock::now();
            std::time_t now_c = std::chrono::system_clock::to_time_t(now);
            struct tm parts;
            localtime_s(&parts, &now_c);
            char timeStr[64];
            strftime(timeStr, sizeof(timeStr), "%Y-%m-%d %H:%M:%S", &parts);
            logStream << timeStr << " - [WGSapi] " << msg << "\n";
            logStream.flush();
        }
    } catch (...) {}
}

void SendRawData(const std::string& data) {
    try {
        std::lock_guard<std::mutex> lock(transportMutex);
        if (activeTransport == TransportType::TCP && hSocket != INVALID_SOCKET) {
            send(hSocket, data.c_str(), (int)data.length(), 0);
        } else if (activeTransport == TransportType::Pipe && globalPipe != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            WriteFile(globalPipe, data.c_str(), (DWORD)data.length(), &written, NULL);
        } else if (activeTransport == TransportType::SHM && pShmS2C != nullptr) {
            DWORD len = (DWORD)data.length();
            if (len > 65532) len = 65532;
            memcpy(pShmS2C, &len, sizeof(DWORD));
            memcpy((BYTE*)pShmS2C + sizeof(DWORD), data.c_str(), len);
            SetEvent(hShmEventS2C);
            WaitForSingleObject(hShmEventS2CAck, 100);
        }
    } catch (const std::exception& e) {
        LogToFile(std::string("Errore in SendRawData: ") + e.what());
    } catch (...) {
        LogToFile("Errore sconosciuto in SendRawData");
    }
}

void SendEvent(const std::wstring& msg) {
    try {
        std::string utf8_msg = WideToUtf8(msg);
        
        if (globalCrypto.isEncrypted && globalCrypto.hKey) {
            ULONG cbData = 0;
            std::vector<BYTE> ivCopy = globalCrypto.globalIV;
            NTSTATUS status = BCryptEncrypt(globalCrypto.hKey, (PUCHAR)utf8_msg.data(), utf8_msg.length(), NULL, ivCopy.data(), ivCopy.size(), NULL, 0, &cbData, BCRYPT_BLOCK_PADDING);
            if (NT_SUCCESS(status)) {
                std::vector<BYTE> cipher(cbData, 0);
                ivCopy = globalCrypto.globalIV;
                status = BCryptEncrypt(globalCrypto.hKey, (PUCHAR)utf8_msg.data(), utf8_msg.length(), NULL, ivCopy.data(), ivCopy.size(), cipher.data(), cipher.size(), &cbData, BCRYPT_BLOCK_PADDING);
                if (NT_SUCCESS(status)) {
                    utf8_msg = HexEncode(std::string((char*)cipher.data(), cbData));
                }
            }
        }
        
        utf8_msg += "\n";
        SendRawData(utf8_msg);
        SecureClearStr(utf8_msg);
    } catch (const std::exception& e) {
        LogToFile(std::string("Errore in SendEvent: ") + e.what());
        keepRunning = false;
        if (hCommandEvent) SetEvent(hCommandEvent);
    } catch (...) {
        LogToFile("Errore sconosciuto in SendEvent");
        keepRunning = false;
        if (hCommandEvent) SetEvent(hCommandEvent);
    }
}

void ReportServerError(const std::string& moduleName, const std::string& errorDetail) {
    LogToFile("ERRORE [" + moduleName + "]: " + errorDetail);
    try {
        std::wstring wMsg = L"WGSAPI_ERROR|" + Utf8ToWide(moduleName) + L"|" + Utf8ToWide(errorDetail);
        SendEvent(wMsg);
    } catch (...) {}
}

void SendShutdownSignal(const std::wstring& reason) {
    try {
        SendEvent(L"WGSAPI_SHUTDOWN|" + reason);
    } catch (...) {}
}

LONG WINAPI TopLevelExceptionHandler(PEXCEPTION_POINTERS pExceptionInfo) {
    std::string errCode = std::to_string(pExceptionInfo->ExceptionRecord->ExceptionCode);
    std::string errAddr = std::to_string((uintptr_t)pExceptionInfo->ExceptionRecord->ExceptionAddress);
    ReportServerError("CRITICAL_CRASH", "Eccezione hardware fatale intercettata. Codice: " + errCode + " Indirizzo: " + errAddr);
    keepRunning = false;
    if (hCommandEvent) SetEvent(hCommandEvent);
    return EXCEPTION_EXECUTE_HANDLER;
}

void MemoryWatchdogThreadFunc() {
    try {
        while (keepRunning) {
            std::this_thread::sleep_for(std::chrono::seconds(10));
            PROCESS_MEMORY_COUNTERS pmc;
            if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
                if (pmc.WorkingSetSize > 314572800) { 
                    SetProcessWorkingSetSize(GetCurrentProcess(), (SIZE_T)-1, (SIZE_T)-1);
                    EmptyWorkingSet(GetCurrentProcess());
                    HeapCompact(GetProcessHeap(), 0);
                }
            }
        }
    } catch (const std::exception& e) {
        ReportServerError("MemoryWatchdogThreadFunc", e.what());
    } catch (...) {
        ReportServerError("MemoryWatchdogThreadFunc", "Errore sconosciuto");
    }
}

// Volume applicato direttamente sui campioni al momento della consegna al dispositivo
// audio. Non si usa ISpVoice::SetVolume perche' SAPI applica il guadagno durante la
// sintesi: quando il comando SET_VOLUME arriva, l'audio della frase in corso e' gia'
// stato generato e si trova nel buffer, quindi il nuovo volume si sentirebbe solo a
// partire dalla frase successiva. Applicandolo qui il cambiamento e' immediato.
std::atomic<int> volumeCorrente{100};

class CWasapiStream : public IStream {
private:
    LONG m_refCount;
    std::mutex m_mutex;
    std::string m_buffer;
    size_t m_readPos;
    bool m_aborted;
    double m_guadagnoApplicato;
public:
    CWasapiStream() : m_refCount(1), m_readPos(0), m_aborted(false), m_guadagnoApplicato(1.0) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppv) override {
        if (riid == IID_IUnknown || riid == IID_ISequentialStream || riid == IID_IStream) {
            *ppv = static_cast<IStream*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = NULL;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&m_refCount); }
    ULONG STDMETHODCALLTYPE Release() override {
        ULONG ref = InterlockedDecrement(&m_refCount);
        if (ref == 0) delete this;
        return ref;
    }
    HRESULT STDMETHODCALLTYPE Read(void* pv, ULONG cb, ULONG* pcbRead) override { return E_NOTIMPL; }
    
    HRESULT STDMETHODCALLTYPE Write(const void* pv, ULONG cb, ULONG* pcbWritten) override {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (pcbWritten) *pcbWritten = cb;
            if (m_aborted) return S_OK;
            m_buffer.append((const char*)pv, cb);
            return S_OK;
        } catch (const std::exception& e) {
            ReportServerError("WASAPI_STREAM_WRITE", e.what());
            return E_FAIL;
        } catch (...) {
            ReportServerError("WASAPI_STREAM_WRITE", "Errore sconosciuto");
            return E_FAIL;
        }
    }
    
    HRESULT STDMETHODCALLTYPE Seek(LARGE_INTEGER, DWORD, ULARGE_INTEGER*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE SetSize(ULARGE_INTEGER) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE CopyTo(IStream*, ULARGE_INTEGER, ULARGE_INTEGER*, ULARGE_INTEGER*) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Commit(DWORD) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Revert() override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE LockRegion(ULARGE_INTEGER, ULARGE_INTEGER, DWORD) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE UnlockRegion(ULARGE_INTEGER, ULARGE_INTEGER, DWORD) override { return E_NOTIMPL; }
    HRESULT STDMETHODCALLTYPE Stat(STATSTG* pstatstg, DWORD) override {
        if (pstatstg) memset(pstatstg, 0, sizeof(STATSTG));
        return S_OK; 
    }
    HRESULT STDMETHODCALLTYPE Clone(IStream**) override { return E_NOTIMPL; }

    void AbortAndClear() {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);

            if (m_readPos >= m_buffer.size()) {
                m_buffer.clear();
                m_readPos = 0;
                m_aborted = false;
                return;
            }

            size_t avail = m_buffer.size() - m_readPos;

            if (avail >= 4) {
                size_t fadeSamples = 441;
                size_t fadeBytes = fadeSamples * 2;
                if (fadeBytes > avail) {
                    fadeBytes = avail;
                    if (fadeBytes % 2 != 0) fadeBytes--;
                }
                size_t numSamples = fadeBytes / 2;

                int16_t* samples = reinterpret_cast<int16_t*>(&m_buffer[m_readPos]);
                for (size_t i = 0; i < numSamples; i++) {
                    double gain = 1.0 - (static_cast<double>(i) / static_cast<double>(numSamples));
                    int32_t v = static_cast<int32_t>(samples[i] * gain);
                    if (v > 32767) v = 32767;
                    if (v < -32768) v = -32768;
                    samples[i] = static_cast<int16_t>(v);
                }

                m_buffer.resize(m_readPos + fadeBytes);
            } else {
                m_buffer.clear();
                m_readPos = 0;
            }

            m_aborted = false;
        } catch (const std::exception& e) {
            ReportServerError("WASAPI_STREAM_ABORT", e.what());
        } catch (...) {
            ReportServerError("WASAPI_STREAM_ABORT", "Errore sconosciuto");
        }
    }

    void ResetFlag() {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_aborted = false;
            m_buffer.clear();
            m_readPos = 0;
        } catch (const std::exception& e) {
            ReportServerError("WASAPI_STREAM_RESET", e.what());
        } catch (...) {
            ReportServerError("WASAPI_STREAM_RESET", "Errore sconosciuto");
        }
    }

    size_t GetAvailableBytes() {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (m_readPos >= m_buffer.size()) return 0;
            return m_buffer.size() - m_readPos;
        } catch (const std::exception& e) {
            ReportServerError("WASAPI_STREAM_GETBYTES", e.what());
            return 0;
        } catch (...) {
            ReportServerError("WASAPI_STREAM_GETBYTES", "Errore sconosciuto");
            return 0;
        }
    }

    size_t PullAudio(BYTE* data, size_t bytesToRead) {
        try {
            std::lock_guard<std::mutex> lock(m_mutex);
            if (m_readPos >= m_buffer.size()) {
                m_buffer.clear();
                m_readPos = 0;
                return 0;
            }
            size_t avail = m_buffer.size() - m_readPos;
            if (bytesToRead > avail) bytesToRead = avail;
            if (bytesToRead > 0) {
                memcpy(data, m_buffer.data() + m_readPos, bytesToRead);

                // Guadagno applicato con rampa lineare sui campioni appena copiati: la
                // rampa evita lo scatto udibile (click) quando il volume cambia mentre
                // la voce sta gia' parlando.
                int volumePercentuale = volumeCorrente.load();
                if (volumePercentuale < 0) volumePercentuale = 0;
                if (volumePercentuale > 100) volumePercentuale = 100;

                double guadagnoTarget = static_cast<double>(volumePercentuale) / 100.0;
                size_t numSamples = bytesToRead / 2;

                if (numSamples > 0 && (guadagnoTarget != 1.0 || m_guadagnoApplicato != 1.0)) {
                    int16_t* samples = reinterpret_cast<int16_t*>(data);
                    double guadagnoIniziale = m_guadagnoApplicato;
                    double passo = 0.0;
                    size_t campioniRampa = numSamples;

                    if (guadagnoIniziale != guadagnoTarget) {
                        size_t campioniMassimiRampa = 441;
                        if (campioniRampa > campioniMassimiRampa) campioniRampa = campioniMassimiRampa;
                        passo = (guadagnoTarget - guadagnoIniziale) / static_cast<double>(campioniRampa);
                    } else {
                        campioniRampa = 0;
                    }

                    for (size_t i = 0; i < numSamples; i++) {
                        double guadagno = guadagnoTarget;
                        if (i < campioniRampa) {
                            guadagno = guadagnoIniziale + (passo * static_cast<double>(i));
                        }
                        int32_t v = static_cast<int32_t>(samples[i] * guadagno);
                        if (v > 32767) v = 32767;
                        if (v < -32768) v = -32768;
                        samples[i] = static_cast<int16_t>(v);
                    }

                    m_guadagnoApplicato = guadagnoTarget;
                }

                m_readPos += bytesToRead;
                if (m_readPos >= m_buffer.size()) {
                    m_buffer.clear();
                    m_readPos = 0;
                } else if (m_readPos > 524288) {
                    m_buffer.erase(0, m_readPos);
                    m_readPos = 0;
                }
            }
            return bytesToRead;
        } catch (const std::exception& e) {
            ReportServerError("WASAPI_STREAM_PULL", e.what());
            return 0;
        } catch (...) {
            ReportServerError("WASAPI_STREAM_PULL", "Errore sconosciuto");
            return 0;
        }
    }
};

CWasapiStream* pGlobalStream = nullptr;

// --- Notifiche di sistema sui dispositivi audio -----------------------------
// Riceve le callback di Windows quando: cambia il dispositivo di riproduzione
// predefinito, un dispositivo viene aggiunto/rimosso, oppure lo stato di un
// dispositivo cambia (es. scollegato, disabilitato). In tutti questi casi
// alziamo semplicemente un flag: il thread WASAPI se ne accorge al giro
// successivo e riseleziona il device corretto (quello scelto esplicitamente,
// se ancora presente, altrimenti il nuovo predefinito di Windows).
class CMMNotificationClient : public IMMNotificationClient {
    LONG _refCount = 1;
public:
    STDMETHODIMP QueryInterface(REFIID riid, void** ppvObject) override {
        if (ppvObject == nullptr) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IMMNotificationClient)) {
            *ppvObject = static_cast<IMMNotificationClient*>(this);
            AddRef();
            return S_OK;
        }
        *ppvObject = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&_refCount); }
    STDMETHODIMP_(ULONG) Release() override {
        ULONG c = InterlockedDecrement(&_refCount);
        if (c == 0) delete this;
        return c;
    }

    STDMETHODIMP OnDefaultDeviceChanged(EDataFlow flow, ERole role, LPCWSTR /*pwstrDefaultDeviceId*/) override {
        // Ci interessa solo l'uscita audio (render) usata per la riproduzione
        // "normale" (eConsole) o multimediale (eMultimedia). Windows invia
        // questa notifica anche per eCommunications: la ignoriamo per evitare
        // switch superflui quando il comportamento riguarda solo le chiamate.
        if (flow == eRender && (role == eConsole || role == eMultimedia)) {
            // Se non e' stato fissato manualmente un device specifico,
            // seguiamo sempre il nuovo predefinito di sistema.
            deviceTopologyChanged = true;
            if (hCommandEvent) SetEvent(hCommandEvent);
        }
        return S_OK;
    }

    STDMETHODIMP OnDeviceAdded(LPCWSTR /*pwstrDeviceId*/) override {
        // Cuffie o casse appena collegate: alcuni driver segnalano prima l'arrivo del
        // dispositivo e solo dopo (o per niente) il cambio di predefinito. Riconsiderare
        // la scelta anche qui evita di restare sull'altoparlante quando Windows ha gia'
        // promosso la cuffia a dispositivo predefinito.
        deviceTopologyChanged = true;
        if (hCommandEvent) SetEvent(hCommandEvent);
        return S_OK;
    }

    STDMETHODIMP OnDeviceRemoved(LPCWSTR pwstrDeviceId) override {
        std::lock_guard<std::mutex> lock(deviceMutex);
        if (!globalTargetAudioDevice.empty() && globalTargetAudioDevice == pwstrDeviceId) {
            // Il device che era stato fissato esplicitamente non esiste piu':
            // torniamo a seguire il predefinito di sistema.
            globalTargetAudioDevice.clear();
        }
        deviceTopologyChanged = true;
        if (hCommandEvent) SetEvent(hCommandEvent);
        return S_OK;
    }

    STDMETHODIMP OnDeviceStateChanged(LPCWSTR pwstrDeviceId, DWORD dwNewState) override {
        if (dwNewState != DEVICE_STATE_ACTIVE) {
            std::lock_guard<std::mutex> lock(deviceMutex);
            if (!globalTargetAudioDevice.empty() && globalTargetAudioDevice == pwstrDeviceId) {
                globalTargetAudioDevice.clear();
            }
        }
        deviceTopologyChanged = true;
        if (hCommandEvent) SetEvent(hCommandEvent);
        return S_OK;
    }

    STDMETHODIMP OnPropertyValueChanged(LPCWSTR /*pwstrDeviceId*/, const PROPERTYKEY /*key*/) override {
        return S_OK;
    }
};

// Vale "segui il predefinito di sistema" sia la stringa vuota sia il valore
// esplicito PREDEFINITO: in quel caso l'uscita non viene mai fissata e resta
// sempre agganciata a quella che Windows considera la predefinita, cuffia
// compresa, senza bisogno che nessuno la reimposti a ogni collegamento.
static bool DeviceSeguePredefinito(const std::wstring& id) {
    if (id.empty()) return true;
    std::wstring maiuscolo = id;
    CharUpperBuffW(&maiuscolo[0], (DWORD)maiuscolo.length());
    return (maiuscolo == L"PREDEFINITO" || maiuscolo == L"DEFAULT");
}

// Rampa di circa un terzo di millisecondo sul solo stacco silenzio/voce.
// Non si allunga il buffer: i blocchi continui restano com'erano, la latenza non cambia.
static void AmmorbidisciStacco16(BYTE* dati, size_t byteAudio, bool inizia, bool finisce) {
    if (dati == nullptr || globalWfx.wBitsPerSample != 16 || globalWfx.nChannels != 1 || byteAudio < 4) return;
    size_t campioni = byteAudio / 2;
    int16_t* s = reinterpret_cast<int16_t*>(dati);
    size_t rampa = (size_t)(globalWfx.nSamplesPerSec / 3000);
    if (rampa < 8) rampa = 8;
    if (rampa > campioni) rampa = campioni;
    if (inizia) {
        for (size_t i = 0; i < rampa; ++i) {
            s[i] = (int16_t)((int32_t)s[i] * (int32_t)(i + 1) / (int32_t)rampa);
        }
    }
    if (finisce && campioni > 1) {
        size_t n = rampa;
        if (n > campioni) n = campioni;
        for (size_t i = 0; i < n; ++i) {
            size_t idx = campioni - n + i;
            s[idx] = (int16_t)((int32_t)s[idx] * (int32_t)(n - i) / (int32_t)n);
        }
    }
}

void WasapiThreadFunc() {
    try {
        CoInitializeEx(NULL, COINIT_MULTITHREADED);
        SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);

        // Enumeratore e notification client creati UNA SOLA VOLTA per tutta la
        // vita del thread: e' su questo enumeratore che restiamo registrati
        // per ricevere i cambi di dispositivo predefinito / plug-unplug anche
        // mentre siamo dentro il loop di riproduzione.
        IMMDeviceEnumerator* pPersistentEnumerator = nullptr;
        CMMNotificationClient* pNotifyClient = nullptr;
        bool notifyRegistered = false;

        if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), (void**)&pPersistentEnumerator))) {
            pNotifyClient = new CMMNotificationClient();
            if (SUCCEEDED(pPersistentEnumerator->RegisterEndpointNotificationCallback(pNotifyClient))) {
                notifyRegistered = true;
            }
        }

        while (keepRunning) {
            IMMDeviceEnumerator* pEnumerator = pPersistentEnumerator;
            if (pEnumerator) {

                // Il flag va azzerato PRIMA di interrogare il sistema. Azzerandolo dopo,
                // una notifica arrivata mentre stavamo interrogando l'enumeratore veniva
                // cancellata subito dopo: il cambio di dispositivo andava perso e l'audio
                // restava sulla vecchia scheda fino all'evento successivo.
                deviceTopologyChanged = false;

                std::wstring currentDevice;
                {
                    std::lock_guard<std::mutex> lock(deviceMutex);
                    currentDevice = globalTargetAudioDevice;
                }

                IMMDevice* pDevice = nullptr;
                bool deviceFound = false;

                if (DeviceSeguePredefinito(currentDevice)) {
                    currentDevice.clear();
                }

                if (!currentDevice.empty()) {
                    IMMDevice* pCandidate = nullptr;
                    if (SUCCEEDED(pEnumerator->GetDevice(currentDevice.c_str(), &pCandidate)) && pCandidate) {
                        DWORD state = 0;
                        // Il device pinnato esplicitamente deve anche essere
                        // ATTIVO: se e' stato scollegato/disabilitato usiamo
                        // subito il predefinito invece di provare ad aprirlo.
                        if (SUCCEEDED(pCandidate->GetState(&state)) && state == DEVICE_STATE_ACTIVE) {
                            pDevice = pCandidate;
                            deviceFound = true;
                        } else {
                            pCandidate->Release();
                            // Il device selezionato manualmente non e' piu'
                            // disponibile: torniamo a seguire il predefinito.
                            std::lock_guard<std::mutex> lock(deviceMutex);
                            if (globalTargetAudioDevice == currentDevice) {
                                globalTargetAudioDevice.clear();
                            }
                        }
                    }
                }

                if (!deviceFound) {
                    pEnumerator->GetDefaultAudioEndpoint(eRender, eConsole, &pDevice);
                }

                if (pDevice) {
                    bool topologyBreak = false;
                    bool aperturaRiuscita = false;
                    IAudioClient* pAudioClient = nullptr;
                    if (SUCCEEDED(pDevice->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (void**)&pAudioClient))) {
                        if (SUCCEEDED(pAudioClient->Initialize(AUDCLNT_SHAREMODE_SHARED, 
                                      AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY, 
                                      0, 0, &globalWfx, NULL))) {
                            aperturaRiuscita = true;
                            REFERENCE_TIME latenzaRT = 0;
                            if (SUCCEEDED(pAudioClient->GetStreamLatency(&latenzaRT))) {
                                int latenzaMisurataMs = (int)(latenzaRT / 10000);
                                latenzaDispositivoMs = latenzaMisurataMs + 20;
                            }

                            IAudioRenderClient* pRenderClient = nullptr;
                            if (SUCCEEDED(pAudioClient->GetService(__uuidof(IAudioRenderClient), (void**)&pRenderClient))) {

                                // IAudioClock: orologio hardware sample-accurate dello stesso stream.
                                // Riporta la posizione REALMENTE in uscita dal dispositivo (non solo
                                // quanto e' stato scritto nel buffer), la stessa tecnica usata dai
                                // player professionali per la sincronizzazione audio/video precisa.
                                IAudioClock* pAudioClock = nullptr;
                                UINT64 clockFreq = 0;
                                if (SUCCEEDED(pAudioClient->GetService(__uuidof(IAudioClock), (void**)&pAudioClock))) {
                                    if (FAILED(pAudioClock->GetFrequency(&clockFreq)) || clockFreq == 0) {
                                        pAudioClock->Release();
                                        pAudioClock = nullptr;
                                    }
                                }

                                // Nuovo stream = nuovo orologio: il conteggio cumulativo dei frame
                                // consegnati riparte da zero in sincronia con la posizione hardware.
                                totalFramesRenderedToDevice = 0;
                                pendingDoneTargetHns = -1;

                                pAudioClient->Start();

                                bool dopoBuco = true;
                                while (keepRunning) {
                                    // Windows ha notificato un cambio di dispositivo predefinito,
                                    // un plug/unplug, o il device attivo non e' piu' ATTIVO: usciamo
                                    // per riselezionare il device corretto dal loop esterno, invece
                                    // di continuare a scrivere sul device (ormai sbagliato/assente).
                                    if (deviceTopologyChanged.load()) {
                                        topologyBreak = true;
                                        break;
                                    }

                                    if (needsWasapiReset) {
                                        pAudioClient->Stop();
                                        pAudioClient->Reset();
                                        pAudioClient->Start();
                                        needsWasapiReset = false;
                                        // Reset() riporta a zero sia il buffer sia l'orologio hardware:
                                        // il nostro conteggio deve restare allineato.
                                        totalFramesRenderedToDevice = 0;
                                        dopoBuco = true;
                                    }

                                    UINT32 bufferFrameCount = 0;
                                    HRESULT hrBufSize = pAudioClient->GetBufferSize(&bufferFrameCount);
                                    UINT32 numFramesPadding = 0;
                                    HRESULT hrPad = pAudioClient->GetCurrentPadding(&numFramesPadding);
                                    if (hrPad == AUDCLNT_E_DEVICE_INVALIDATED) { topologyBreak = true; break; }
                                    if (FAILED(hrBufSize) || FAILED(hrPad) || numFramesPadding > bufferFrameCount) {
                                        Sleep(2);
                                        continue;
                                    }

                                    UINT32 numFramesAvailable = bufferFrameCount - numFramesPadding;
                                    size_t bytesAvailableInStream = pGlobalStream ? pGlobalStream->GetAvailableBytes() : 0;

                                    // --- Rilevamento preciso di fine riproduzione ---
                                    // Non ci basiamo piu' su "il buffer sembra vuoto da un po'":
                                    // confrontiamo direttamente l'istante target (calcolato quando
                                    // SAPI ha segnalato la fine dell'ultimo messaggio in coda) con la
                                    // posizione REALE dell'orologio hardware. Nessuna euristica,
                                    // nessun margine indovinato.
                                    int pendId = pendingDoneMsgId.load();
                                    if (pendId >= 0 && pAudioClock) {
                                        UINT64 devPos = 0;
                                        if (SUCCEEDED(pAudioClock->GetPosition(&devPos, nullptr))) {
                                            LONGLONG posHns = (LONGLONG)((double)devPos * 10000000.0 / (double)clockFreq);
                                            LONGLONG targetHns = pendingDoneTargetHns.load();
                                            if (targetHns >= 0 && posHns >= targetHns) {
                                                int doneId = pendingDoneMsgId.exchange(-1);
                                                pendingDoneTargetHns = -1;
                                                if (doneId >= 0) {
                                                    std::wstring doneMsg = L"DONE|" + std::to_wstring(doneId);
                                                    SendEvent(doneMsg);
                                                }
                                            }
                                        }
                                    }

                                    UINT32 allineamento = globalWfx.nBlockAlign;
                                    if (numFramesAvailable > 0 && bytesAvailableInStream > 0 && allineamento > 0) {
                                        UINT32 framesToWrite = numFramesAvailable;
                                        size_t bytesToWrite = (size_t)framesToWrite * allineamento;

                                        if (bytesToWrite > bytesAvailableInStream) {
                                            bytesToWrite = bytesAvailableInStream - (bytesAvailableInStream % allineamento);
                                            framesToWrite = (UINT32)(bytesToWrite / allineamento);
                                        }

                                        if (framesToWrite > 0) {
                                            BYTE* pData;
                                            HRESULT hrBuf = pRenderClient->GetBuffer(framesToWrite, &pData);
                                            if (hrBuf == AUDCLNT_E_DEVICE_INVALIDATED) { topologyBreak = true; break; }
                                            if (SUCCEEDED(hrBuf)) {
                                                size_t actuallyRead = pGlobalStream->PullAudio(pData, bytesToWrite);
                                                bool fineNetta = actuallyRead < bytesToWrite;
                                                if (fineNetta) {
                                                    memset(pData + actuallyRead, 0, bytesToWrite - actuallyRead);
                                                }
                                                if (actuallyRead >= 2) {
                                                    AmmorbidisciStacco16(pData, actuallyRead, dopoBuco, fineNetta);
                                                }
                                                dopoBuco = fineNetta;
                                                pRenderClient->ReleaseBuffer(framesToWrite, 0);
                                                totalFramesRenderedToDevice += framesToWrite;
                                            }
                                        }
                                    } else if (numFramesAvailable > 0 && bytesAvailableInStream == 0 && numFramesPadding * 50 < globalWfx.nSamplesPerSec) {
                                        // Buffer hardware quasi vuoto e niente altro da scrivere: il prossimo attacco
                                        // è un vero stacco. La coda vuota mentre c'è ancora voce in hardware non si sfuma,
                                        // altrimenti ogni pezzo di SAPI fa un buco.
                                        dopoBuco = true;
                                    }
                                    Sleep(2);
                                }
                                pAudioClient->Stop();
                                if (pAudioClock) pAudioClock->Release();
                                pRenderClient->Release();
                            }
                        }
                        pAudioClient->Release();
                    }
                    pDevice->Release();

                    // Il dispositivo fissato dall'utente esiste ed e' attivo, ma non si
                    // lascia aprire (driver occupato, modalita' esclusiva di un altro
                    // programma, cuffia sparita fra il controllo e l'apertura). Meglio
                    // parlare dal predefinito che restare muti: si lascia cadere la
                    // scelta fissa e si riprova subito.
                    if (!aperturaRiuscita && deviceFound) {
                        {
                            std::lock_guard<std::mutex> lock(deviceMutex);
                            if (globalTargetAudioDevice == currentDevice) {
                                globalTargetAudioDevice.clear();
                            }
                        }
                        continue;
                    }

                    // Se siamo usciti per un cambio di topologia, ripartiamo subito
                    // con il nuovo device invece di aspettare il backoff normale.
                    if (topologyBreak) continue;
                }
            }

            // Attesa prima di riprovare. Mezzo secondo pieno significava mezzo secondo
            // di silenzio dopo aver collegato la cuffia: si spezza in fette da dieci
            // millisecondi e si esce appena Windows segnala un cambio.
            if (keepRunning) {
                for (int attesa = 0; attesa < 50 && keepRunning; attesa++) {
                    if (deviceTopologyChanged.load()) break;
                    Sleep(10);
                }
            }
        }

        if (pPersistentEnumerator) {
            if (notifyRegistered && pNotifyClient) {
                pPersistentEnumerator->UnregisterEndpointNotificationCallback(pNotifyClient);
            }
            pPersistentEnumerator->Release();
        }
        if (pNotifyClient) pNotifyClient->Release();

        CoUninitialize();
    } catch (const std::exception& e) {
        ReportServerError("WASAPI_THREAD", e.what());
        SendShutdownSignal(L"WASAPI_ERROR");
        keepRunning = false;
        if (hCommandEvent) SetEvent(hCommandEvent);
    } catch (...) {
        ReportServerError("WASAPI_THREAD", "Errore sconosciuto");
        SendShutdownSignal(L"WASAPI_ERROR");
        keepRunning = false;
        if (hCommandEvent) SetEvent(hCommandEvent);
    }
}

void DisconnectClient() {
    try {
        std::lock_guard<std::mutex> lock(transportMutex);
        if (activeTransport == TransportType::TCP && hSocket != INVALID_SOCKET) {
            shutdown(hSocket, SD_BOTH);
            closesocket(hSocket);
            hSocket = INVALID_SOCKET;
        } else if (activeTransport == TransportType::Pipe && globalPipe != INVALID_HANDLE_VALUE) {
            DisconnectNamedPipe(globalPipe);
            globalPipe = INVALID_HANDLE_VALUE;
        }
        
        isClientConnected = false;
        globalCrypto.Reset();
        lastDisconnectTime = std::time(nullptr);
        activeTransport = TransportType::None;
    } catch (const std::exception& e) {
        ReportServerError("DisconnectClient", e.what());
    } catch (...) {
        ReportServerError("DisconnectClient", "Errore sconosciuto");
    }
}

bool ProcessClientCommand(std::string& cmd, bool& authenticated, int& invalidCryptoCount) {
    try {
        if (!authenticated) {
            std::string authPrefix = globalAuthToken + "|";
            if (cmd.find(authPrefix) == 0) {
                authenticated = true;
                isClientConnected = true;

                std::string pubKeyHex = cmd.substr(authPrefix.length());
                std::string pubKeyXml = HexDecode(pubKeyHex);
                
                if (globalCrypto.hAesAlg == NULL) {
                    NTSTATUS statusAlg = BCryptOpenAlgorithmProvider(&globalCrypto.hAesAlg, BCRYPT_AES_ALGORITHM, NULL, 0);
                    if (!NT_SUCCESS(statusAlg) || globalCrypto.hAesAlg == NULL) {
                        ReportServerError("AUTH", "BCryptOpenAlgorithmProvider (AES) fallito: " + std::to_string(statusAlg));
                        SecureClearStr(cmd);
                        return false;
                    }
                    BCryptSetProperty(globalCrypto.hAesAlg, BCRYPT_CHAINING_MODE, (PUCHAR)BCRYPT_CHAIN_MODE_CBC, sizeof(BCRYPT_CHAIN_MODE_CBC), 0);
                }

                BCryptGenRandom(NULL, globalCrypto.sessionKey.data(), 32, BCRYPT_USE_SYSTEM_PREFERRED_RNG);
                BCryptGenRandom(NULL, globalCrypto.globalIV.data(), 16, BCRYPT_USE_SYSTEM_PREFERRED_RNG);

                DWORD cbKeyObject = 0;
                DWORD cbDataProp = 0;
                NTSTATUS statusProp = BCryptGetProperty(globalCrypto.hAesAlg, BCRYPT_OBJECT_LENGTH, (PUCHAR)&cbKeyObject, sizeof(DWORD), &cbDataProp, 0);
                if (!NT_SUCCESS(statusProp) || cbKeyObject == 0) {
                    ReportServerError("AUTH", "BCryptGetProperty (OBJECT_LENGTH) fallito: " + std::to_string(statusProp));
                    SecureClearStr(cmd);
                    return false;
                }
                globalCrypto.pbKeyObject.resize(cbKeyObject);

                if (globalCrypto.hKey) {
                    BCryptDestroyKey(globalCrypto.hKey);
                    globalCrypto.hKey = NULL;
                }
                NTSTATUS statusKey = BCryptGenerateSymmetricKey(globalCrypto.hAesAlg, &globalCrypto.hKey, globalCrypto.pbKeyObject.data(), globalCrypto.pbKeyObject.size(), globalCrypto.sessionKey.data(), globalCrypto.sessionKey.size(), 0);
                if (!NT_SUCCESS(statusKey) || globalCrypto.hKey == NULL) {
                    ReportServerError("AUTH", "BCryptGenerateSymmetricKey fallito: " + std::to_string(statusKey));
                    globalCrypto.isEncrypted = false;
                    SecureClearStr(cmd);
                    return false;
                }
                globalCrypto.isEncrypted = true;

                std::string modStr = ExtractXmlTag(pubKeyXml, "Modulus");
                std::string expStr = ExtractXmlTag(pubKeyXml, "Exponent");

                DWORD mLen = 0;
                if (!CryptStringToBinaryA(modStr.c_str(), 0, CRYPT_STRING_BASE64, NULL, &mLen, NULL, NULL) || mLen == 0) {
                    ReportServerError("AUTH", "Decodifica Base64 Modulus fallita");
                    return false;
                }
                std::vector<BYTE> modBytes(mLen);
                CryptStringToBinaryA(modStr.c_str(), 0, CRYPT_STRING_BASE64, modBytes.data(), &mLen, NULL, NULL);

                DWORD eLen = 0;
                if (!CryptStringToBinaryA(expStr.c_str(), 0, CRYPT_STRING_BASE64, NULL, &eLen, NULL, NULL) || eLen == 0) {
                    ReportServerError("AUTH", "Decodifica Base64 Exponent fallita");
                    return false;
                }
                std::vector<BYTE> expBytes(eLen);
                CryptStringToBinaryA(expStr.c_str(), 0, CRYPT_STRING_BASE64, expBytes.data(), &eLen, NULL, NULL);

                ULONG blobSize = sizeof(BCRYPT_RSAKEY_BLOB) + expBytes.size() + modBytes.size();
                std::vector<BYTE> blob(blobSize, 0);
                BCRYPT_RSAKEY_BLOB* pBlob = (BCRYPT_RSAKEY_BLOB*)blob.data();
                pBlob->Magic = BCRYPT_RSAPUBLIC_MAGIC;
                pBlob->BitLength = modBytes.size() * 8;
                pBlob->cbPublicExp = expBytes.size();
                pBlob->cbModulus = modBytes.size();
                pBlob->cbPrime1 = 0;
                pBlob->cbPrime2 = 0;

                BYTE* dest = blob.data() + sizeof(BCRYPT_RSAKEY_BLOB);
                memcpy(dest, expBytes.data(), expBytes.size());
                dest += expBytes.size();
                memcpy(dest, modBytes.data(), modBytes.size());

                BCRYPT_ALG_HANDLE hRsaAlg = NULL;
                BCryptOpenAlgorithmProvider(&hRsaAlg, BCRYPT_RSA_ALGORITHM, NULL, 0);
                BCRYPT_KEY_HANDLE hRsaKey = NULL;
                NTSTATUS status = BCryptImportKeyPair(hRsaAlg, NULL, BCRYPT_RSAPUBLIC_BLOB, &hRsaKey, blob.data(), blobSize, 0);

                if (!NT_SUCCESS(status) || hRsaKey == NULL) {
                    ReportServerError("CRYPTO", "Errore in BCryptImportKeyPair: " + std::to_string(status));
                    if (hRsaKey) BCryptDestroyKey(hRsaKey);
                    if (hRsaAlg) BCryptCloseAlgorithmProvider(hRsaAlg, 0);
                    SecureClearStr(cmd);
                    return false;
                }

                ULONG cbEncKey = 0;
                NTSTATUS statusEncKey1 = BCryptEncrypt(hRsaKey, globalCrypto.sessionKey.data(), globalCrypto.sessionKey.size(), NULL, NULL, 0, NULL, 0, &cbEncKey, BCRYPT_PAD_PKCS1);
                if (!NT_SUCCESS(statusEncKey1) || cbEncKey == 0) {
                    ReportServerError("CRYPTO", "BCryptEncrypt (dimensione chiave) fallito: " + std::to_string(statusEncKey1));
                    BCryptDestroyKey(hRsaKey);
                    BCryptCloseAlgorithmProvider(hRsaAlg, 0);
                    SecureClearStr(cmd);
                    return false;
                }
                std::vector<BYTE> encKey(cbEncKey, 0);
                NTSTATUS statusEncKey2 = BCryptEncrypt(hRsaKey, globalCrypto.sessionKey.data(), globalCrypto.sessionKey.size(), NULL, NULL, 0, encKey.data(), encKey.size(), &cbEncKey, BCRYPT_PAD_PKCS1);
                if (!NT_SUCCESS(statusEncKey2)) {
                    ReportServerError("CRYPTO", "BCryptEncrypt (cifratura chiave) fallito: " + std::to_string(statusEncKey2));
                    BCryptDestroyKey(hRsaKey);
                    BCryptCloseAlgorithmProvider(hRsaAlg, 0);
                    SecureClearStr(cmd);
                    return false;
                }

                std::reverse(encKey.begin(), encKey.end());

                ULONG cbEncIv = 0;
                NTSTATUS statusEncIv1 = BCryptEncrypt(hRsaKey, globalCrypto.globalIV.data(), globalCrypto.globalIV.size(), NULL, NULL, 0, NULL, 0, &cbEncIv, BCRYPT_PAD_PKCS1);
                if (!NT_SUCCESS(statusEncIv1) || cbEncIv == 0) {
                    ReportServerError("CRYPTO", "BCryptEncrypt (dimensione IV) fallito: " + std::to_string(statusEncIv1));
                    BCryptDestroyKey(hRsaKey);
                    BCryptCloseAlgorithmProvider(hRsaAlg, 0);
                    SecureClearStr(cmd);
                    return false;
                }
                std::vector<BYTE> encIv(cbEncIv, 0);
                NTSTATUS statusEncIv2 = BCryptEncrypt(hRsaKey, globalCrypto.globalIV.data(), globalCrypto.globalIV.size(), NULL, NULL, 0, encIv.data(), encIv.size(), &cbEncIv, BCRYPT_PAD_PKCS1);
                if (!NT_SUCCESS(statusEncIv2)) {
                    ReportServerError("CRYPTO", "BCryptEncrypt (cifratura IV) fallito: " + std::to_string(statusEncIv2));
                    BCryptDestroyKey(hRsaKey);
                    BCryptCloseAlgorithmProvider(hRsaAlg, 0);
                    SecureClearStr(cmd);
                    return false;
                }

                std::reverse(encIv.begin(), encIv.end());

                std::string keyMsg = "KEY|" + HexEncode(std::string((char*)encKey.data(), cbEncKey)) + "|" + HexEncode(std::string((char*)encIv.data(), cbEncIv)) + "\n";
                SendRawData(keyMsg);

                BCryptDestroyKey(hRsaKey);
                BCryptCloseAlgorithmProvider(hRsaAlg, 0);
                
                SecureClearStr(cmd);
                return true;
            } else {
                ReportServerError("AUTH", "Autenticazione fallita. Atteso prefisso valido.");
                SecureClearStr(cmd);
                return false;
            }
        } else {
            std::string rawCipher = HexDecode(cmd);
            if (rawCipher.empty() || rawCipher.length() % 16 != 0) {
                invalidCryptoCount++;
                return invalidCryptoCount <= MAX_INVALID_PACKETS;
            }
            ULONG cbData = 0;
            std::vector<BYTE> ivCopy = globalCrypto.globalIV;
            NTSTATUS status = BCryptDecrypt(globalCrypto.hKey, (PUCHAR)rawCipher.data(), rawCipher.length(), NULL, ivCopy.data(), ivCopy.size(), NULL, 0, &cbData, BCRYPT_BLOCK_PADDING);
            if (NT_SUCCESS(status)) {
                std::vector<BYTE> plain(cbData, 0);
                ivCopy = globalCrypto.globalIV;
                status = BCryptDecrypt(globalCrypto.hKey, (PUCHAR)rawCipher.data(), rawCipher.length(), NULL, ivCopy.data(), ivCopy.size(), plain.data(), plain.size(), &cbData, BCRYPT_BLOCK_PADDING);
                if (NT_SUCCESS(status)) {
                    cmd = std::string((char*)plain.data(), cbData);
                    invalidCryptoCount = 0;
                } else {
                    invalidCryptoCount++;
                    return invalidCryptoCount <= MAX_INVALID_PACKETS;
                }
            } else {
                invalidCryptoCount++;
                return invalidCryptoCount <= MAX_INVALID_PACKETS;
            }
        }

        if (cmd == "PING") {
            lastPingTime = std::time(nullptr);
            PROCESS_MEMORY_COUNTERS pmc;
            if (GetProcessMemoryInfo(GetCurrentProcess(), &pmc, sizeof(pmc))) {
                std::wstring pongMsg = L"PONG|" + std::to_wstring(pmc.WorkingSetSize);
                SendEvent(pongMsg);
            } else {
                SendEvent(L"PONG");
            }
            SecureClearStr(cmd);
            return true;
        }

        if (cmd == "EXIT") {
            keepRunning = false;
            if (hCommandEvent) SetEvent(hCommandEvent);
            SecureClearStr(cmd);
            return false;
        }

        std::wstring wcmd = Utf8ToWide(cmd);

        if (wcmd.rfind(L"SPEAK|", 0) == 0) {
            globalQueues.PushText(wcmd);
        } else {
            if (wcmd == L"STOP") {
                if (pGlobalStream) pGlobalStream->AbortAndClear();
                globalQueues.ClearText();
                {
                    std::lock_guard<std::mutex> lock(speechQueueMutex);
                    speechQueue.clear();
                }
                currentMsgId = -1;
                lastEnqueuedMsgId = -1;
                currentSeq = -1;
                currentIsLast = 0;
                sapiIsSpeaking = false;
                pendingDoneMsgId = -1;
                pendingDoneTargetHns = -1;
                tickBufferVuotoDaQuando = 0;
            } else if (wcmd.rfind(L"SET_AUDIO_DEVICE|", 0) == 0) {
                {
                    std::lock_guard<std::mutex> lockDevice(deviceMutex);
                    globalTargetAudioDevice = wcmd.substr(17);
                }
                needsWasapiReset = true;
                // Senza questo il thread WASAPI restava sul dispositivo aperto: il reset
                // svuota il buffer ma non riapre l'uscita. Va forzata la riselezione.
                deviceTopologyChanged = true;
                if (hCommandEvent) SetEvent(hCommandEvent);
            }
            globalQueues.PushCtrl(wcmd);
        }

        SecureClearStr(cmd);
        return true;

    } catch (const std::exception& e) {
        ReportServerError("ProcessClientCommand", e.what());
        SecureClearStr(cmd);
        return false;
    } catch (...) {
        ReportServerError("ProcessClientCommand", "Errore sconosciuto");
        SecureClearStr(cmd);
        return false;
    }
}

void ProcessClientBuffer(std::string& cmdBuffer, bool& authenticated, int& invalidCryptoCount, bool& keepConnection) {
    try {
        size_t startPos = 0;
        size_t pos = 0;

        while ((pos = cmdBuffer.find('\n', startPos)) != std::string::npos) {
            std::string cmd = cmdBuffer.substr(startPos, pos - startPos);
            startPos = pos + 1;
            
            if (!cmd.empty() && cmd.back() == '\r') cmd.pop_back();

            if (cmd.length() >= 3 && (unsigned char)cmd[0] == 0xEF && (unsigned char)cmd[1] == 0xBB && (unsigned char)cmd[2] == 0xBF) {
                cmd.erase(0, 3);
            }

            if (!ProcessClientCommand(cmd, authenticated, invalidCryptoCount)) {
                keepConnection = false;
                break;
            }
        }

        if (startPos > 0) {
            cmdBuffer.erase(0, startPos);
        }
    } catch (const std::exception& e) {
        ReportServerError("ProcessClientBuffer", e.what());
        keepConnection = false;
    } catch (...) {
        ReportServerError("ProcessClientBuffer", "Errore sconosciuto");
        keepConnection = false;
    }
}

void HandleClientConnectionTCP(SOCKET ClientSocket) {
    HANDLE hIOCP = NULL;
    try {
        hIOCP = CreateIoCompletionPort(INVALID_HANDLE_VALUE, NULL, 0, 0);
        CreateIoCompletionPort((HANDLE)ClientSocket, hIOCP, (ULONG_PTR)ClientSocket, 0);

        bool authenticated = false;
        int invalidCryptoCount = 0;
        char buffer[TCP_BUFFER_SIZE];
        std::string cmdBuffer = "";

        WSAOVERLAPPED overlapped;
        SecureZeroMemory(&overlapped, sizeof(WSAOVERLAPPED));
        WSABUF dataBuf;
        dataBuf.buf = buffer;
        dataBuf.len = sizeof(buffer);
        DWORD flagsIo = 0;

        WSARecv(ClientSocket, &dataBuf, 1, NULL, &flagsIo, &overlapped, NULL);

        while (keepRunning) {
            DWORD bytesTransferred = 0;
            ULONG_PTR compKey = 0;
            LPOVERLAPPED pOv = NULL;

            DWORD timeout = authenticated ? INFINITE : HANDSHAKE_TIMEOUT_MS;
            BOOL bRet = GetQueuedCompletionStatus(hIOCP, &bytesTransferred, &compKey, &pOv, timeout);

            if (!bRet && pOv == NULL) {
                if (WSAGetLastError() == WAIT_TIMEOUT) {
                    if (!authenticated) {
                        ReportServerError("TCP_CLIENT", "Handshake timeout scaduto.");
                        break;
                    }
                    continue;
                }
                break;
            }

            if (bRet && pOv) {
                if (bytesTransferred == 0) break;

                cmdBuffer.append(buffer, bytesTransferred);
                if (cmdBuffer.length() > 10485760) {
                    ReportServerError("TCP_CLIENT", "Buffer TCP overflow superato.");
                    break;
                }

                bool keepConnection = true;
                ProcessClientBuffer(cmdBuffer, authenticated, invalidCryptoCount, keepConnection);
                if (!keepConnection) break;

                SecureZeroMemory(&overlapped, sizeof(WSAOVERLAPPED));
                flagsIo = 0;
                WSARecv(ClientSocket, &dataBuf, 1, NULL, &flagsIo, &overlapped, NULL);
            } else {
                break;
            }
        }
        
        SecureClearStr(cmdBuffer);
    } catch (const std::exception& e) {
        ReportServerError("HandleClientConnectionTCP", e.what());
    } catch (...) {
        ReportServerError("HandleClientConnectionTCP", "Errore sconosciuto");
    }
    
    DisconnectClient();
    if (hIOCP) CloseHandle(hIOCP);
}

void TcpListenerThreadFunc() {
    try {
        SOCKET ListenSocket = INVALID_SOCKET;

        while (keepRunning) {
            if (ListenSocket == INVALID_SOCKET) {
                ListenSocket = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
                if (ListenSocket != INVALID_SOCKET) {
                    sockaddr_in serverAddr;
                    serverAddr.sin_family = AF_INET;
                    serverAddr.sin_addr.s_addr = inet_addr(BIND_IP);
                    serverAddr.sin_port = htons(globalPort);
                    bind(ListenSocket, (SOCKADDR*)&serverAddr, sizeof(serverAddr));
                    listen(ListenSocket, SOMAXCONN);
                }
            }

            if (activeTransport != TransportType::None && activeTransport != TransportType::TCP) {
                Sleep(100);
                continue;
            }

            if (ListenSocket != INVALID_SOCKET) {
                fd_set readfds;
                FD_ZERO(&readfds);
                FD_SET(ListenSocket, &readfds);
                timeval tv = {1, 0};

                int res = select(0, &readfds, NULL, NULL, &tv);
                if (res > 0) {
                    SOCKET ClientSocket = accept(ListenSocket, NULL, NULL);
                    if (ClientSocket != INVALID_SOCKET) {
                        TransportType expected = TransportType::None;
                        if (activeTransport.compare_exchange_strong(expected, TransportType::TCP)) {
                            int flag = 1;
                            setsockopt(ClientSocket, IPPROTO_TCP, TCP_NODELAY, (char*)&flag, sizeof(int));
                            int rcvbuf = TCP_BUFFER_SIZE; 
                            setsockopt(ClientSocket, SOL_SOCKET, SO_RCVBUF, (char*)&rcvbuf, sizeof(int));

                            int keepalive = 1;
                            setsockopt(ClientSocket, SOL_SOCKET, SO_KEEPALIVE, (char*)&keepalive, sizeof(keepalive));
                            tcp_keepalive alive;
                            alive.onoff = 1;
                            alive.keepalivetime = KEEPALIVE_TIME;
                            alive.keepaliveinterval = KEEPALIVE_INTERVAL;
                            DWORD dwBytesRet = 0;
                            WSAIoctl(ClientSocket, SIO_KEEPALIVE_VALS, &alive, sizeof(alive), NULL, 0, &dwBytesRet, NULL, NULL);

                            {
                                std::lock_guard<std::mutex> lock(transportMutex);
                                hSocket = ClientSocket;
                            }
                            HandleClientConnectionTCP(ClientSocket);
                        } else {
                            closesocket(ClientSocket);
                        }
                    }
                }
            }
        }
        if (ListenSocket != INVALID_SOCKET) closesocket(ListenSocket);
    } catch (const std::exception& e) {
        ReportServerError("TcpListenerThreadFunc", e.what());
    } catch (...) {
        ReportServerError("TcpListenerThreadFunc", "Errore sconosciuto");
    }
}

void HandleClientConnectionPipe(HANDLE hPipe) {
    HANDLE hEvent = NULL;
    try {
        bool authenticated = false;
        int invalidCryptoCount = 0;
        char buffer[TCP_BUFFER_SIZE];
        std::string cmdBuffer = "";

        hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
        OVERLAPPED overlapped = {};
        overlapped.hEvent = hEvent;

        BOOL bRead = ReadFile(hPipe, buffer, sizeof(buffer), NULL, &overlapped);
        if (!bRead && GetLastError() != ERROR_IO_PENDING) {
            CloseHandle(hEvent);
            DisconnectClient();
            return;
        }

        while (keepRunning) {
            DWORD timeout = authenticated ? INFINITE : HANDSHAKE_TIMEOUT_MS;
            DWORD waitRes = WaitForSingleObject(hEvent, timeout);

            if (waitRes == WAIT_OBJECT_0) {
                DWORD bytesTransferred = 0;
                if (!GetOverlappedResult(hPipe, &overlapped, &bytesTransferred, FALSE) || bytesTransferred == 0) {
                    break;
                }

                cmdBuffer.append(buffer, bytesTransferred);
                if (cmdBuffer.length() > 10485760) {
                    ReportServerError("PIPE_CLIENT", "Buffer Pipe overflow superato.");
                    break;
                }

                bool keepConnection = true;
                ProcessClientBuffer(cmdBuffer, authenticated, invalidCryptoCount, keepConnection);
                if (!keepConnection) break;

                ResetEvent(hEvent);
                SecureZeroMemory(&overlapped, sizeof(OVERLAPPED));
                overlapped.hEvent = hEvent;
                bRead = ReadFile(hPipe, buffer, sizeof(buffer), NULL, &overlapped);
                if (!bRead && GetLastError() != ERROR_IO_PENDING) {
                    break;
                }
            } else if (waitRes == WAIT_TIMEOUT) {
                if (!authenticated) {
                    ReportServerError("PIPE_CLIENT", "Handshake timeout scaduto.");
                    break;
                }
            } else {
                break;
            }
        }
        
        SecureClearStr(cmdBuffer);
    } catch (const std::exception& e) {
        ReportServerError("HandleClientConnectionPipe", e.what());
    } catch (...) {
        ReportServerError("HandleClientConnectionPipe", "Errore sconosciuto");
    }
    
    if (hEvent) CloseHandle(hEvent);
    DisconnectClient();
}

void PipeListenerThreadFunc() {
    try {
        std::string tokenStr = globalAuthToken.substr(5);
        std::string pipeName = "\\\\.\\pipe\\WGSapiPipe_" + tokenStr;
        while (keepRunning) {
            HANDLE hPipe = CreateNamedPipeA(pipeName.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1, TCP_BUFFER_SIZE, TCP_BUFFER_SIZE, 0, NULL);
            if (hPipe == INVALID_HANDLE_VALUE) { Sleep(1000); continue; }
            
            OVERLAPPED ov = {};
            ov.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
            
            BOOL connected = ConnectNamedPipe(hPipe, &ov);
            if (connected || GetLastError() == ERROR_PIPE_CONNECTED) {
                SetEvent(ov.hEvent);
            }
            
            while (keepRunning) {
                DWORD waitRes = WaitForSingleObject(ov.hEvent, 1000);
                if (waitRes == WAIT_OBJECT_0) {
                    TransportType expected = TransportType::None;
                    if (activeTransport.compare_exchange_strong(expected, TransportType::Pipe)) {
                        {
                            std::lock_guard<std::mutex> transportLock(transportMutex);
                            globalPipe = hPipe;
                        }
                        HandleClientConnectionPipe(hPipe);
                        break;
                    } else {
                        DisconnectNamedPipe(hPipe);
                        break;
                    }
                }
            }
            
            if (globalPipe == INVALID_HANDLE_VALUE) CloseHandle(hPipe);
            CloseHandle(ov.hEvent);
        }
    } catch (const std::exception& e) {
        ReportServerError("PipeListenerThreadFunc", e.what());
    } catch (...) {
        ReportServerError("PipeListenerThreadFunc", "Errore sconosciuto");
    }
}

void HandleClientConnectionSHM() {
    try {
        bool authenticated = false;
        int invalidCryptoCount = 0;
        std::string cmdBuffer = "";

        auto processShmPayload = [&]() -> bool {
            // La metà C2S della mappa è di 65536 byte: un len fino a 131068 legge anche la metà S2C
            // e interpreta come comandi i byte che il server ha scritto per il client.
            if (pShmC2S == nullptr) return false;
            DWORD len = 0;
            memcpy(&len, pShmC2S, sizeof(DWORD));
            if (len > 0 && len <= 65532) {
                cmdBuffer.append((char*)pShmC2S + sizeof(DWORD), len);
                bool keepConnection = true;
                ProcessClientBuffer(cmdBuffer, authenticated, invalidCryptoCount, keepConnection);
                return keepConnection;
            }
            return false;
        };

        if (!processShmPayload()) {
            DisconnectClient();
            return;
        }
        SetEvent(hShmEventC2SAck);

        while (keepRunning) {
            DWORD timeout = authenticated ? INFINITE : HANDSHAKE_TIMEOUT_MS;
            DWORD waitRes = WaitForSingleObject(hShmEventC2S, timeout);

            if (waitRes == WAIT_OBJECT_0) {
                if (!processShmPayload()) break;
                SetEvent(hShmEventC2SAck);
            } else if (waitRes == WAIT_TIMEOUT) {
                if (!authenticated) {
                    ReportServerError("SHM_CLIENT", "Handshake timeout scaduto.");
                    break;
                }
            } else {
                break;
            }
        }
        SecureClearStr(cmdBuffer);
    } catch (const std::exception& e) {
        ReportServerError("HandleClientConnectionSHM", e.what());
    } catch (...) {
        ReportServerError("HandleClientConnectionSHM", "Errore sconosciuto");
    }
    
    DisconnectClient();
}

void ShmListenerThreadFunc() {
    try {
        std::string tokenStr = globalAuthToken.substr(5);
        std::string shmName = "Local\\WGSapi_SHM_" + tokenStr;
        HANDLE hMap = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, 131072, shmName.c_str());
        if (!hMap) {
            ReportServerError("SHM_LISTENER", "Impossibile creare mapping memoria condivisa");
            return;
        }
        
        LPVOID pMap = MapViewOfFile(hMap, FILE_MAP_ALL_ACCESS, 0, 0, 131072);
        if (!pMap) { 
            CloseHandle(hMap); 
            ReportServerError("SHM_LISTENER", "Impossibile mappare la memoria");
            return; 
        }

        std::string ev1 = "Local\\WGSapi_SHM_C2S_" + tokenStr;
        std::string ev2 = "Local\\WGSapi_SHM_C2S_ACK_" + tokenStr;
        std::string ev3 = "Local\\WGSapi_SHM_S2C_" + tokenStr;
        std::string ev4 = "Local\\WGSapi_SHM_S2C_ACK_" + tokenStr;

        HANDLE hEvC2S = CreateEventA(NULL, FALSE, FALSE, ev1.c_str());
        HANDLE hEvC2SAck = CreateEventA(NULL, FALSE, FALSE, ev2.c_str());
        HANDLE hEvS2C = CreateEventA(NULL, FALSE, FALSE, ev3.c_str());
        HANDLE hEvS2CAck = CreateEventA(NULL, FALSE, FALSE, ev4.c_str());

        if (!hEvC2S || !hEvC2SAck || !hEvS2C || !hEvS2CAck) {
            ReportServerError("SHM_LISTENER", "Impossibile creare gli eventi della memoria condivisa");
            if (pMap) UnmapViewOfFile(pMap);
            if (hMap) CloseHandle(hMap);
            if (hEvC2S) CloseHandle(hEvC2S);
            if (hEvC2SAck) CloseHandle(hEvC2SAck);
            if (hEvS2C) CloseHandle(hEvS2C);
            if (hEvS2CAck) CloseHandle(hEvS2CAck);
            return;
        }

        while (keepRunning) {
            DWORD waitRes = WaitForSingleObject(hEvC2S, 1000);
            if (waitRes == WAIT_OBJECT_0) {
                TransportType expected = TransportType::None;
                if (activeTransport.compare_exchange_strong(expected, TransportType::SHM)) {
                    {
                        std::lock_guard<std::mutex> transportLock(transportMutex);
                        hShmMap = hMap;
                        pShmC2S = pMap;
                        pShmS2C = (BYTE*)pMap + 65536;
                        hShmEventC2S = hEvC2S;
                        hShmEventC2SAck = hEvC2SAck;
                        hShmEventS2C = hEvS2C;
                        hShmEventS2CAck = hEvS2CAck;
                    }
                    HandleClientConnectionSHM();
                }
            }
        }
        
        if (pMap) UnmapViewOfFile(pMap);
        if (hMap) CloseHandle(hMap);
        if (hEvC2S) CloseHandle(hEvC2S);
        if (hEvC2SAck) CloseHandle(hEvC2SAck);
        if (hEvS2C) CloseHandle(hEvS2C);
        if (hEvS2CAck) CloseHandle(hEvS2CAck);
        
        {
            std::lock_guard<std::mutex> transportLock(transportMutex);
            pShmC2S = nullptr;
            pShmS2C = nullptr;
        }
    } catch (const std::exception& e) {
        ReportServerError("ShmListenerThreadFunc", e.what());
    } catch (...) {
        ReportServerError("ShmListenerThreadFunc", "Errore sconosciuto");
    }
}

void AliveHeartbeatThreadFunc() {
    try {
        while (keepRunning) {
            std::this_thread::sleep_for(std::chrono::seconds(2));
            if (!keepRunning) break;
            if (!isClientConnected) continue;

            // "Vivo" deve riflettere se c'e' ancora AUDIO da riprodurre, non se SAPI sta
            // ancora sintetizzando testo: la sintesi scrive nel buffer molto piu' veloce
            // del tempo reale di riproduzione, quindi sapiIsSpeaking puo' diventare falso
            // molti secondi prima che l'ultimo chunk finisca davvero di suonare. In quella
            // finestra il client non deve vedere silenzio: c'e' ancora lavoro in corso
            // (buffer non vuoto, oppure DONE finale non ancora raggiunto sull'orologio
            // hardware) e va segnalato come tale.
            bool audioResiduo = pGlobalStream && pGlobalStream->GetAvailableBytes() > 0;
            bool doneInAttesa = pendingDoneMsgId.load() >= 0;
            if (!sapiIsSpeaking && !audioResiduo && !doneInAttesa) continue;

            int mId = currentMsgId.load();
            int sq = currentSeq.load();
            if (mId < 0) continue;

            std::wstring aliveMsg = L"ALIVE|" + std::to_wstring(mId) + L"|" + std::to_wstring(sq);
            SendEvent(aliveMsg);
        }
    } catch (const std::exception& e) {
        ReportServerError("AliveHeartbeatThreadFunc", e.what());
    } catch (...) {
        ReportServerError("AliveHeartbeatThreadFunc", "Errore sconosciuto");
    }
}

constexpr int MP3_THREAD_MASSIMI = 32;
constexpr int MP3_BITRATE_BYTE_SEC = 8000;
constexpr long long MP3_BYTE_BUFFER_MASSIMI = 134217728;

struct Mp3Pezzo {
    std::wstring testo;
    std::vector<BYTE> pcm;
    std::atomic<bool> pronto{false};
};

struct Mp3Lavoro {
    std::wstring id;
    std::wstring voce;
    int velocita = 50;
    int volume = 100;
    std::wstring percorso;
    std::vector<std::unique_ptr<Mp3Pezzo>> pezzi;
    WAVEFORMATEX formato = {};
    int numeroThread = 4;
    std::atomic<bool> annullato{false};
    std::atomic<bool> finito{false};
    std::atomic<bool> battitoAttivo{false};
    std::atomic<int> prossimoDaAssegnare{0};
    std::atomic<long long> byteInAttesa{0};
    std::atomic<int> prossimoDaConsumare{0};
    std::atomic<int> ultimaPercentuale{-1};
    std::thread coordinatore;
};

std::atomic<int> mp3LavoriAttivi{0};
std::mutex mp3Mutex;
std::map<std::wstring, std::shared_ptr<Mp3Lavoro>> mp3Lavori;

static std::vector<std::wstring> Mp3DividiComando(const std::wstring& testo, int numeroCampi) {
    std::vector<std::wstring> campi;
    size_t inizio = 0;
    while ((int)campi.size() < numeroCampi - 1) {
        size_t pos = testo.find(L'|', inizio);
        if (pos == std::wstring::npos) break;
        campi.push_back(testo.substr(inizio, pos - inizio));
        inizio = pos + 1;
    }
    campi.push_back(testo.substr(inizio));
    return campi;
}

static int Mp3ConvertiIntero(const std::wstring& testo, int valoreDefault) {
    try {
        if (testo.empty()) return valoreDefault;
        return std::stoi(testo);
    } catch (...) {
        return valoreDefault;
    }
}

static void Mp3InviaProgresso(const std::shared_ptr<Mp3Lavoro>& lavoro, int percentuale, bool forzato = false) {
    try {
        if (!lavoro) return;
        if (percentuale < 0) percentuale = 0;
        if (percentuale > 100) percentuale = 100;
        if (!forzato && lavoro->ultimaPercentuale.load() == percentuale) return;
        lavoro->ultimaPercentuale.store(percentuale);
        SendEvent(L"MP3_PROGRESS|" + lavoro->id + L"|" + std::to_wstring(percentuale));
    } catch (...) {
        LogToFile("Mp3InviaProgresso: errore sconosciuto");
    }
}

static void Mp3InviaFine(const std::wstring& id, bool esito, const std::wstring& messaggio) {
    try {
        SendEvent(L"MP3_DONE|" + id + L"|" + (esito ? L"1" : L"0") + L"|" + messaggio);
    } catch (...) {
        LogToFile("Mp3InviaFine: errore sconosciuto");
    }
}

static bool Mp3TrovaTokenVoce(const std::wstring& richiesta, Microsoft::WRL::ComPtr<ISpObjectToken>& tokenTrovato) {
    try {
        std::wstring cercata = richiesta;
        while (!cercata.empty() && iswspace(cercata.back())) cercata.pop_back();
        while (!cercata.empty() && iswspace(cercata.front())) cercata.erase(0, 1);

        int indiceCercato = -1;
        try { indiceCercato = std::stoi(cercata); } catch (...) {}

        std::wstring cercataMinuscola = cercata;
        if (!cercataMinuscola.empty()) CharLowerBuffW(&cercataMinuscola[0], (DWORD)cercataMinuscola.length());

        int indiceCorrente = 0;

        Microsoft::WRL::ComPtr<IEnumSpObjectTokens> pEnum;
        if (SUCCEEDED(SpEnumTokens(SPCAT_VOICES, NULL, NULL, &pEnum))) {
            ULONG conteggio = 0;
            pEnum->GetCount(&conteggio);
            for (ULONG i = 0; i < conteggio; i++) {
                Microsoft::WRL::ComPtr<ISpObjectToken> pToken;
                if (SUCCEEDED(pEnum->Item(i, &pToken))) {
                    WCHAR* pszDesc = nullptr;
                    if (SUCCEEDED(SpGetDescription(pToken.Get(), &pszDesc))) {
                        std::wstring descMinuscola = pszDesc;
                        if (!descMinuscola.empty()) CharLowerBuffW(&descMinuscola[0], (DWORD)descMinuscola.length());
                        bool corrisponde = (descMinuscola == cercataMinuscola)
                            || (cercataMinuscola.length() > 3 && descMinuscola.find(cercataMinuscola) != std::wstring::npos)
                            || (indiceCorrente == indiceCercato);
                        CoTaskMemFree(pszDesc);
                        if (corrisponde) {
                            tokenTrovato = pToken;
                            return true;
                        }
                    }
                }
                indiceCorrente++;
            }
        }

        Microsoft::WRL::ComPtr<ISpObjectTokenCategory> pCat;
        if (SUCCEEDED(CoCreateInstance(CLSID_SpObjectTokenCategory, NULL, CLSCTX_ALL, IID_ISpObjectTokenCategory, &pCat))) {
            if (SUCCEEDED(pCat->SetId(SAPI_ONECORE_REG_KEY, FALSE))) {
                Microsoft::WRL::ComPtr<IEnumSpObjectTokens> pEnumOneCore;
                if (SUCCEEDED(pCat->EnumTokens(NULL, NULL, &pEnumOneCore))) {
                    ULONG conteggio = 0;
                    pEnumOneCore->GetCount(&conteggio);
                    for (ULONG i = 0; i < conteggio; i++) {
                        Microsoft::WRL::ComPtr<ISpObjectToken> pToken;
                        if (SUCCEEDED(pEnumOneCore->Item(i, &pToken))) {
                            WCHAR* pszDesc = nullptr;
                            if (SUCCEEDED(SpGetDescription(pToken.Get(), &pszDesc))) {
                                std::wstring descMinuscola = pszDesc;
                                descMinuscola += L" (onecore)";
                                CharLowerBuffW(&descMinuscola[0], (DWORD)descMinuscola.length());
                                bool corrisponde = (descMinuscola == cercataMinuscola)
                                    || (cercataMinuscola.length() > 3 && descMinuscola.find(cercataMinuscola) != std::wstring::npos)
                                    || (indiceCorrente == indiceCercato);
                                CoTaskMemFree(pszDesc);
                                if (corrisponde) {
                                    tokenTrovato = pToken;
                                    return true;
                                }
                            }
                        }
                        indiceCorrente++;
                    }
                }
            }
        }

        return false;
    } catch (...) {
        LogToFile("Mp3TrovaTokenVoce: errore sconosciuto");
        return false;
    }
}

Microsoft::WRL::ComPtr<ISpVoice> mp3AnteprimaVoce;

static void Mp3FermaAnteprima() {
    try {
        if (mp3AnteprimaVoce) {
            mp3AnteprimaVoce->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
            mp3AnteprimaVoce.Reset();
        }
    } catch (...) {
        LogToFile("Mp3FermaAnteprima: errore sconosciuto");
    }
}

static void Mp3AvviaAnteprima(const std::wstring& voce, int velocita, int volume, const std::wstring& testo) {
    try {
        Mp3FermaAnteprima();
        if (testo.empty()) return;

        if (FAILED(CoCreateInstance(CLSID_SpVoice, NULL, CLSCTX_ALL, IID_ISpVoice, &mp3AnteprimaVoce)) || !mp3AnteprimaVoce) {
            LogToFile("Mp3AvviaAnteprima: creazione SpVoice fallita");
            return;
        }

        Microsoft::WRL::ComPtr<ISpObjectToken> token;
        if (Mp3TrovaTokenVoce(voce, token) && token) {
            HRESULT hrVoce = mp3AnteprimaVoce->SetVoice(token.Get());
            if (FAILED(hrVoce)) LogToFile("Mp3AvviaAnteprima: SetVoice fallito hr=" + std::to_string(hrVoce));
        }

        int sapiRate = (velocita / 5) - 10;
        if (sapiRate < -10) sapiRate = -10;
        if (sapiRate > 10) sapiRate = 10;
        mp3AnteprimaVoce->SetRate(sapiRate);

        int volumeSicuro = volume;
        if (volumeSicuro < 0) volumeSicuro = 0;
        if (volumeSicuro > 100) volumeSicuro = 100;
        mp3AnteprimaVoce->SetVolume((USHORT)volumeSicuro);

        HRESULT hrSpeak = mp3AnteprimaVoce->Speak(testo.c_str(), SPF_ASYNC | SPF_IS_NOT_XML, NULL);
        if (FAILED(hrSpeak)) LogToFile("Mp3AvviaAnteprima: Speak fallito hr=" + std::to_string(hrSpeak));
    } catch (const std::exception& e) {
        ReportServerError("Mp3AvviaAnteprima", e.what());
    } catch (...) {
        ReportServerError("Mp3AvviaAnteprima", "Errore sconosciuto nell'anteprima mp3");
    }
}

static void Mp3ThreadBattito(std::shared_ptr<Mp3Lavoro> lavoro) {
    try {
        ULONGLONG tickUltimoBattito = GetTickCount64();
        while (lavoro->battitoAttivo.load()) {
            if (GetTickCount64() - tickUltimoBattito >= 5000) {
                tickUltimoBattito = GetTickCount64();
                Mp3InviaProgresso(lavoro, lavoro->ultimaPercentuale.load(), true);
            }
            Sleep(100);
        }
    } catch (const std::exception& e) {
        ReportServerError("Mp3ThreadBattito", e.what());
    } catch (...) {
        ReportServerError("Mp3ThreadBattito", "Errore sconosciuto nel battito mp3");
    }
}

static void Mp3ThreadSintesi(std::shared_ptr<Mp3Lavoro> lavoro, int indiceWorker) {
    HRESULT hrInit = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_ABOVE_NORMAL);
    try {
        Microsoft::WRL::ComPtr<ISpVoice> voce;
        if (FAILED(CoCreateInstance(CLSID_SpVoice, NULL, CLSCTX_ALL, IID_ISpVoice, &voce)) || !voce) {
            LogToFile("Mp3ThreadSintesi: creazione SpVoice fallita");
            lavoro->annullato.store(true);
        } else {
            Microsoft::WRL::ComPtr<ISpObjectToken> token;
            if (Mp3TrovaTokenVoce(lavoro->voce, token) && token) {
                HRESULT hrVoce = voce->SetVoice(token.Get());
                if (FAILED(hrVoce)) LogToFile("Mp3ThreadSintesi: SetVoice fallito hr=" + std::to_string(hrVoce));
            }

            int sapiRate = (lavoro->velocita / 5) - 10;
            if (sapiRate < -10) sapiRate = -10;
            if (sapiRate > 10) sapiRate = 10;
            voce->SetRate(sapiRate);

            int volumeSicuro = lavoro->volume;
            if (volumeSicuro < 0) volumeSicuro = 0;
            if (volumeSicuro > 100) volumeSicuro = 100;
            voce->SetVolume((USHORT)volumeSicuro);

            while (!lavoro->annullato.load()) {
                int indice = lavoro->prossimoDaAssegnare.fetch_add(1);
                if (indice < 0 || indice >= (int)lavoro->pezzi.size()) break;

                while (!lavoro->annullato.load() && lavoro->byteInAttesa.load() > MP3_BYTE_BUFFER_MASSIMI) {
                    Sleep(5);
                }
                if (lavoro->annullato.load()) {
                    lavoro->pezzi[indice]->pronto.store(true);
                    break;
                }

                Mp3Pezzo* pezzo = lavoro->pezzi[indice].get();
                try {
                    if (pezzo && !pezzo->testo.empty()) {
                        Microsoft::WRL::ComPtr<IStream> flussoMemoria;
                        if (SUCCEEDED(CreateStreamOnHGlobal(NULL, TRUE, &flussoMemoria)) && flussoMemoria) {
                            Microsoft::WRL::ComPtr<ISpStream> flussoSapi;
                            if (SUCCEEDED(CoCreateInstance(CLSID_SpStream, NULL, CLSCTX_ALL, IID_ISpStream, &flussoSapi)) && flussoSapi) {
                                WAVEFORMATEX formatoLocale = lavoro->formato;
                                if (SUCCEEDED(flussoSapi->SetBaseStream(flussoMemoria.Get(), SPDFID_WaveFormatEx, &formatoLocale))) {
                                    voce->SetOutput(flussoSapi.Get(), TRUE);
                                    HRESULT hrSpeak = voce->Speak(pezzo->testo.c_str(), SPF_IS_NOT_XML, NULL);
                                    if (FAILED(hrSpeak)) {
                                        LogToFile("Mp3ThreadSintesi: Speak fallito hr=" + std::to_string(hrSpeak));
                                    } else {
                                        voce->WaitUntilDone(INFINITE);
                                        STATSTG stato = {};
                                        if (SUCCEEDED(flussoMemoria->Stat(&stato, STATFLAG_NONAME))) {
                                            size_t dimensione = (size_t)stato.cbSize.QuadPart;
                                            if (dimensione > 0) {
                                                HGLOBAL hMemoria = NULL;
                                                if (SUCCEEDED(GetHGlobalFromStream(flussoMemoria.Get(), &hMemoria)) && hMemoria) {
                                                    void* puntatore = GlobalLock(hMemoria);
                                                    if (puntatore) {
                                                        pezzo->pcm.assign((BYTE*)puntatore, (BYTE*)puntatore + dimensione);
                                                        GlobalUnlock(hMemoria);
                                                        lavoro->byteInAttesa.fetch_add((long long)dimensione);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    voce->SetOutput(NULL, FALSE);
                                }
                            }
                        }
                    }
                } catch (const std::exception& e) {
                    ReportServerError("Mp3ThreadSintesi", e.what());
                } catch (...) {
                    ReportServerError("Mp3ThreadSintesi", "Errore sconosciuto nella sintesi di un pezzo");
                }

                if (pezzo) pezzo->pronto.store(true);
            }
        }
    } catch (const std::exception& e) {
        lavoro->annullato.store(true);
        ReportServerError("Mp3ThreadSintesi", e.what());
    } catch (...) {
        lavoro->annullato.store(true);
        ReportServerError("Mp3ThreadSintesi", "Errore sconosciuto nel thread di sintesi");
    }

    SetThreadExecutionState(ES_CONTINUOUS);

    if (SUCCEEDED(hrInit)) CoUninitialize();
}

static bool Mp3PreparaScrittore(const std::shared_ptr<Mp3Lavoro>& lavoro, Microsoft::WRL::ComPtr<IMFSinkWriter>& scrittore, DWORD& indiceFlusso) {
    try {
        Microsoft::WRL::ComPtr<IMFCollection> tipiDisponibili;
        if (FAILED(MFTranscodeGetAudioOutputAvailableTypes(MFAudioFormat_MP3, MFT_ENUM_FLAG_ALL, NULL, &tipiDisponibili)) || !tipiDisponibili) {
            LogToFile("Mp3PreparaScrittore: nessun codificatore mp3 disponibile");
            return false;
        }

        DWORD numeroTipi = 0;
        tipiDisponibili->GetElementCount(&numeroTipi);
        if (numeroTipi == 0) {
            LogToFile("Mp3PreparaScrittore: elenco tipi mp3 vuoto");
            return false;
        }

        Microsoft::WRL::ComPtr<IMFMediaType> tipoScelto;
        int punteggioMigliore = -1;

        for (DWORD i = 0; i < numeroTipi; i++) {
            Microsoft::WRL::ComPtr<IUnknown> elemento;
            if (FAILED(tipiDisponibili->GetElement(i, &elemento)) || !elemento) continue;
            Microsoft::WRL::ComPtr<IMFMediaType> tipoCorrente;
            if (FAILED(elemento.As(&tipoCorrente)) || !tipoCorrente) continue;

            UINT32 frequenza = 0;
            UINT32 canali = 0;
            UINT32 byteAlSecondo = 0;
            tipoCorrente->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &frequenza);
            tipoCorrente->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &canali);
            tipoCorrente->GetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, &byteAlSecondo);

            int punteggio = 0;
            if (frequenza == 22050) punteggio += 200;
            else if (frequenza == 24000) punteggio += 180;
            else if (frequenza == 32000) punteggio += 140;
            else if (frequenza == 44100) punteggio += 100;
            if (canali == 1) punteggio += 80;
            int scarto = (int)byteAlSecondo - MP3_BITRATE_BYTE_SEC;
            if (scarto < 0) scarto = -scarto;
            punteggio += (40000 - scarto) / 1000;

            if (punteggio > punteggioMigliore) {
                punteggioMigliore = punteggio;
                tipoScelto = tipoCorrente;
            }
        }

        if (!tipoScelto) {
            LogToFile("Mp3PreparaScrittore: nessun tipo mp3 selezionabile");
            return false;
        }

        UINT32 frequenzaScelta = 44100;
        UINT32 canaliScelti = 1;
        tipoScelto->GetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, &frequenzaScelta);
        tipoScelto->GetUINT32(MF_MT_AUDIO_NUM_CHANNELS, &canaliScelti);
        if (frequenzaScelta == 0) frequenzaScelta = 44100;
        if (canaliScelti == 0) canaliScelti = 1;

        lavoro->formato.wFormatTag = WAVE_FORMAT_PCM;
        lavoro->formato.nChannels = (WORD)canaliScelti;
        lavoro->formato.nSamplesPerSec = frequenzaScelta;
        lavoro->formato.wBitsPerSample = 16;
        lavoro->formato.nBlockAlign = (WORD)((canaliScelti * 16) / 8);
        lavoro->formato.nAvgBytesPerSec = frequenzaScelta * lavoro->formato.nBlockAlign;
        lavoro->formato.cbSize = 0;

        if (FAILED(MFCreateSinkWriterFromURL(lavoro->percorso.c_str(), NULL, NULL, &scrittore)) || !scrittore) {
            LogToFile("Mp3PreparaScrittore: creazione scrittore fallita");
            return false;
        }

        if (FAILED(scrittore->AddStream(tipoScelto.Get(), &indiceFlusso))) {
            LogToFile("Mp3PreparaScrittore: AddStream fallito");
            return false;
        }

        Microsoft::WRL::ComPtr<IMFMediaType> tipoIngresso;
        if (FAILED(MFCreateMediaType(&tipoIngresso)) || !tipoIngresso) return false;
        tipoIngresso->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio);
        tipoIngresso->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM);
        tipoIngresso->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        tipoIngresso->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, frequenzaScelta);
        tipoIngresso->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, canaliScelti);
        tipoIngresso->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, lavoro->formato.nBlockAlign);
        tipoIngresso->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, lavoro->formato.nAvgBytesPerSec);
        tipoIngresso->SetUINT32(MF_MT_ALL_SAMPLES_INDEPENDENT, TRUE);

        if (FAILED(scrittore->SetInputMediaType(indiceFlusso, tipoIngresso.Get(), NULL))) {
            LogToFile("Mp3PreparaScrittore: SetInputMediaType fallito");
            return false;
        }

        return true;
    } catch (const std::exception& e) {
        ReportServerError("Mp3PreparaScrittore", e.what());
        return false;
    } catch (...) {
        ReportServerError("Mp3PreparaScrittore", "Errore sconosciuto nella preparazione del codificatore");
        return false;
    }
}

static bool Mp3ScriviCampioni(IMFSinkWriter* scrittore, DWORD indiceFlusso, const std::vector<BYTE>& dati, LONGLONG& tempoHns, const WAVEFORMATEX& formato) {
    try {
        if (!scrittore || dati.empty()) return true;

        Microsoft::WRL::ComPtr<IMFMediaBuffer> buffer;
        if (FAILED(MFCreateMemoryBuffer((DWORD)dati.size(), &buffer)) || !buffer) return false;

        BYTE* puntatore = nullptr;
        DWORD massimo = 0;
        DWORD corrente = 0;
        if (FAILED(buffer->Lock(&puntatore, &massimo, &corrente)) || !puntatore) return false;
        memcpy(puntatore, dati.data(), dati.size());
        buffer->Unlock();
        buffer->SetCurrentLength((DWORD)dati.size());

        Microsoft::WRL::ComPtr<IMFSample> campione;
        if (FAILED(MFCreateSample(&campione)) || !campione) return false;
        if (FAILED(campione->AddBuffer(buffer.Get()))) return false;

        UINT32 byteAlSecondo = formato.nAvgBytesPerSec ? formato.nAvgBytesPerSec : 88200;
        LONGLONG durata = (LONGLONG)((double)dati.size() * 10000000.0 / (double)byteAlSecondo);
        campione->SetSampleTime(tempoHns);
        campione->SetSampleDuration(durata);

        HRESULT hrScrittura = scrittore->WriteSample(indiceFlusso, campione.Get());
        if (FAILED(hrScrittura)) {
            LogToFile("Mp3ScriviCampioni: WriteSample fallito hr=" + std::to_string(hrScrittura));
            return false;
        }

        tempoHns += durata;
        return true;
    } catch (const std::exception& e) {
        ReportServerError("Mp3ScriviCampioni", e.what());
        return false;
    } catch (...) {
        ReportServerError("Mp3ScriviCampioni", "Errore sconosciuto nella scrittura dei campioni");
        return false;
    }
}

static void Mp3ThreadCoordinatore(std::shared_ptr<Mp3Lavoro> lavoro) {
    HRESULT hrInit = CoInitializeEx(NULL, COINIT_MULTITHREADED);
    mp3LavoriAttivi.fetch_add(1);
    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED);
    lavoro->battitoAttivo.store(true);
    std::thread battito(Mp3ThreadBattito, lavoro);
    bool mfAvviato = false;
    bool esito = false;
    std::wstring messaggio = L"";
    std::vector<std::thread> lavoratori;
    Microsoft::WRL::ComPtr<IMFSinkWriter> scrittore;
    DWORD indiceFlusso = 0;

    try {
        if (lavoro->pezzi.empty()) {
            messaggio = L"nessun testo da convertire";
        } else if (FAILED(MFStartup(MF_VERSION, MFSTARTUP_LITE))) {
            messaggio = L"media foundation non disponibile";
        } else {
            mfAvviato = true;

            if (!Mp3PreparaScrittore(lavoro, scrittore, indiceFlusso)) {
                messaggio = L"codificatore mp3 non disponibile";
            } else if (FAILED(scrittore->BeginWriting())) {
                messaggio = L"avvio scrittura mp3 fallito";
            } else {
                for (int w = 0; w < lavoro->numeroThread; w++) {
                    lavoratori.emplace_back(Mp3ThreadSintesi, lavoro, w);
                }

                LONGLONG tempoHns = 0;
                bool erroreScrittura = false;
                size_t totale = lavoro->pezzi.size();

                for (size_t i = 0; i < totale; i++) {
                    while (!lavoro->pezzi[i]->pronto.load()) {
                        if (lavoro->annullato.load()) break;
                        Sleep(2);
                    }
                    if (lavoro->annullato.load()) break;

                    if (!Mp3ScriviCampioni(scrittore.Get(), indiceFlusso, lavoro->pezzi[i]->pcm, tempoHns, lavoro->formato)) {
                        erroreScrittura = true;
                    }

                    lavoro->byteInAttesa.fetch_sub((long long)lavoro->pezzi[i]->pcm.size());
                    lavoro->pezzi[i]->pcm.clear();
                    lavoro->pezzi[i]->pcm.shrink_to_fit();
                    lavoro->prossimoDaConsumare.store((int)i + 1);

                    Mp3InviaProgresso(lavoro, (int)(((i + 1) * 95) / totale));

                    if (erroreScrittura) break;
                }

                lavoro->annullato.store(lavoro->annullato.load() || erroreScrittura);

                for (auto& t : lavoratori) {
                    if (t.joinable()) t.join();
                }
                lavoratori.clear();

                if (erroreScrittura) {
                    messaggio = L"scrittura mp3 fallita";
                } else if (lavoro->annullato.load()) {
                    messaggio = L"conversione annullata";
                } else {
                    HRESULT hrFine = scrittore->Finalize();
                    if (FAILED(hrFine)) {
                        messaggio = L"chiusura file mp3 fallita";
                        LogToFile("Mp3ThreadCoordinatore: Finalize fallito hr=" + std::to_string(hrFine));
                    } else {
                        esito = true;
                        Mp3InviaProgresso(lavoro, 100);
                    }
                }
            }
        }
    } catch (const std::exception& e) {
        messaggio = L"errore interno";
        ReportServerError("Mp3ThreadCoordinatore", e.what());
    } catch (...) {
        messaggio = L"errore sconosciuto";
        ReportServerError("Mp3ThreadCoordinatore", "Errore sconosciuto nel coordinatore mp3");
    }

    try {
        lavoro->annullato.store(true);
        for (auto& t : lavoratori) {
            if (t.joinable()) t.join();
        }
        lavoratori.clear();
    } catch (...) {
    }

    try {
        lavoro->battitoAttivo.store(false);
        if (battito.joinable()) battito.join();
    } catch (...) {
    }

    try {
        scrittore.Reset();
    } catch (...) {
    }

    if (!esito) {
        try {
            DeleteFileW(lavoro->percorso.c_str());
        } catch (...) {
        }
    }

    if (mfAvviato) {
        try {
            MFShutdown();
        } catch (...) {
        }
    }

    if (SUCCEEDED(hrInit)) CoUninitialize();

    SetThreadExecutionState(ES_CONTINUOUS);
    mp3LavoriAttivi.fetch_sub(1);

    lavoro->finito.store(true);
    Mp3InviaFine(lavoro->id, esito, messaggio);
}

static void Mp3PuliziaLavoriFiniti() {
    try {
        std::lock_guard<std::mutex> lock(mp3Mutex);
        for (auto it = mp3Lavori.begin(); it != mp3Lavori.end();) {
            if (it->second && it->second->finito.load()) {
                if (it->second->coordinatore.joinable()) it->second->coordinatore.join();
                it = mp3Lavori.erase(it);
            } else {
                ++it;
            }
        }
    } catch (...) {
        LogToFile("Mp3PuliziaLavoriFiniti: errore sconosciuto");
    }
}

static void Mp3ArrestaTuttiLavori() {
    try {
        std::vector<std::shared_ptr<Mp3Lavoro>> daAttendere;
        {
            std::lock_guard<std::mutex> lock(mp3Mutex);
            for (auto& coppia : mp3Lavori) {
                if (coppia.second) {
                    coppia.second->annullato.store(true);
                    daAttendere.push_back(coppia.second);
                }
            }
        }
        for (auto& lavoro : daAttendere) {
            if (lavoro->coordinatore.joinable()) lavoro->coordinatore.join();
        }
        std::lock_guard<std::mutex> lock(mp3Mutex);
        mp3Lavori.clear();
    } catch (...) {
        LogToFile("Mp3ArrestaTuttiLavori: errore sconosciuto");
    }
}

static bool Mp3GestisciComando(const std::wstring& wcmd) {
    try {
        if (wcmd.rfind(L"MP3_START|", 0) == 0) {
            Mp3PuliziaLavoriFiniti();
            std::vector<std::wstring> campi = Mp3DividiComando(wcmd.substr(10), 6);
            if (campi.size() < 6) {
                LogToFile("MP3_START: parametri insufficienti");
                return true;
            }
            auto lavoro = std::make_shared<Mp3Lavoro>();
            lavoro->id = campi[0];
            lavoro->voce = campi[1];
            lavoro->velocita = Mp3ConvertiIntero(campi[2], 50);
            lavoro->volume = Mp3ConvertiIntero(campi[3], 100);
            lavoro->numeroThread = Mp3ConvertiIntero(campi[4], 0);
            if (lavoro->numeroThread <= 0) {
                unsigned int nuclei = std::thread::hardware_concurrency();
                if (nuclei == 0) nuclei = 4;
                lavoro->numeroThread = (int)(nuclei * 2);
            }
            if (lavoro->numeroThread > MP3_THREAD_MASSIMI) lavoro->numeroThread = MP3_THREAD_MASSIMI;
            if (lavoro->numeroThread < 1) lavoro->numeroThread = 1;
            lavoro->percorso = campi[5];
            {
                std::lock_guard<std::mutex> lock(mp3Mutex);
                mp3Lavori[lavoro->id] = lavoro;
            }
            SendEvent(L"MP3_PROGRESS|" + lavoro->id + L"|0");
            return true;
        }

        if (wcmd.rfind(L"MP3_TEXT|", 0) == 0) {
            std::vector<std::wstring> campi = Mp3DividiComando(wcmd.substr(9), 3);
            if (campi.size() < 3) return true;
            std::shared_ptr<Mp3Lavoro> lavoro;
            {
                std::lock_guard<std::mutex> lock(mp3Mutex);
                auto it = mp3Lavori.find(campi[0]);
                if (it != mp3Lavori.end()) lavoro = it->second;
            }
            if (!lavoro) return true;
            auto pezzo = std::make_unique<Mp3Pezzo>();
            pezzo->testo = SanitizeTestoParlato(campi[2]);
            lavoro->pezzi.push_back(std::move(pezzo));
            return true;
        }

        if (wcmd.rfind(L"MP3_END|", 0) == 0) {
            std::wstring id = wcmd.substr(8);
            std::shared_ptr<Mp3Lavoro> lavoro;
            {
                std::lock_guard<std::mutex> lock(mp3Mutex);
                auto it = mp3Lavori.find(id);
                if (it != mp3Lavori.end()) lavoro = it->second;
            }
            if (!lavoro) {
                Mp3InviaFine(id, false, L"lavoro non trovato");
                return true;
            }
            lavoro->coordinatore = std::thread(Mp3ThreadCoordinatore, lavoro);
            return true;
        }

        if (wcmd.rfind(L"MP3_PREVIEW|", 0) == 0) {
            std::vector<std::wstring> campi = Mp3DividiComando(wcmd.substr(12), 4);
            if (campi.size() < 4) return true;
            Mp3AvviaAnteprima(campi[0], Mp3ConvertiIntero(campi[1], 50), Mp3ConvertiIntero(campi[2], 100), SanitizeTestoParlato(campi[3]));
            return true;
        }

        if (wcmd == L"MP3_PREVIEW_STOP") {
            Mp3FermaAnteprima();
            return true;
        }

        if (wcmd.rfind(L"MP3_ABORT|", 0) == 0) {
            std::wstring id = wcmd.substr(10);
            std::shared_ptr<Mp3Lavoro> lavoro;
            {
                std::lock_guard<std::mutex> lock(mp3Mutex);
                auto it = mp3Lavori.find(id);
                if (it != mp3Lavori.end()) lavoro = it->second;
            }
            if (lavoro) lavoro->annullato.store(true);
            return true;
        }

        return false;
    } catch (const std::exception& e) {
        ReportServerError("Mp3GestisciComando", e.what());
        return true;
    } catch (...) {
        ReportServerError("Mp3GestisciComando", "Errore sconosciuto nella gestione comando mp3");
        return true;
    }
}

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR lpCmdLine, int nCmdShow) {
    SetUnhandledExceptionFilter(TopLevelExceptionHandler);

    HANDLE hJob = CreateJobObjectW(NULL, NULL);
    if (hJob) {
        JOBOBJECT_BASIC_UI_RESTRICTIONS uiRes = {0};
        uiRes.UIRestrictionsClass = JOB_OBJECT_UILIMIT_ALL;
        SetInformationJobObject(hJob, JobObjectBasicUIRestrictions, &uiRes, sizeof(uiRes));
        AssignProcessToJobObject(hJob, GetCurrentProcess());
    }

    HANDLE hMutex = NULL;

    try {
        std::wstring wCmdLine(lpCmdLine);
        std::string cmdLineStr = WideToUtf8(wCmdLine);

        size_t first = cmdLineStr.find_first_not_of(" \t\r\n\"");
        if (first == std::string::npos) {
            ReportServerError("MAIN", "Parametri di avvio assenti. Chiusura.");
            return 1;
        }
        
        size_t last = cmdLineStr.find_last_not_of(" \t\r\n\"");
        cmdLineStr = cmdLineStr.substr(first, (last - first + 1));

        std::string tokenStr = cmdLineStr;
        std::string portStr = "44445";

        size_t spacePos = cmdLineStr.find(' ');
        if (spacePos != std::string::npos) {
            tokenStr = cmdLineStr.substr(0, spacePos);
            portStr = cmdLineStr.substr(spacePos + 1);
            size_t pFirst = portStr.find_first_not_of(" \t");
            if (pFirst != std::string::npos) {
                portStr = portStr.substr(pFirst);
            }
        }

        globalAuthToken = "AUTH|" + tokenStr;
        try { globalPort = std::stoi(portStr); } catch(...) { globalPort = 44445; }
        
        std::string mutexName = "WGSapi_Mutex_" + tokenStr;
        hMutex = CreateMutexA(NULL, TRUE, mutexName.c_str());
        if (GetLastError() == ERROR_ALREADY_EXISTS) {
            ReportServerError("MAIN", "Processo gia in esecuzione (Mutex bloccato). Chiusura.");
            CloseHandle(hMutex);
            return 0;
        }
        
        SecureClearStr(tokenStr);

        WSADATA wsaData;
        WSAStartup(MAKEWORD(2, 2), &wsaData);

        hCommandEvent = CreateEvent(NULL, FALSE, FALSE, NULL);
        globalQueues.Init(hCommandEvent);

        std::thread tcpThread(TcpListenerThreadFunc);
        std::thread pipeThread(PipeListenerThreadFunc);
        std::thread shmThread(ShmListenerThreadFunc);
        std::thread watchdogThread(MemoryWatchdogThreadFunc);

        SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_HIGHEST);
        CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);

        globalWfx.wFormatTag = WAVE_FORMAT_PCM;
        globalWfx.nChannels = 1;
        globalWfx.nSamplesPerSec = 44100;
        globalWfx.wBitsPerSample = 16;
        globalWfx.nBlockAlign = (globalWfx.nChannels * globalWfx.wBitsPerSample) / 8;
        globalWfx.nAvgBytesPerSec = globalWfx.nSamplesPerSec * globalWfx.nBlockAlign;
        globalWfx.cbSize = 0;

        Microsoft::WRL::ComPtr<IMMDeviceEnumerator> pEnumInit;
        if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), &pEnumInit))) {
            Microsoft::WRL::ComPtr<IMMDevice> pDevInit;
            if (SUCCEEDED(pEnumInit->GetDefaultAudioEndpoint(eRender, eConsole, &pDevInit))) {
                Microsoft::WRL::ComPtr<IAudioClient> pAudioClientInit;
                if (SUCCEEDED(pDevInit->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (void**)&pAudioClientInit))) {
                    WAVEFORMATEX* pWfxNative = nullptr;
                    if (SUCCEEDED(pAudioClientInit->GetMixFormat(&pWfxNative)) && pWfxNative != nullptr) {
                        if (pWfxNative->nSamplesPerSec >= 8000) {
                            globalWfx.nSamplesPerSec = pWfxNative->nSamplesPerSec;
                            globalWfx.nAvgBytesPerSec = globalWfx.nSamplesPerSec * globalWfx.nBlockAlign;
                        }
                        CoTaskMemFree(pWfxNative);
                    }
                }
            }
        }

        pGlobalStream = new CWasapiStream();

        Microsoft::WRL::ComPtr<ISpVoice> pVoice;
        CoCreateInstance(CLSID_SpVoice, NULL, CLSCTX_ALL, IID_ISpVoice, &pVoice);

        Microsoft::WRL::ComPtr<ISpStream> pSpStream;
        CoCreateInstance(CLSID_SpStream, NULL, CLSCTX_ALL, IID_ISpStream, &pSpStream);

        HANDLE hSapiEvent = NULL;

        if (pVoice && pSpStream && pGlobalStream) {
            pSpStream->SetBaseStream(pGlobalStream, SPDFID_WaveFormatEx, &globalWfx);
            pVoice->SetOutput(pSpStream.Get(), TRUE);
            pVoice->SetPriority(SPVPRI_OVER);
            // SAPI sintetizza sempre a volume pieno: l'attenuazione viene applicata dal
            // renderer WASAPI, cosi' il cambio di volume ha effetto immediato.
            pVoice->SetVolume(100);
            pVoice->SetNotifyWin32Event();
            hSapiEvent = pVoice->GetNotifyEventHandle();
            pVoice->SetInterest(SPFEI(SPEI_START_INPUT_STREAM) | SPFEI(SPEI_WORD_BOUNDARY) | SPFEI(SPEI_END_INPUT_STREAM), SPFEI(SPEI_START_INPUT_STREAM) | SPFEI(SPEI_WORD_BOUNDARY) | SPFEI(SPEI_END_INPUT_STREAM));
        }

        std::thread wasapiThreadObj(WasapiThreadFunc);
        std::thread aliveThreadObj(AliveHeartbeatThreadFunc);

        HANDLE handles[2];
        handles[0] = hCommandEvent;
        handles[1] = hSapiEvent;
        DWORD numHandles = (hSapiEvent != NULL) ? 2 : 1;

        std::time_t startupTime = std::time(nullptr);
        bool everConnected = false;

        while (keepRunning) {
            DWORD res = MsgWaitForMultipleObjects(numHandles, handles, FALSE, 1000, QS_ALLINPUT);

            if (isClientConnected) everConnected = true;

            if (!everConnected && mp3LavoriAttivi.load() == 0 && (std::time(nullptr) - startupTime > 15)) {
                SendShutdownSignal(L"Nessun client connesso all'avvio");
                keepRunning = false;
                break;
            }

            if (everConnected && !isClientConnected && activeTransport == TransportType::None && mp3LavoriAttivi.load() == 0 && (std::time(nullptr) - lastDisconnectTime > 30)) {
                SendShutdownSignal(L"Client disconnesso per troppo tempo");
                keepRunning = false;
                break;
            }

            if (res == WAIT_OBJECT_0) {
                while (keepRunning) {
                    std::wstring wcmd;
                    
                    if (!globalQueues.PopCommand(wcmd)) {
                        break;
                    }
                    
                    if (Mp3GestisciComando(wcmd)) {
                        continue;
                    }
                    
                    if (pVoice) {
                        try {
                            if (wcmd == L"STOP") {
                                {
                                    std::lock_guard<std::mutex> lock(speechQueueMutex);
                                    speechQueue.clear();
                                }
                                currentMsgId = -1;
                lastEnqueuedMsgId = -1;
                                currentSeq = -1;
                                currentIsLast = 0;
                                sapiIsSpeaking = false;
                                pendingDoneMsgId = -1;
                                pendingDoneTargetHns = -1;
                                tickBufferVuotoDaQuando = 0;
                                pVoice->Pause();
                                pVoice->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
                                pVoice->Resume();
                                if (pGlobalStream) pGlobalStream->AbortAndClear();
                            } else if (wcmd.rfind(L"SPEAK|", 0) == 0) {
                                size_t p1 = wcmd.find(L"|");
                                size_t p2 = (p1 != std::wstring::npos) ? wcmd.find(L"|", p1 + 1) : std::wstring::npos;
                                size_t p3 = (p2 != std::wstring::npos) ? wcmd.find(L"|", p2 + 1) : std::wstring::npos;
                                size_t p4 = (p3 != std::wstring::npos) ? wcmd.find(L"|", p3 + 1) : std::wstring::npos;
                                size_t p5 = (p4 != std::wstring::npos) ? wcmd.find(L"|", p4 + 1) : std::wstring::npos;

                                if (p1 != std::wstring::npos && p2 != std::wstring::npos && p3 != std::wstring::npos && p4 != std::wstring::npos && p5 != std::wstring::npos) {
                                    int msgId = -1;
                                    int seq = -1;
                                    int isLast = 0;
                                    int offsetGlobale = 0;
                                    try { msgId = std::stoi(wcmd.substr(p1 + 1, p2 - p1 - 1)); } catch (...) {}
                                    try { seq = std::stoi(wcmd.substr(p2 + 1, p3 - p2 - 1)); } catch (...) {}
                                    try { isLast = std::stoi(wcmd.substr(p3 + 1, p4 - p3 - 1)); } catch (...) {}
                                    try { offsetGlobale = std::stoi(wcmd.substr(p4 + 1, p5 - p4 - 1)); } catch (...) {}

                                    std::wstring text = wcmd.substr(p5 + 1);
                                    text = SanitizeTestoParlato(text);

                                    bool nuovoMessaggio = (msgId != lastEnqueuedMsgId.load());
                                    lastEnqueuedMsgId = msgId;

                                    if (nuovoMessaggio) {
                                        {
                                            std::lock_guard<std::mutex> lock(speechQueueMutex);
                                            speechQueue.clear();
                                        }
                                        pVoice->Pause();
                                        pVoice->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
                                        pVoice->Resume();
                                        if (pGlobalStream) pGlobalStream->AbortAndClear();
                                        sapiIsSpeaking = false;
                                        pendingDoneMsgId = -1;
                                        pendingDoneTargetHns = -1;
                                        tickBufferVuotoDaQuando = 0;
                                    }

                                    SpeechItem newItem;
                                    newItem.msgId = msgId;
                                    newItem.seq = seq;
                                    newItem.isLast = isLast;
                                    newItem.offsetGlobale = offsetGlobale;
                                    newItem.text = text;

                                    // Accodiamo solo i METADATI (verranno associati al chunk giusto
                                    // quando SAPI segnala SPEI_START_INPUT_STREAM) e mandiamo SUBITO
                                    // il testo al motore. SAPI accoda internamente le Speak()
                                    // successive e puo' iniziare a sintetizzare il chunk N+1 mentre
                                    // il chunk N sta ancora suonando: e' questo che elimina il gap
                                    // dovuto al warm-up del motore ad ogni nuova utterance, che prima
                                    // veniva esposto perche' si aspettava la fine del chunk N prima
                                    // di anche solo chiamare Speak() per il chunk N+1.
                                    {
                                        std::lock_guard<std::mutex> lock(speechQueueMutex);
                                        speechQueue.push_back(newItem);
                                    }

                                    HRESULT hrSpeak = pVoice->Speak(newItem.text.c_str(), SPF_ASYNC | SPF_IS_NOT_XML, NULL);
                                    if (FAILED(hrSpeak)) {
                                        LogToFile("SPEAK: pVoice->Speak fallito hr=" + std::to_string(hrSpeak));
                                        std::lock_guard<std::mutex> lock(speechQueueMutex);
                                        if (!speechQueue.empty()) speechQueue.pop_back();
                                    } else {
                                        sapiIsSpeaking = true;
                                    }
                                } else if (p1 != std::wstring::npos && p2 != std::wstring::npos && p3 == std::wstring::npos) {
                                    std::wstring text = wcmd.substr(p2 + 1);
                                    text = SanitizeTestoParlato(text);
                                    pVoice->Pause();
                                    pVoice->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
                                    pVoice->Resume();
                                    if (pGlobalStream) pGlobalStream->ResetFlag();
                                    pVoice->Speak(text.c_str(), SPF_ASYNC, NULL);
                                }
                            } else if (wcmd == L"PAUSE") {
                                pVoice->Pause();
                            } else if (wcmd == L"RESUME") {
                                pVoice->Resume();
                            } else if (wcmd == L"GET_VOICES") {
                                std::wstring voci = L"VOICES|";
                                Microsoft::WRL::ComPtr<IEnumSpObjectTokens> pEnum;
                                
                                if (SUCCEEDED(SpEnumTokens(SPCAT_VOICES, NULL, NULL, &pEnum))) {
                                    ULONG count = 0; pEnum->GetCount(&count);
                                    for (ULONG i = 0; i < count; i++) {
                                        Microsoft::WRL::ComPtr<ISpObjectToken> pToken;
                                        if (SUCCEEDED(pEnum->Item(i, &pToken))) {
                                            WCHAR* pszDesc = nullptr;
                                            if (SUCCEEDED(SpGetDescription(pToken.Get(), &pszDesc))) {
                                                voci += pszDesc; voci += L"|"; CoTaskMemFree(pszDesc);
                                            }
                                        }
                                    }
                                }
                                
                                Microsoft::WRL::ComPtr<ISpObjectTokenCategory> pCat;
                                if (SUCCEEDED(CoCreateInstance(CLSID_SpObjectTokenCategory, NULL, CLSCTX_ALL, IID_ISpObjectTokenCategory, &pCat))) {
                                    if (SUCCEEDED(pCat->SetId(SAPI_ONECORE_REG_KEY, FALSE))) {
                                        Microsoft::WRL::ComPtr<IEnumSpObjectTokens> pEnumOneCore;
                                        if (SUCCEEDED(pCat->EnumTokens(NULL, NULL, &pEnumOneCore))) {
                                            ULONG count = 0; pEnumOneCore->GetCount(&count);
                                            for (ULONG i = 0; i < count; i++) {
                                                Microsoft::WRL::ComPtr<ISpObjectToken> pToken;
                                                if (SUCCEEDED(pEnumOneCore->Item(i, &pToken))) {
                                                    WCHAR* pszDesc = nullptr;
                                                    if (SUCCEEDED(SpGetDescription(pToken.Get(), &pszDesc))) {
                                                        voci += pszDesc; voci += L" (OneCore)|"; CoTaskMemFree(pszDesc);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                SendEvent(voci);
                                
                            } else if (wcmd == L"GET_AUDIO_DEVICES") {
                                std::wstring devs = L"AUDIO_DEVICES|";
                                Microsoft::WRL::ComPtr<IMMDeviceEnumerator> pEnumDev;
                                if (SUCCEEDED(CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_ALL, __uuidof(IMMDeviceEnumerator), &pEnumDev))) {
                                    Microsoft::WRL::ComPtr<IMMDeviceCollection> pCol;
                                    if (SUCCEEDED(pEnumDev->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &pCol))) {
                                        UINT count = 0; pCol->GetCount(&count);
                                        for(UINT i = 0; i < count; i++) {
                                            Microsoft::WRL::ComPtr<IMMDevice> pDevItem;
                                            if (SUCCEEDED(pCol->Item(i, &pDevItem))) {
                                                LPWSTR id = nullptr;
                                                if (FAILED(pDevItem->GetId(&id)) || id == nullptr) {
                                                    continue;
                                                }
                                                Microsoft::WRL::ComPtr<IPropertyStore> pProps;
                                                if (SUCCEEDED(pDevItem->OpenPropertyStore(STGM_READ, &pProps))) {
                                                    PROPVARIANT varName;
                                                    PropVariantInit(&varName);
                                                    if (SUCCEEDED(pProps->GetValue(PKEY_Device_FriendlyName, &varName)) && varName.vt == VT_LPWSTR && varName.pwszVal != nullptr) {
                                                        devs += id; devs += L"="; devs += varName.pwszVal; devs += L"|";
                                                    }
                                                    PropVariantClear(&varName);
                                                }
                                                CoTaskMemFree(id);
                                            }
                                        }
                                    }
                                }
                                SendEvent(devs);
                            } else if (wcmd.rfind(L"SET_AUDIO_DEVICE|", 0) == 0) {
                            } else if (wcmd.rfind(L"SET_VOICE|", 0) == 0) {
                                std::wstring targetName = wcmd.substr(10);
                                while(!targetName.empty() && iswspace(targetName.back())) targetName.pop_back();
                                while(!targetName.empty() && iswspace(targetName.front())) targetName.erase(0, 1);
                                
                                int targetIdx = -1;
                                try { targetIdx = std::stoi(targetName); } catch(...) {}
                                
                                std::wstring targetLower = targetName;
                                CharLowerBuffW(&targetLower[0], (DWORD)targetLower.length());
                                
                                int currentIdx = 0;
                                bool found = false;

                                if (pGlobalStream) pGlobalStream->AbortAndClear();
                                needsWasapiReset = true;
                                pVoice->Pause();
                                pVoice->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
                                pVoice->Resume();

                                {
                                    std::lock_guard<std::mutex> lock(speechQueueMutex);
                                    speechQueue.clear();
                                }
                                currentMsgId = -1;
                lastEnqueuedMsgId = -1;
                                currentSeq = -1;
                                currentIsLast = 0;
                                sapiIsSpeaking = false;
                                // needsWasapiReset azzerera' l'orologio hardware: un eventuale
                                // target DONE calcolato sul vecchio orologio non ha piu' senso.
                                pendingDoneMsgId = -1;
                                pendingDoneTargetHns = -1;

                                Microsoft::WRL::ComPtr<IEnumSpObjectTokens> pEnum;
                                if (SUCCEEDED(SpEnumTokens(SPCAT_VOICES, NULL, NULL, &pEnum))) {
                                    ULONG count = 0; pEnum->GetCount(&count);
                                    for (ULONG i = 0; i < count; i++) {
                                        Microsoft::WRL::ComPtr<ISpObjectToken> pToken;
                                        if (SUCCEEDED(pEnum->Item(i, &pToken))) {
                                            WCHAR* pszDesc = nullptr;
                                            if (SUCCEEDED(SpGetDescription(pToken.Get(), &pszDesc))) {
                                                std::wstring descLower = pszDesc;
                                                CharLowerBuffW(&descLower[0], (DWORD)descLower.length());
                                                
                                                if (descLower == targetLower || (targetLower.length() > 3 && descLower.find(targetLower) != std::wstring::npos) || currentIdx == targetIdx) {
                                                    pVoice->SetVoice(pToken.Get());
                                                    found = true;
                                                }
                                                CoTaskMemFree(pszDesc);
                                            }
                                            if (found) break;
                                        }
                                        currentIdx++;
                                    }
                                }
                                
                                if (!found) {
                                    Microsoft::WRL::ComPtr<ISpObjectTokenCategory> pCat;
                                    if (SUCCEEDED(CoCreateInstance(CLSID_SpObjectTokenCategory, NULL, CLSCTX_ALL, IID_ISpObjectTokenCategory, &pCat))) {
                                        if (SUCCEEDED(pCat->SetId(SAPI_ONECORE_REG_KEY, FALSE))) {
                                            Microsoft::WRL::ComPtr<IEnumSpObjectTokens> pEnumOneCore;
                                            if (SUCCEEDED(pCat->EnumTokens(NULL, NULL, &pEnumOneCore))) {
                                                ULONG count = 0; pEnumOneCore->GetCount(&count);
                                                for (ULONG i = 0; i < count; i++) {
                                                    Microsoft::WRL::ComPtr<ISpObjectToken> pToken;
                                                    if (SUCCEEDED(pEnumOneCore->Item(i, &pToken))) {
                                                        WCHAR* pszDesc = nullptr;
                                                        if (SUCCEEDED(SpGetDescription(pToken.Get(), &pszDesc))) {
                                                            std::wstring descLower = pszDesc;
                                                            descLower += L" (onecore)";
                                                            CharLowerBuffW(&descLower[0], (DWORD)descLower.length());
                                                            
                                                            if (descLower == targetLower || (targetLower.length() > 3 && descLower.find(targetLower) != std::wstring::npos) || currentIdx == targetIdx) {
                                                                pVoice->SetVoice(pToken.Get());
                                                                found = true;
                                                            }
                                                            CoTaskMemFree(pszDesc);
                                                        }
                                                        if (found) break;
                                                    }
                                                    currentIdx++;
                                                }
                                            }
                                        }
                                    }
                                }
                            } else if (wcmd.rfind(L"SET_RATE|", 0) == 0) {
                                try { 
                                    int rateVal = std::stoi(wcmd.substr(9));
                                    int sapiRate = (rateVal / 5) - 10;
                                    if (sapiRate < -10) sapiRate = -10;
                                    if (sapiRate > 10) sapiRate = 10;
                                    pVoice->SetRate(sapiRate); 
                                } catch (...) {}
                            } else if (wcmd.rfind(L"SET_VOLUME|", 0) == 0) {
                                try {
                                    int volumeVal = std::stoi(wcmd.substr(11));
                                    if (volumeVal < 0) volumeVal = 0;
                                    if (volumeVal > 100) volumeVal = 100;
                                    // Il volume viene applicato dal renderer WASAPI in PullAudio,
                                    // quindi ha effetto immediato anche sull'audio gia' sintetizzato
                                    // e ancora in coda nel buffer.
                                    volumeCorrente.store(volumeVal);
                                } catch (...) {}
                            }
                        } catch (const std::exception& e) {
                            ReportServerError("wWinMain_SapiCommand", e.what());
                        } catch (...) {
                            ReportServerError("wWinMain_SapiCommand", "Errore sconosciuto in esecuzione comando SAPI");
                        }
                    }
                }
            } else if (numHandles == 2 && res == WAIT_OBJECT_0 + 1) {
                if (pVoice) {
                    try {
                        SPEVENT event;
                        ULONG fetched = 0;
                        while (pVoice->GetEvents(1, &event, &fetched) == S_OK && fetched > 0) {
                            if (event.eEventId == SPEI_WORD_BOUNDARY) {
                                int posLocale = (int)event.lParam;
                                int lung = (int)event.wParam;
                                int posAssoluta = posLocale + currentOffsetGlobale.load();
                                std::wstring msg = L"WORD|" + std::to_wstring(posAssoluta) + L"|" + std::to_wstring(lung) + L"|";
                                SendEvent(msg);
                            } else if (event.eEventId == SPEI_START_INPUT_STREAM) {
                                // Il motore ha iniziato DAVVERO a processare questo chunk (era gia'
                                // in coda dentro SAPI dalla chiamata Speak() fatta al momento
                                // dell'arrivo del comando SPEAK|). Qui, e non prima, aggiorniamo i
                                // metadati "correnti": e' questo l'evento giusto per sapere quale
                                // chunk sta effettivamente per essere sintetizzato/riprodotto.
                                std::lock_guard<std::mutex> lock(speechQueueMutex);
                                if (!speechQueue.empty()) {
                                    SpeechItem startedItem = speechQueue.front();
                                    speechQueue.pop_front();
                                    currentMsgId = startedItem.msgId;
                                    currentSeq = startedItem.seq;
                                    currentIsLast = startedItem.isLast;
                                    currentOffsetGlobale = startedItem.offsetGlobale;
                                }
                            } else if (event.eEventId == SPEI_END_INPUT_STREAM) {
                                int finishedMsgId = currentMsgId.load();
                                int finishedIsLast = currentIsLast.load();

                                bool haveNext;
                                {
                                    std::lock_guard<std::mutex> lock(speechQueueMutex);
                                    haveNext = !speechQueue.empty();
                                    if (!haveNext) sapiIsSpeaking = false;
                                }

                                if (!haveNext && finishedIsLast != 0 && finishedMsgId >= 0) {
                                    // A questo punto SAPI ha gia' scritto in modo sincrono TUTTI
                                    // i byte audio residui di questo messaggio in pGlobalStream
                                    // (SPEI_END_INPUT_STREAM viene segnalato solo a scrittura
                                    // completata). Calcoliamo quindi il frame esatto, sull'orologio
                                    // hardware del dispositivo, in cui l'ultimo campione di questo
                                    // messaggio diventera' udibile:
                                    //   target = frame gia' consegnati al device + frame ancora
                                    //            in coda nel nostro buffer sorgente
                                    // Questa somma resta invariata anche se nel frattempo il thread
                                    // WASAPI continua a consumare il buffer sorgente e ad aumentare
                                    // il conteggio consegnato: i due termini si compensano esattamente.
                                    size_t bytesResidui = pGlobalStream ? pGlobalStream->GetAvailableBytes() : 0;
                                    UINT32 blockAlign = globalWfx.nBlockAlign ? globalWfx.nBlockAlign : 1;
                                    UINT32 sampleRate = globalWfx.nSamplesPerSec ? globalWfx.nSamplesPerSec : 1;
                                    ULONGLONG framesResidui = bytesResidui / blockAlign;
                                    ULONGLONG targetFrame = totalFramesRenderedToDevice.load() + framesResidui;
                                    LONGLONG targetHns = (LONGLONG)((double)targetFrame * 10000000.0 / (double)sampleRate);

                                    // Ordine di scrittura importante: il target deve essere gia'
                                    // valido nel momento in cui il thread WASAPI vede l'ID valido.
                                    pendingDoneTargetHns = targetHns;
                                    pendingDoneMsgId = finishedMsgId;
                                }
                            }
                            SpClearEvent(&event);
                        }
                    } catch (const std::exception& e) {
                        ReportServerError("wWinMain_GetEvents", e.what());
                    } catch (...) {
                        ReportServerError("wWinMain_GetEvents", "Errore sconosciuto in eventi SAPI");
                    }
                }
            } else if (res == WAIT_OBJECT_0 + numHandles) {
                try {
                    MSG msg;
                    while (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE)) {
                        TranslateMessage(&msg);
                        DispatchMessage(&msg);
                    }
                } catch (const std::exception& e) {
                    ReportServerError("wWinMain_MessageLoop", e.what());
                } catch (...) {
                    ReportServerError("wWinMain_MessageLoop", "Errore sconosciuto nel MessageLoop");
                }
            }
        }

        Mp3FermaAnteprima();
        Mp3ArrestaTuttiLavori();

        DisconnectClient();

        tcpThread.join();
        pipeThread.join();
        shmThread.join();
        watchdogThread.join();
        aliveThreadObj.join();
        wasapiThreadObj.join();

        if (pVoice) {
            try {
                pVoice->Speak(NULL, SPF_PURGEBEFORESPEAK | SPF_ASYNC, NULL);
            } catch (...) {}
        }

        CloseHandle(hCommandEvent);
        if (pGlobalStream) pGlobalStream->Release();
        CoUninitialize();
        WSACleanup();
        if (hJob) CloseHandle(hJob);
        if (hMutex) CloseHandle(hMutex);
        return 0;
    } catch (const std::exception& e) {
        ReportServerError("MAIN_FATAL", e.what());
        SendShutdownSignal(L"FATAL_MAIN_ERROR");
        if (hJob) CloseHandle(hJob);
        if (hMutex) CloseHandle(hMutex);
        return 1;
    } catch (...) {
        ReportServerError("MAIN_FATAL", "Errore fatale imprevisto in wWinMain");
        SendShutdownSignal(L"FATAL_MAIN_ERROR");
        if (hJob) CloseHandle(hJob);
        if (hMutex) CloseHandle(hMutex);
        return 1;
    }
}