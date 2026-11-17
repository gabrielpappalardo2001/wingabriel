#define NOMINMAX
#include <windows.h>
#include <iostream>
#include <fstream>
#include <string>
#include <vector>
#include <tlhelp32.h>
#include <wlanapi.h>
#include <BluetoothAPIs.h>
#include <atomic>
#include <mutex>
#include <chrono>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Globalization.h>
#include <winrt/Windows.Media.SpeechRecognition.h>

#pragma comment(lib, "user32.lib")
#pragma comment(lib, "windowsapp.lib")
#pragma comment(lib, "wlanapi.lib")
#pragma comment(lib, "Bthprops.lib")

std::string baseDir = "";
std::atomic<bool> exitRequested(false);
std::mutex logMutex;

const wchar_t* NOME_EVENTO_ATTIVAZIONE = L"WGParolaAttivazioneRilevata";
std::atomic<bool> riconoscimentoRichiesto(false);
std::atomic<bool> riavvioRiconoscimento(false);
std::atomic<bool> confidenzaMediaAmmessa(false);
std::atomic<int> disponibilitaItaliano(-1);
std::atomic<int> statoRiconoscimento(0);
std::atomic<unsigned long long> ultimaAttivazione(0);
HANDLE eventoControlloRiconoscimento = NULL;
HANDLE eventoAttivazione = NULL;

void LogToFile(const std::string& message) {
    std::lock_guard<std::mutex> lock(logMutex);
    try {
        std::string logPath = baseDir + "\\dati\\logs\\logservice.txt";
        std::ofstream logFile(logPath, std::ios_base::app);
        if (logFile.is_open()) {
            SYSTEMTIME st;
            GetLocalTime(&st);
            logFile << "[" << st.wYear << "-" << st.wMonth << "-" << st.wDay << " "
                    << st.wHour << ":" << st.wMinute << ":" << st.wSecond << "] "
                    << message << "\n";
            logFile.close();
        }
    } catch (...) {
    }
}

void RunCommandHidden(const std::string& cmd) {
    try {
        STARTUPINFOA si = { sizeof(STARTUPINFOA) };
        PROCESS_INFORMATION pi;
        std::string cmdCopy = cmd;
        if (CreateProcessA(NULL, (LPSTR)cmdCopy.c_str(), NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi)) {
            WaitForSingleObject(pi.hProcess, 15000);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        } else {
            LogToFile("Errore esecuzione comando: " + cmd);
        }
    } catch (...) {
        LogToFile("Eccezione durante esecuzione comando: " + cmd);
    }
}

bool IsProcessRunning(const std::wstring& processName) {
    bool exists = false;
    try {
        PROCESSENTRY32W entry;
        entry.dwSize = sizeof(PROCESSENTRY32W);
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot != INVALID_HANDLE_VALUE) {
            if (Process32FirstW(snapshot, &entry)) {
                do {
                    if (!_wcsicmp(entry.szExeFile, processName.c_str())) {
                        exists = true;
                        break;
                    }
                } while (Process32NextW(snapshot, &entry));
            }
            CloseHandle(snapshot);
        } else {
            LogToFile("Errore CreateToolhelp32Snapshot");
        }
    } catch (...) {
        LogToFile("Eccezione in IsProcessRunning");
    }
    return exists;
}

void KillProcessByName(const std::wstring& processName) {
    try {
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot != INVALID_HANDLE_VALUE) {
            PROCESSENTRY32W entry;
            entry.dwSize = sizeof(PROCESSENTRY32W);
            if (Process32FirstW(snapshot, &entry)) {
                do {
                    if (!_wcsicmp(entry.szExeFile, processName.c_str())) {
                        HANDLE hProcess = OpenProcess(PROCESS_TERMINATE, FALSE, entry.th32ProcessID);
                        if (hProcess != NULL) {
                            TerminateProcess(hProcess, 0);
                            CloseHandle(hProcess);
                        }
                    }
                } while (Process32NextW(snapshot, &entry));
            }
            CloseHandle(snapshot);
        }
    } catch (...) {
        LogToFile("Eccezione in KillProcessByName");
    }
}

DWORD WINAPI MonitorProcess(LPVOID lpParam) {
    std::wstring exeName = L"wingabriel.exe";
    while (!exitRequested) {
        Sleep(5000);
        if (exitRequested) break;
        try {
            if (!IsProcessRunning(exeName)) {
                if (riconoscimentoRichiesto) {
                    riconoscimentoRichiesto = false;
                    if (eventoControlloRiconoscimento != NULL) SetEvent(eventoControlloRiconoscimento);
                }
                std::string appPath = baseDir + "\\wingabriel.exe";
                STARTUPINFOA si = { sizeof(STARTUPINFOA) };
                PROCESS_INFORMATION pi;
                if (CreateProcessA(appPath.c_str(), NULL, NULL, NULL, FALSE, 0, NULL, baseDir.c_str(), &si, &pi)) {
                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                } else {
                    LogToFile("Impossibile riavviare wingabriel.exe");
                }
            }
        } catch (...) {
            LogToFile("Eccezione nel thread MonitorProcess");
        }
    }
    return 0;
}

// --- Fallback di sicurezza per Win+D --------------------------------------------------
// WGTastiera (nel processo wingabriel.exe) intercetta gia' Win+D/Win+M con un hook di
// tastiera di basso livello e gestisce da solo minimizzazione + ritorno al desktop. Windows
// pero' puo' rimuovere silenziosamente un hook del genere se la sua callback e' troppo
// lenta anche solo per un istante, e in generale l'hook vive dentro wingabriel.exe: se quel
// processo si blocca, l'hook con lui non risponde piu'. Questo thread gira nel servizio,
// un processo indipendente, e legge lo stato FISICO dei tasti con GetAsyncKeyState: non
// passa dalla catena di hook di wingabriel.exe, quindi funziona anche quando il suo hook e'
// stato rimosso o il processo e' in sofferenza.
//
// Alla pressione di Win+D forziamo la minimizzazione della finestra di WinGabriel con
// SW_FORCEMINIMIZE (non richiede che il thread della UI risponda ai messaggi) e segnaliamo,
// con un evento di sistema condiviso, che la guardia del focus deve restare sospesa finche'
// l'utente non ripristina la finestra di persona (vedi GuardiaSospesaDalServizio /
// OnSizeChanged in WGContenitore.vb). Non serve replicare qui anche il "torna al desktop":
// se l'hook di WGTastiera non e' attivo, Win+D non viene intercettato da nessuno prima di
// Explorer, che quindi esegue normalmente la propria funzione nativa "mostra il desktop".
const wchar_t* NOME_EVENTO_GUARDIA_SOSPESA = L"WGGuardiaFocusSospesaDaServizio";
const int SW_FORCEMINIMIZE_LOCALE = 11; // SW_FORCEMINIMIZE

HANDLE ApriOCreaEventoGuardia() {
    HANDLE h = CreateEventW(NULL, TRUE, FALSE, NOME_EVENTO_GUARDIA_SOSPESA);
    if (h == NULL) {
        LogToFile("Impossibile creare/aprire l'evento di sospensione guardia");
    }
    return h;
}

HWND TrovaFinestraWinGabriel() {
    // Il form principale imposta esplicitamente Me.Text = "WinGabriel" (vedi WGContenitore.vb,
    // Sub New). Se in futuro il titolo cambiasse, questa ricerca andra' aggiornata di
    // conseguenza (es. enumerando le finestre del processo wingabriel.exe).
    return FindWindowW(NULL, L"WinGabriel");
}

DWORD WINAPI WinDFallbackThread(LPVOID lpParam) {
    bool comboGiaGestita = false;
    HANDLE hEvento = ApriOCreaEventoGuardia();

    while (!exitRequested) {
        Sleep(15);
        if (exitRequested) break;
        try {
            bool winPremuto = ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) || ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0);
            bool dPremuto = (GetAsyncKeyState('D') & 0x8000) != 0;

            if (winPremuto && dPremuto) {
                if (!comboGiaGestita) {
                    comboGiaGestita = true;
                    LogToFile("Win+D rilevato dal servizio (fallback di sicurezza)");

                    HWND hwndGabriel = TrovaFinestraWinGabriel();
                    if (hwndGabriel != NULL) {
                        ShowWindow(hwndGabriel, SW_FORCEMINIMIZE_LOCALE);
                    } else {
                        LogToFile("Fallback Win+D: finestra WinGabriel non trovata");
                    }

                    if (hEvento != NULL) {
                        SetEvent(hEvento);
                    }
                }
            } else {
                comboGiaGestita = false;
            }
        } catch (...) {
            LogToFile("Eccezione nel thread WinDFallbackThread");
        }
    }

    if (hEvento != NULL) CloseHandle(hEvento);
    return 0;
}

DWORD WINAPI HotkeyThread(LPVOID lpParam) {
    try {
        RegisterHotKey(NULL, 1, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x57);
        MSG msg = {0};
        while (GetMessage(&msg, NULL, 0, 0) != 0) {
            if (msg.message == WM_HOTKEY) {
                KillProcessByName(L"wingabriel.exe");
                KillProcessByName(L"WGSapi.exe");
                KillProcessByName(L"WGPico.exe");
            }
        }
    } catch (...) {
        LogToFile("Eccezione in HotkeyThread");
    }
    return 0;
}

void WINAPI WlanNotificationCallback(PWLAN_NOTIFICATION_DATA arg1, PVOID arg2) {
    if (arg1 != NULL && arg1->NotificationSource == WLAN_NOTIFICATION_SOURCE_ACM) {
        if (arg1->NotificationCode == wlan_notification_acm_scan_complete ||
            arg1->NotificationCode == wlan_notification_acm_scan_fail) {
            SetEvent((HANDLE)arg2);
        }
    }
}

std::string GetWiFiNetworks() {
    std::string result = "OK|";
    HANDLE hClient = NULL;
    PWLAN_INTERFACE_INFO_LIST pIfList = NULL;
    HANDLE hScanEvent = CreateEvent(NULL, FALSE, FALSE, NULL);
    
    try {
        DWORD dwMaxClient = 2;
        DWORD dwCurVersion = 0;
        DWORD dwResult = 0;
        
        dwResult = WlanOpenHandle(dwMaxClient, NULL, &dwCurVersion, &hClient);
        if (dwResult != ERROR_SUCCESS) {
            if (hScanEvent) CloseHandle(hScanEvent);
            LogToFile("Errore WlanOpenHandle");
            return "ERROR|WIFI_DISATTIVATO";
        }

        dwResult = WlanEnumInterfaces(hClient, NULL, &pIfList);
        if (dwResult != ERROR_SUCCESS || pIfList == NULL || pIfList->dwNumberOfItems == 0) {
            if (pIfList != NULL) {
                WlanFreeMemory(pIfList);
                pIfList = NULL;
            }
            if (hClient != NULL) {
                WlanCloseHandle(hClient, NULL);
                hClient = NULL;
            }
            if (hScanEvent) CloseHandle(hScanEvent);
            return "ERROR|WIFI_DISATTIVATO";
        }

        WlanRegisterNotification(hClient, WLAN_NOTIFICATION_SOURCE_ACM, TRUE, WlanNotificationCallback, hScanEvent, NULL, NULL);

        int networkId = 1;
        std::vector<std::string> foundSSIDs;

        for (int i = 0; i < (int)pIfList->dwNumberOfItems; i++) {
            PWLAN_INTERFACE_INFO pIfInfo = (PWLAN_INTERFACE_INFO)&pIfList->InterfaceInfo[i];
            
            ResetEvent(hScanEvent);
            dwResult = WlanScan(hClient, &pIfInfo->InterfaceGuid, NULL, NULL, NULL);
            
            if (dwResult == ERROR_SUCCESS) {
                WaitForSingleObject(hScanEvent, 8000);
                Sleep(250);
            } else {
                Sleep(1000);
            }
            
            PWLAN_AVAILABLE_NETWORK_LIST pBssList = NULL;
            dwResult = WlanGetAvailableNetworkList(hClient, &pIfInfo->InterfaceGuid, 0, NULL, &pBssList);
            
            if (dwResult == ERROR_SUCCESS && pBssList != NULL) {
                for (int j = 0; j < (int)pBssList->dwNumberOfItems; j++) {
                    PWLAN_AVAILABLE_NETWORK pNetwork = (PWLAN_AVAILABLE_NETWORK)&pBssList->Network[j];
                    std::string ssid = "";
                    for (int k = 0; k < (int)pNetwork->dot11Ssid.uSSIDLength; k++) {
                        ssid += (char)pNetwork->dot11Ssid.ucSSID[k];
                    }
                    
                    bool alreadyAdded = false;
                    for (size_t x = 0; x < foundSSIDs.size(); x++) {
                        if (foundSSIDs[x] == ssid) {
                            alreadyAdded = true;
                            break;
                        }
                    }

                    if (!ssid.empty() && !alreadyAdded) {
                        foundSSIDs.push_back(ssid);
                        std::string connected = (pNetwork->dwFlags & WLAN_AVAILABLE_NETWORK_CONNECTED) ? "1" : "0";
                        std::string secure = pNetwork->bSecurityEnabled ? "1" : "0";
                        std::string signal = std::to_string(pNetwork->wlanSignalQuality);
                        
                        result += std::to_string(networkId) + "|" + ssid + "|" + connected + "|" + secure + "|" + signal + ";";
                        networkId++;
                    }
                }
                WlanFreeMemory(pBssList);
            }
        }
        
        WlanRegisterNotification(hClient, WLAN_NOTIFICATION_SOURCE_NONE, TRUE, NULL, NULL, NULL, NULL);
        
    } catch (...) {
        LogToFile("Eccezione in GetWiFiNetworks");
        result = "ERROR|Eccezione WiFi";
    }
    
    if (pIfList != NULL) {
        WlanFreeMemory(pIfList);
    }
    if (hClient != NULL) {
        WlanCloseHandle(hClient, NULL);
    }
    if (hScanEvent != NULL) {
        CloseHandle(hScanEvent);
    }
    
    if (result == "OK|") {
        return "ERROR|WIFI_DISATTIVATO";
    }
    
    return result;
}

std::string GetBluetoothDevices() {
    std::string result = "OK|";
    HANDLE hRadio = NULL;
    HBLUETOOTH_RADIO_FIND hFindRadio = NULL;
    HBLUETOOTH_DEVICE_FIND hFind = NULL;
    
    try {
        BLUETOOTH_FIND_RADIO_PARAMS radioParams = { sizeof(BLUETOOTH_FIND_RADIO_PARAMS) };
        hFindRadio = BluetoothFindFirstRadio(&radioParams, &hRadio);
        if (hFindRadio == NULL) {
            return "ERROR|BLUETOOTH_DISATTIVATO";
        }
        CloseHandle(hRadio);
        hRadio = NULL;
        BluetoothFindRadioClose(hFindRadio);
        hFindRadio = NULL;

        BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams = { sizeof(BLUETOOTH_DEVICE_SEARCH_PARAMS) };
        searchParams.fReturnAuthenticated = TRUE;
        searchParams.fReturnRemembered = TRUE;
        searchParams.fReturnUnknown = TRUE;
        searchParams.fReturnConnected = TRUE;
        searchParams.fIssueInquiry = TRUE;
        searchParams.cTimeoutMultiplier = 2;

        BLUETOOTH_DEVICE_INFO deviceInfo = { sizeof(BLUETOOTH_DEVICE_INFO) };
        hFind = BluetoothFindFirstDevice(&searchParams, &deviceInfo);

        if (hFind != NULL) {
            do {
                int size_needed = WideCharToMultiByte(CP_UTF8, 0, deviceInfo.szName, -1, NULL, 0, NULL, NULL);
                if (size_needed > 0) {
                    std::vector<char> buffer(size_needed);
                    WideCharToMultiByte(CP_UTF8, 0, deviceInfo.szName, -1, &buffer[0], size_needed, NULL, NULL);
                    std::string name(&buffer[0]);
                    if (!name.empty() && result.find(name + "|") == std::string::npos) {
                        std::string connected = deviceInfo.fConnected ? "1" : "0";
                        result += name + "|" + connected + ";";
                    }
                }
            } while (BluetoothFindNextDevice(hFind, &deviceInfo));
            BluetoothFindDeviceClose(hFind);
            hFind = NULL;
        } else {
            LogToFile("Nessun dispositivo Bluetooth trovato o errore BluetoothFindFirstDevice");
            result = "ERROR|Nessun dispositivo Bluetooth trovato";
        }
    } catch (...) {
        LogToFile("Eccezione in GetBluetoothDevices");
        if (hRadio != NULL) CloseHandle(hRadio);
        if (hFindRadio != NULL) BluetoothFindRadioClose(hFindRadio);
        if (hFind != NULL) BluetoothFindDeviceClose(hFind);
        return "ERROR|Eccezione Bluetooth";
    }
    return result;
}

bool LinguaItalianaDisponibile() {
    try {
        auto lingue = winrt::Windows::Media::SpeechRecognition::SpeechRecognizer::SupportedGrammarLanguages();
        for (auto const& lingua : lingue) {
            std::wstring etichetta = lingua.LanguageTag().c_str();
            if (etichetta.size() >= 2 && _wcsnicmp(etichetta.c_str(), L"it", 2) == 0) {
                return true;
            }
        }
    } catch (winrt::hresult_error const& errore) {
        LogToFile("Errore LinguaItalianaDisponibile: " + winrt::to_string(errore.message()));
    } catch (...) {
        LogToFile("Eccezione in LinguaItalianaDisponibile");
    }
    return false;
}

void OnRisultatoRiconoscimento(winrt::Windows::Media::SpeechRecognition::SpeechContinuousRecognitionSession const& sessione,
                               winrt::Windows::Media::SpeechRecognition::SpeechContinuousRecognitionResultGeneratedEventArgs const& argomenti) {
    try {
        using namespace winrt::Windows::Media::SpeechRecognition;
        if (!riconoscimentoRichiesto) return;

        SpeechRecognitionResult risultato = argomenti.Result();
        if (risultato == nullptr) return;
        if (risultato.Status() != SpeechRecognitionResultStatus::Success) return;
        if (risultato.Text().empty()) return;

        SpeechRecognitionConfidence confidenza = risultato.Confidence();
        bool accettato = (confidenza == SpeechRecognitionConfidence::High) ||
                         (confidenzaMediaAmmessa && confidenza == SpeechRecognitionConfidence::Medium);
        if (!accettato) return;

        unsigned long long adesso = GetTickCount64();
        if (adesso - ultimaAttivazione.load() < 1500) return;
        ultimaAttivazione = adesso;

        if (eventoAttivazione != NULL) {
            SetEvent(eventoAttivazione);
        }

        riavvioRiconoscimento = true;
        if (eventoControlloRiconoscimento != NULL) SetEvent(eventoControlloRiconoscimento);
    } catch (winrt::hresult_error const& errore) {
        LogToFile("Errore OnRisultatoRiconoscimento: " + winrt::to_string(errore.message()));
    } catch (...) {
        LogToFile("Eccezione in OnRisultatoRiconoscimento");
    }
}

void OnFineSessioneRiconoscimento(winrt::Windows::Media::SpeechRecognition::SpeechContinuousRecognitionSession const& sessione,
                                  winrt::Windows::Media::SpeechRecognition::SpeechContinuousRecognitionCompletedEventArgs const& argomenti) {
    try {
        using namespace winrt::Windows::Media::SpeechRecognition;
        if (argomenti.Status() != SpeechRecognitionResultStatus::Success &&
            argomenti.Status() != SpeechRecognitionResultStatus::UserCanceled) {
            LogToFile("Sessione di riconoscimento vocale terminata con stato " + std::to_string(static_cast<int>(argomenti.Status())));
        }
        if (riconoscimentoRichiesto) {
            riavvioRiconoscimento = true;
            if (eventoControlloRiconoscimento != NULL) SetEvent(eventoControlloRiconoscimento);
        }
    } catch (...) {
        LogToFile("Eccezione in OnFineSessioneRiconoscimento");
    }
}

bool AvviaRiconoscimento(winrt::Windows::Media::SpeechRecognition::SpeechRecognizer& riconoscitore,
                         winrt::event_token& tokenRisultato, winrt::event_token& tokenFine) {
    try {
        using namespace winrt::Windows::Media::SpeechRecognition;

        riconoscitore = SpeechRecognizer(winrt::Windows::Globalization::Language(L"it-IT"));

        std::vector<winrt::hstring> frasi;
        frasi.push_back(L"WinGabriel");
        frasi.push_back(L"Win Gabriel");
        frasi.push_back(L"Ei WinGabriel");
        frasi.push_back(L"Ei Win Gabriel");
        frasi.push_back(L"Ehi WinGabriel");
        frasi.push_back(L"Ehi Win Gabriel");
        SpeechRecognitionListConstraint vincolo(winrt::single_threaded_vector<winrt::hstring>(std::move(frasi)), L"attivazione");
        riconoscitore.Constraints().Append(vincolo);

        SpeechRecognitionCompilationResult compilazione = riconoscitore.CompileConstraintsAsync().get();
        if (compilazione.Status() != SpeechRecognitionResultStatus::Success) {
            LogToFile("Compilazione della grammatica di attivazione non riuscita, stato " + std::to_string(static_cast<int>(compilazione.Status())));
            riconoscitore.Close();
            riconoscitore = nullptr;
            return false;
        }

        SpeechContinuousRecognitionSession sessione = riconoscitore.ContinuousRecognitionSession();
        sessione.AutoStopSilenceTimeout(std::chrono::hours(24));
        tokenRisultato = sessione.ResultGenerated(&OnRisultatoRiconoscimento);
        tokenFine = sessione.Completed(&OnFineSessioneRiconoscimento);
        sessione.StartAsync().get();
        return true;
    } catch (winrt::hresult_error const& errore) {
        LogToFile("Errore AvviaRiconoscimento: " + winrt::to_string(errore.message()));
    } catch (...) {
        LogToFile("Eccezione in AvviaRiconoscimento");
    }
    try {
        if (riconoscitore != nullptr) {
            riconoscitore.Close();
            riconoscitore = nullptr;
        }
    } catch (...) {
        LogToFile("Eccezione nella chiusura del riconoscitore dopo un errore");
    }
    return false;
}

void FermaRiconoscimento(winrt::Windows::Media::SpeechRecognition::SpeechRecognizer& riconoscitore,
                         winrt::event_token& tokenRisultato, winrt::event_token& tokenFine) {
    if (riconoscitore == nullptr) return;
    try {
        auto sessione = riconoscitore.ContinuousRecognitionSession();
        try {
            sessione.ResultGenerated(tokenRisultato);
            sessione.Completed(tokenFine);
        } catch (...) {
            LogToFile("Eccezione nella rimozione dei gestori del riconoscimento");
        }
        if (riconoscitore.State() != winrt::Windows::Media::SpeechRecognition::SpeechRecognizerState::Idle) {
            sessione.CancelAsync().get();
        }
    } catch (winrt::hresult_error const& errore) {
        LogToFile("Errore FermaRiconoscimento: " + winrt::to_string(errore.message()));
    } catch (...) {
        LogToFile("Eccezione in FermaRiconoscimento");
    }
    try {
        riconoscitore.Close();
    } catch (...) {
        LogToFile("Eccezione nella chiusura del riconoscitore");
    }
    riconoscitore = nullptr;
}

DWORD WINAPI RiconoscimentoVocaleThread(LPVOID lpParam) {
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
    } catch (...) {
        LogToFile("Impossibile inizializzare Windows Runtime per il riconoscimento vocale");
        disponibilitaItaliano = 0;
        return 0;
    }

    disponibilitaItaliano = LinguaItalianaDisponibile() ? 1 : 0;

    winrt::Windows::Media::SpeechRecognition::SpeechRecognizer riconoscitore{ nullptr };
    winrt::event_token tokenRisultato{};
    winrt::event_token tokenFine{};
    bool attivo = false;
    int controlliInattivi = 0;

    while (!exitRequested) {
        try {
            WaitForSingleObject(eventoControlloRiconoscimento, 2000);
            if (exitRequested) break;

            bool richiesto = riconoscimentoRichiesto && disponibilitaItaliano == 1;

            if (richiesto && attivo && !riavvioRiconoscimento) {
                bool inattivo = false;
                try {
                    inattivo = (riconoscitore == nullptr) || (riconoscitore.State() == winrt::Windows::Media::SpeechRecognition::SpeechRecognizerState::Idle);
                } catch (winrt::hresult_error const& errore) {
                    LogToFile("Errore nella lettura dello stato del riconoscimento: " + winrt::to_string(errore.message()));
                    inattivo = true;
                } catch (...) {
                    LogToFile("Eccezione nella lettura dello stato del riconoscimento");
                    inattivo = true;
                }
                controlliInattivi = inattivo ? controlliInattivi + 1 : 0;
                if (controlliInattivi >= 2) {
                    controlliInattivi = 0;
                    riavvioRiconoscimento = true;
                }
            } else {
                controlliInattivi = 0;
            }

            if (richiesto && attivo && riavvioRiconoscimento) {
                riavvioRiconoscimento = false;
                FermaRiconoscimento(riconoscitore, tokenRisultato, tokenFine);
                attivo = false;
            }

            if (richiesto && !attivo) {
                attivo = AvviaRiconoscimento(riconoscitore, tokenRisultato, tokenFine);
                statoRiconoscimento = attivo ? 1 : -1;
                if (!attivo) Sleep(3000);
            } else if (!richiesto && attivo) {
                FermaRiconoscimento(riconoscitore, tokenRisultato, tokenFine);
                attivo = false;
                statoRiconoscimento = 0;
            } else if (richiesto && attivo) {
                statoRiconoscimento = 1;
            } else if (!richiesto) {
                statoRiconoscimento = 0;
            }
        } catch (...) {
            LogToFile("Eccezione nel thread RiconoscimentoVocaleThread");
            Sleep(2000);
        }
    }

    if (attivo) FermaRiconoscimento(riconoscitore, tokenRisultato, tokenFine);
    try {
        winrt::uninit_apartment();
    } catch (...) {
        LogToFile("Eccezione nella chiusura di Windows Runtime");
    }
    return 0;
}

int AttendiDisponibilitaItaliano(int millisecondi) {
    int trascorsi = 0;
    while (disponibilitaItaliano == -1 && trascorsi < millisecondi) {
        Sleep(100);
        trascorsi += 100;
    }
    return disponibilitaItaliano;
}

int AttendiStatoRiconoscimento(int millisecondi) {
    int trascorsi = 0;
    while (statoRiconoscimento == 0 && trascorsi < millisecondi) {
        Sleep(100);
        trascorsi += 100;
    }
    return statoRiconoscimento;
}

void ProcessCommand(const std::string& command, std::string& response) {
    try {
        if (command == "EXIT") {
            exitRequested = true;
            response = "OK|Chiusura in corso";
        } else if (command == "SPEECH_CHECK") {
            int disponibile = AttendiDisponibilitaItaliano(8000);
            if (disponibile == 1) response = "OK|1";
            else if (disponibile == 0) response = "OK|0";
            else response = "ERROR|Verifica non completata";
        } else if (command == "WAKE_ON" || command == "WAKE_ON|MEDIA") {
            confidenzaMediaAmmessa = (command == "WAKE_ON|MEDIA");
            if (AttendiDisponibilitaItaliano(8000) != 1) {
                response = "ERROR|ITALIANO_NON_DISPONIBILE";
            } else {
                statoRiconoscimento = 0;
                riconoscimentoRichiesto = true;
                if (eventoControlloRiconoscimento != NULL) SetEvent(eventoControlloRiconoscimento);
                int stato = AttendiStatoRiconoscimento(10000);
                if (stato == 1) response = "OK|1";
                else response = "ERROR|Avvio del riconoscimento non riuscito";
            }
        } else if (command == "WAKE_OFF") {
            riconoscimentoRichiesto = false;
            if (eventoControlloRiconoscimento != NULL) SetEvent(eventoControlloRiconoscimento);
            response = "OK|0";
        } else if (command == "GET_WIFI") {
            response = GetWiFiNetworks();
        } else if (command.rfind("SET_WIFI|", 0) == 0) {
            size_t firstPipe = command.find('|');
            size_t secondPipe = command.find('|', firstPipe + 1);
            if (firstPipe != std::string::npos && secondPipe != std::string::npos) {
                std::string ssid = command.substr(firstPipe + 1, secondPipe - firstPipe - 1);
                std::string password = command.substr(secondPipe + 1);
                
                std::string profileXml = "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name>" + ssid + "</name><SSIDConfig><SSID><name>" + ssid + "</name></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>" + password + "</keyMaterial></sharedKey></security></MSM></WLANProfile>";
                std::string tempPath = baseDir + "\\temp_profile.xml";
                std::ofstream out(tempPath);
                if (out.is_open()) {
                    out << profileXml;
                    out.close();
                    
                    std::string cmdAdd = "netsh wlan add profile filename=\"" + tempPath + "\"";
                    RunCommandHidden(cmdAdd);
                    
                    std::string cmdConn = "netsh wlan connect name=\"" + ssid + "\"";
                    RunCommandHidden(cmdConn);
                    
                    DeleteFileA(tempPath.c_str());
                    
                    bool connected = false;
                    for(int i=0; i<8; i++) {
                        Sleep(1000);
                        std::string currentNetworks = GetWiFiNetworks();
                        if (currentNetworks.find(ssid + "|1") != std::string::npos) {
                            connected = true;
                            break;
                        }
                    }
                    
                    if (connected) {
                        response = "OK|Connessione avvenuta";
                    } else {
                        RunCommandHidden("netsh wlan delete profile name=\"" + ssid + "\"");
                        response = "ERROR|Connessione fallita";
                    }
                } else {
                    LogToFile("Impossibile creare il file XML temporaneo per WiFi");
                    response = "ERROR|File XML fallito";
                }
            } else {
                LogToFile("Formato comando SET_WIFI non valido: " + command);
                response = "ERROR|Parametri mancanti";
            }
        } else if (command.rfind("FORGET_WIFI|", 0) == 0) {
            std::string ssid = command.substr(12);
            std::string cmdDel = "netsh wlan delete profile name=\"" + ssid + "\"";
            RunCommandHidden(cmdDel);
            response = "OK|Rete eliminata";
        } else if (command == "WIFI_ON") {
            RunCommandHidden("netsh interface set interface name=\"Wi-Fi\" admin=enable");
            response = "OK|Wi-Fi attivato";
        } else if (command == "WIFI_OFF") {
            RunCommandHidden("netsh interface set interface name=\"Wi-Fi\" admin=disable");
            response = "OK|Wi-Fi disattivato";
        } else if (command == "GET_BLUETOOTH") {
            response = GetBluetoothDevices();
        } else if (command.rfind("SET_BT|", 0) == 0) {
            response = "ERROR|Connessione Bluetooth non supportata direttamente";
        } else if (command.rfind("FORGET_BT|", 0) == 0) {
            std::string targetName = command.substr(10);
            BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams = { sizeof(BLUETOOTH_DEVICE_SEARCH_PARAMS) };
            searchParams.fReturnAuthenticated = TRUE;
            searchParams.fReturnRemembered = TRUE;
            searchParams.fReturnUnknown = TRUE;
            searchParams.fReturnConnected = TRUE;
            searchParams.fIssueInquiry = FALSE;
            searchParams.cTimeoutMultiplier = 0;

            BLUETOOTH_DEVICE_INFO deviceInfo = { sizeof(BLUETOOTH_DEVICE_INFO) };
            HBLUETOOTH_DEVICE_FIND hFind = BluetoothFindFirstDevice(&searchParams, &deviceInfo);
            bool removed = false;
            if (hFind != NULL) {
                do {
                    int size_needed = WideCharToMultiByte(CP_UTF8, 0, deviceInfo.szName, -1, NULL, 0, NULL, NULL);
                    if (size_needed > 0) {
                        std::vector<char> buffer(size_needed);
                        WideCharToMultiByte(CP_UTF8, 0, deviceInfo.szName, -1, &buffer[0], size_needed, NULL, NULL);
                        std::string name(&buffer[0]);
                        if (name == targetName) {
                            if (BluetoothRemoveDevice(&deviceInfo.Address) == ERROR_SUCCESS) {
                                removed = true;
                            }
                            break;
                        }
                    }
                } while (BluetoothFindNextDevice(hFind, &deviceInfo));
                BluetoothFindDeviceClose(hFind);
            }
            if (removed) response = "OK|Dispositivo eliminato";
            else response = "ERROR|Impossibile eliminare";
        } else if (command == "BT_ON") {
            RunCommandHidden("powershell.exe -WindowStyle Hidden -Command \"Start-Service bthserv\"");
            response = "OK|Bluetooth attivato";
        } else if (command == "BT_OFF") {
            RunCommandHidden("powershell.exe -WindowStyle Hidden -Command \"Stop-Service bthserv -Force\"");
            response = "OK|Bluetooth disattivato";
        } else if (command == "GET_BRIGHTNESS") {
            response = "OK|50";
        } else if (command.rfind("SET_BRIGHTNESS|", 0) == 0) {
            response = "OK|Luminosita impostata";
        } else {
            response = "ERROR|Comando sconosciuto";
            LogToFile("Comando sconosciuto: " + command);
        }
    } catch (...) {
        LogToFile("Eccezione in ProcessCommand. Comando: " + command);
        response = "ERROR|Eccezione ProcessCommand";
    }
}

DWORD WINAPI PipeClientHandler(LPVOID lpParam) {
    HANDLE hPipe = (HANDLE)lpParam;
    try {
        char buffer[512];
        DWORD bytesRead;
        if (ReadFile(hPipe, buffer, sizeof(buffer) - 1, &bytesRead, NULL)) {
            buffer[bytesRead] = '\0';
            std::string command(buffer);
            
            size_t pos = command.find('\r');
            if (pos != std::string::npos) command.erase(pos);
            pos = command.find('\n');
            if (pos != std::string::npos) command.erase(pos);

            std::string response;
            ProcessCommand(command, response);
            response += "\r\n";

            DWORD bytesWritten;
            WriteFile(hPipe, response.c_str(), response.length(), &bytesWritten, NULL);
        } else {
            LogToFile("Errore in ReadFile per la pipe");
        }
    } catch (...) {
        LogToFile("Eccezione in PipeClientHandler");
    }
    FlushFileBuffers(hPipe);
    DisconnectNamedPipe(hPipe);
    CloseHandle(hPipe);
    return 0;
}

DWORD WINAPI PipeServerThread(LPVOID lpParam) {
    while (!exitRequested) {
        try {
            HANDLE hPipe = CreateNamedPipeA(
                "\\\\.\\pipe\\WGServicePipe",
                PIPE_ACCESS_DUPLEX,
                PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
                PIPE_UNLIMITED_INSTANCES,
                512, 512, 0, NULL);

            if (hPipe == INVALID_HANDLE_VALUE) {
                LogToFile("Impossibile creare la pipe");
                Sleep(2000);
                continue;
            }

            if (ConnectNamedPipe(hPipe, NULL) != FALSE) {
                HANDLE hThread = CreateThread(NULL, 0, PipeClientHandler, (LPVOID)hPipe, 0, NULL);
                if (hThread != NULL) {
                    CloseHandle(hThread);
                } else {
                    CloseHandle(hPipe);
                }
            } else {
                CloseHandle(hPipe);
            }
        } catch (...) {
            LogToFile("Eccezione in PipeServerThread");
            Sleep(2000);
        }
    }
    return 0;
}

int APIENTRY WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    try {
        char path[MAX_PATH];
        GetModuleFileNameA(NULL, path, MAX_PATH);
        std::string fullPath(path);
        baseDir = fullPath.substr(0, fullPath.find_last_of("\\/"));

        CreateDirectoryA((baseDir + "\\dati").c_str(), NULL);
        CreateDirectoryA((baseDir + "\\dati\\logs").c_str(), NULL);

        eventoControlloRiconoscimento = CreateEventW(NULL, FALSE, FALSE, NULL);
        eventoAttivazione = CreateEventW(NULL, FALSE, FALSE, NOME_EVENTO_ATTIVAZIONE);
        if (eventoControlloRiconoscimento == NULL || eventoAttivazione == NULL) {
            LogToFile("Impossibile creare gli eventi del riconoscimento vocale");
        }
        HANDLE hRiconoscimentoThread = CreateThread(NULL, 0, RiconoscimentoVocaleThread, NULL, 0, NULL);
        if (hRiconoscimentoThread == NULL) {
            LogToFile("Impossibile creare il thread del riconoscimento vocale");
        }

        HANDLE hMonitorThread = CreateThread(NULL, 0, MonitorProcess, NULL, 0, NULL);
        HANDLE hPipeThread = CreateThread(NULL, 0, PipeServerThread, NULL, 0, NULL);
        HANDLE hHotkeyThread = CreateThread(NULL, 0, HotkeyThread, NULL, 0, NULL);
        HANDLE hWinDFallbackThread = CreateThread(NULL, 0, WinDFallbackThread, NULL, 0, NULL);

        if (hMonitorThread == NULL || hPipeThread == NULL || hHotkeyThread == NULL || hWinDFallbackThread == NULL) {
            LogToFile("Impossibile creare i thread principali");
        }

        WaitForSingleObject(hPipeThread, INFINITE);
    } catch (...) {
        LogToFile("Eccezione fatale in WinMain");
    }
    return 0;
}