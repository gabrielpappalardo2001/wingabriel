#include <windows.h>
#include <oleacc.h>
#include <servprov.h>
#include <oleauto.h>
#include <string>
#include <vector>
#include <cwchar>
#include <cstdio>
#include <cstddef>
#include <new>

typedef long AccessibleStates;

struct IA2Locale
{
    BSTR language;
    BSTR country;
    BSTR variant;
};

MIDL_INTERFACE("E89F726E-C4F4-4c19-BB19-B647D7FA8478")
IAccessible2 : public IAccessible
{
public:
    virtual HRESULT STDMETHODCALLTYPE get_nRelations(long* numero) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_relation(long indice, IUnknown** relazione) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_relations(long massimo, IUnknown** relazioni, long* numero) = 0;
    virtual HRESULT STDMETHODCALLTYPE role(long* ruolo) = 0;
    virtual HRESULT STDMETHODCALLTYPE scrollTo(int tipo) = 0;
    virtual HRESULT STDMETHODCALLTYPE scrollToPoint(int tipo, long x, long y) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_groupPosition(long* livello, long* simili, long* posizione) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_states(AccessibleStates* stati) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_extendedRole(BSTR* ruolo) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_localizedExtendedRole(BSTR* ruolo) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_nExtendedStates(long* numero) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_extendedStates(long massimo, BSTR** stati, long* numero) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_localizedExtendedStates(long massimo, BSTR** stati, long* numero) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_uniqueID(long* identificativo) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_windowHandle(HWND* finestra) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_indexInParent(long* indice) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_locale(IA2Locale* lingua) = 0;
    virtual HRESULT STDMETHODCALLTYPE get_attributes(BSTR* attributi) = 0;
};

namespace
{
    const IID IID_Accessibile2 = { 0xE89F726E, 0xC4F4, 0x4c19, { 0xBB, 0x19, 0xB6, 0x47, 0xD7, 0xFA, 0x84, 0x78 } };
    const wchar_t* const NomeMessaggio = L"WGAiutoLettore.Esegui.1";
    const wchar_t* const PrefissoTubo = L"\\\\.\\pipe\\WGAiutoLettore_v1_";
    const DWORD MassimaRichiesta = 65536;
    const long MassimiFigli = 4000;
    const size_t MassimoNome = 3000;
    const size_t MassimiAttributi = 1000;
    const size_t MassimoValore = 1000;

    struct Lavoro
    {
        volatile LONG riferimenti;
        volatile LONG abbandonato;
        HWND finestra;
        DWORD thread;
        long idDocumento;
        long massimoNodi;
        DWORD tempoMassimo;
        HANDLE fatto;
        std::wstring esito;
    };

    struct VocePila
    {
        IAccessible* accessibile;
        int profondita;
    };

    HMODULE g_modulo = nullptr;
    UINT g_messaggio = 0;
    volatile LONG g_avviato = 0;
    CRITICAL_SECTION g_bloccoCoda;
        bool g_blocchiPronti = false;
    std::vector<Lavoro*>* g_coda = nullptr;
    thread_local bool t_inEsecuzione = false;

    void ScriviErrore(const char* origine, DWORD codice)
    {
        char riga[256] = {};
        if (sprintf_s(riga, "WGAiutoLettore pid %lu %s: codice %lu\n", GetCurrentProcessId(), origine, codice) > 0)
        {
            OutputDebugStringA(riga);
        }
    }

    void RilasciaLavoro(Lavoro* lavoro)
    {
        if (lavoro == nullptr)
        {
            return;
        }
        if (InterlockedDecrement(&lavoro->riferimenti) == 0)
        {
            if (lavoro->fatto != nullptr)
            {
                CloseHandle(lavoro->fatto);
            }
            delete lavoro;
        }
    }

    void Accoda(std::wstring& uscita, const wchar_t* testo, size_t massimo)
    {
        if (testo == nullptr)
        {
            return;
        }
        size_t contati = 0;
        for (const wchar_t* punto = testo; *punto != 0 && contati < massimo; ++punto, ++contati)
        {
            wchar_t carattere = *punto;
            if (carattere == L'\t' || carattere == L'\r' || carattere == L'\n')
            {
                carattere = L' ';
            }
            uscita.push_back(carattere);
        }
    }

    void AccodaNumero(std::wstring& uscita, long long numero)
    {
        wchar_t testo[32] = {};
        swprintf_s(testo, L"%lld", numero);
        uscita.append(testo);
    }

    IAccessible2* OttieniAccessibile2(IAccessible* accessibile)
    {
        if (accessibile == nullptr)
        {
            return nullptr;
        }
        IAccessible2* risultato = nullptr;
        IServiceProvider* servizi = nullptr;
        if (SUCCEEDED(accessibile->QueryInterface(IID_IServiceProvider, reinterpret_cast<void**>(&servizi))) && servizi != nullptr)
        {
            if (FAILED(servizi->QueryService(IID_IAccessible, IID_Accessibile2, reinterpret_cast<void**>(&risultato))))
            {
                risultato = nullptr;
            }
            servizi->Release();
        }
        if (risultato == nullptr)
        {
            if (FAILED(accessibile->QueryInterface(IID_Accessibile2, reinterpret_cast<void**>(&risultato))))
            {
                risultato = nullptr;
            }
        }
        return risultato;
    }

    long IdentificativoDi(IAccessible* accessibile)
    {
        long identificativo = 0;
        IAccessible2* secondo = OttieniAccessibile2(accessibile);
        if (secondo != nullptr)
        {
            if (FAILED(secondo->get_uniqueID(&identificativo)))
            {
                identificativo = 0;
            }
            secondo->Release();
        }
        return identificativo;
    }

    IAccessible* AccessibileDaVariante(VARIANT& valore)
    {
        IAccessible* risultato = nullptr;
        if (valore.vt == VT_DISPATCH && valore.pdispVal != nullptr)
        {
            if (FAILED(valore.pdispVal->QueryInterface(IID_IAccessible, reinterpret_cast<void**>(&risultato))))
            {
                risultato = nullptr;
            }
        }
        return risultato;
    }

    IAccessible* ProvaFiglio(IAccessible* radice, long identificativo)
    {
        if (radice == nullptr || identificativo == 0)
        {
            return nullptr;
        }
        VARIANT figlio;
        VariantInit(&figlio);
        figlio.vt = VT_I4;
        figlio.lVal = identificativo;
        IDispatch* oggetto = nullptr;
        HRESULT esito = radice->get_accChild(figlio, &oggetto);
        if (esito != S_OK || oggetto == nullptr)
        {
            if (oggetto != nullptr)
            {
                oggetto->Release();
            }
            return nullptr;
        }
        IAccessible* trovato = nullptr;
        if (FAILED(oggetto->QueryInterface(IID_IAccessible, reinterpret_cast<void**>(&trovato))))
        {
            trovato = nullptr;
        }
        oggetto->Release();
        if (trovato == nullptr)
        {
            return nullptr;
        }
        if (IdentificativoDi(trovato) != identificativo)
        {
            trovato->Release();
            return nullptr;
        }
        return trovato;
    }

    IAccessible* CercaPerIdentificativo(IAccessible* radice, long identificativo, long alternativo)
    {
        std::vector<IAccessible*> coda;
        radice->AddRef();
        coda.push_back(radice);
        size_t posizione = 0;
        ULONGLONG inizio = GetTickCount64();
        IAccessible* trovato = nullptr;
        std::vector<VARIANT> figli;
        while (posizione < coda.size() && trovato == nullptr)
        {
            if (coda.size() > 8000 || GetTickCount64() - inizio > 600)
            {
                break;
            }
            IAccessible* corrente = coda[posizione];
            ++posizione;
            long proprio = IdentificativoDi(corrente);
            if (proprio != 0 && (proprio == identificativo || proprio == alternativo))
            {
                corrente->AddRef();
                trovato = corrente;
                break;
            }
            long conteggio = 0;
            if (FAILED(corrente->get_accChildCount(&conteggio)) || conteggio <= 0)
            {
                continue;
            }
            if (conteggio > MassimiFigli)
            {
                conteggio = MassimiFigli;
            }
            figli.assign(static_cast<size_t>(conteggio), VARIANT{});
            for (long indice = 0; indice < conteggio; ++indice)
            {
                VariantInit(&figli[static_cast<size_t>(indice)]);
            }
            LONG ottenuti = 0;
            if (SUCCEEDED(AccessibleChildren(corrente, 0, conteggio, figli.data(), &ottenuti)))
            {
                for (LONG indice = 0; indice < ottenuti; ++indice)
                {
                    IAccessible* figlio = AccessibileDaVariante(figli[static_cast<size_t>(indice)]);
                    if (figlio != nullptr)
                    {
                        coda.push_back(figlio);
                    }
                }
            }
            for (long indice = 0; indice < conteggio; ++indice)
            {
                VariantClear(&figli[static_cast<size_t>(indice)]);
            }
        }
        for (IAccessible* elemento : coda)
        {
            elemento->Release();
        }
        return trovato;
    }

    long RuoloOStato(IAccessible* accessibile, bool stato)
    {
        VARIANT io;
        VariantInit(&io);
        io.vt = VT_I4;
        io.lVal = CHILDID_SELF;
        VARIANT valore;
        VariantInit(&valore);
        long risultato = 0;
        HRESULT esito = stato ? accessibile->get_accState(io, &valore) : accessibile->get_accRole(io, &valore);
        if (SUCCEEDED(esito) && valore.vt == VT_I4)
        {
            risultato = valore.lVal;
        }
        VariantClear(&valore);
        return risultato;
    }

    bool ServeValore(long ruolo, long statiSecondi)
    {
        switch (ruolo)
        {
        case ROLE_SYSTEM_TEXT:
        case ROLE_SYSTEM_COMBOBOX:
        case ROLE_SYSTEM_SLIDER:
        case ROLE_SYSTEM_SPINBUTTON:
        case ROLE_SYSTEM_PROGRESSBAR:
            return true;
        default:
            return (statiSecondi & 0x8) != 0;
        }
    }

    void Percorri(IAccessible* documento, Lavoro* lavoro, long& numero, bool& troncato, std::wstring& corpo)
    {
        std::vector<VocePila> pila;
        documento->AddRef();
        pila.push_back({ documento, 0 });
        ULONGLONG inizio = GetTickCount64();
        VARIANT io;
        VariantInit(&io);
        io.vt = VT_I4;
        io.lVal = CHILDID_SELF;
        std::vector<VARIANT> figli;
        while (!pila.empty())
        {
            VocePila voce = pila.back();
            pila.pop_back();
            if (troncato)
            {
                voce.accessibile->Release();
                continue;
            }
            if (numero >= lavoro->massimoNodi || GetTickCount64() - inizio > lavoro->tempoMassimo || lavoro->abbandonato != 0)
            {
                troncato = true;
                voce.accessibile->Release();
                continue;
            }
            IAccessible* accessibile = voce.accessibile;
            long ruolo = RuoloOStato(accessibile, false);
            long stato = RuoloOStato(accessibile, true);
            if (voce.profondita > 0 && (stato & STATE_SYSTEM_INVISIBLE) != 0)
            {
                accessibile->Release();
                continue;
            }
            long identificativo = 0;
            long ruoloSecondo = 0;
            long statiSecondi = 0;
            long livello = 0;
            long simili = 0;
            long posizione = 0;
            BSTR attributi = nullptr;
            IAccessible2* secondo = OttieniAccessibile2(accessibile);
            if (secondo != nullptr)
            {
                if (FAILED(secondo->get_uniqueID(&identificativo)))
                {
                    identificativo = 0;
                }
                if (FAILED(secondo->role(&ruoloSecondo)))
                {
                    ruoloSecondo = 0;
                }
                if (FAILED(secondo->get_states(&statiSecondi)))
                {
                    statiSecondi = 0;
                }
                if (FAILED(secondo->get_groupPosition(&livello, &simili, &posizione)))
                {
                    livello = 0;
                }
                if (FAILED(secondo->get_attributes(&attributi)))
                {
                    attributi = nullptr;
                }
                secondo->Release();
            }
            BSTR nome = nullptr;
            if (FAILED(accessibile->get_accName(io, &nome)))
            {
                nome = nullptr;
            }
            BSTR valore = nullptr;
            if (ServeValore(ruolo, statiSecondi))
            {
                if (FAILED(accessibile->get_accValue(io, &valore)))
                {
                    valore = nullptr;
                }
            }
            long conteggio = 0;
            if (FAILED(accessibile->get_accChildCount(&conteggio)))
            {
                conteggio = 0;
            }
            if (identificativo != 0)
            {
                AccodaNumero(corpo, identificativo);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, voce.profondita);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, ruolo);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, stato);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, ruoloSecondo);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, statiSecondi);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, livello);
                corpo.push_back(L'\t');
                AccodaNumero(corpo, conteggio);
                corpo.push_back(L'\t');
                Accoda(corpo, nome, MassimoNome);
                corpo.push_back(L'\t');
                Accoda(corpo, valore, MassimoValore);
                corpo.push_back(L'\t');
                Accoda(corpo, attributi, MassimiAttributi);
                corpo.push_back(L'\n');
                ++numero;
            }
            if (nome != nullptr)
            {
                SysFreeString(nome);
            }
            if (valore != nullptr)
            {
                SysFreeString(valore);
            }
            if (attributi != nullptr)
            {
                SysFreeString(attributi);
            }
            if (conteggio > 0)
            {
                if (conteggio > MassimiFigli)
                {
                    conteggio = MassimiFigli;
                }
                figli.assign(static_cast<size_t>(conteggio), VARIANT{});
                for (long indice = 0; indice < conteggio; ++indice)
                {
                    VariantInit(&figli[static_cast<size_t>(indice)]);
                }
                LONG ottenuti = 0;
                if (SUCCEEDED(AccessibleChildren(accessibile, 0, conteggio, figli.data(), &ottenuti)))
                {
                    for (LONG indice = ottenuti - 1; indice >= 0; --indice)
                    {
                        IAccessible* figlio = AccessibileDaVariante(figli[static_cast<size_t>(indice)]);
                        if (figlio != nullptr)
                        {
                            pila.push_back({ figlio, voce.profondita + 1 });
                        }
                    }
                }
                for (long indice = 0; indice < conteggio; ++indice)
                {
                    VariantClear(&figli[static_cast<size_t>(indice)]);
                }
            }
            accessibile->Release();
        }
    }

    void EseguiAlbero(Lavoro* lavoro)
    {
        IAccessible* radice = nullptr;
        HRESULT esito = AccessibleObjectFromWindow(lavoro->finestra, static_cast<DWORD>(OBJID_CLIENT), IID_IAccessible, reinterpret_cast<void**>(&radice));
        if (FAILED(esito) || radice == nullptr)
        {
            lavoro->esito = L"ERRORE\tradice";
            return;
        }
        long trovatoId = lavoro->idDocumento;
        IAccessible* documento = ProvaFiglio(radice, lavoro->idDocumento);
        if (documento == nullptr && lavoro->idDocumento != 0)
        {
            trovatoId = -lavoro->idDocumento;
            documento = ProvaFiglio(radice, trovatoId);
        }
        if (documento == nullptr)
        {
            documento = CercaPerIdentificativo(radice, lavoro->idDocumento, -lavoro->idDocumento);
            if (documento != nullptr)
            {
                trovatoId = IdentificativoDi(documento);
            }
        }
        radice->Release();
        if (documento == nullptr)
        {
            lavoro->esito = L"ERRORE\tdocumento";
            return;
        }
        std::wstring corpo;
        corpo.reserve(1 << 20);
        long numero = 0;
        bool troncato = false;
        Percorri(documento, lavoro, numero, troncato, corpo);
        documento->Release();
        std::wstring risposta = L"OK\t";
        AccodaNumero(risposta, trovatoId);
        risposta.push_back(L'\t');
        AccodaNumero(risposta, numero);
        risposta.push_back(L'\t');
        risposta.append(troncato ? L"1" : L"0");
        risposta.push_back(L'\n');
        risposta.append(corpo);
        lavoro->esito.swap(risposta);
    }

    void EseguiCoda()
    {
        if (t_inEsecuzione || g_coda == nullptr)
        {
            return;
        }
        t_inEsecuzione = true;
        std::vector<Lavoro*> miei;
        DWORD io = GetCurrentThreadId();
        EnterCriticalSection(&g_bloccoCoda);
        for (size_t indice = 0; indice < g_coda->size();)
        {
            if ((*g_coda)[indice]->thread == io)
            {
                miei.push_back((*g_coda)[indice]);
                g_coda->erase(g_coda->begin() + static_cast<std::ptrdiff_t>(indice));
            }
            else
            {
                ++indice;
            }
        }
        LeaveCriticalSection(&g_bloccoCoda);
        HRESULT com = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        for (Lavoro* lavoro : miei)
        {
            if (lavoro->abbandonato == 0)
            {
                try
                {
                    EseguiAlbero(lavoro);
                }
                catch (...)
                {
                    lavoro->esito = L"ERRORE\teccezione";
                    ScriviErrore("EseguiAlbero", 0);
                }
            }
            SetEvent(lavoro->fatto);
            RilasciaLavoro(lavoro);
        }
        if (com == S_OK || com == S_FALSE)
        {
            CoUninitialize();
        }
        t_inEsecuzione = false;
    }

    bool LeggiEsatti(HANDLE tubo, void* destinazione, DWORD quanti)
    {
        BYTE* punto = static_cast<BYTE*>(destinazione);
        DWORD letti = 0;
        while (letti < quanti)
        {
            DWORD questa = 0;
            if (!ReadFile(tubo, punto + letti, quanti - letti, &questa, nullptr) || questa == 0)
            {
                return false;
            }
            letti += questa;
        }
        return true;
    }

    bool ScriviEsatti(HANDLE tubo, const void* origine, DWORD quanti)
    {
        const BYTE* punto = static_cast<const BYTE*>(origine);
        DWORD scritti = 0;
        while (scritti < quanti)
        {
            DWORD questa = 0;
            if (!WriteFile(tubo, punto + scritti, quanti - scritti, &questa, nullptr) || questa == 0)
            {
                return false;
            }
            scritti += questa;
        }
        return true;
    }

    std::vector<std::wstring> Dividi(const std::wstring& testo)
    {
        std::vector<std::wstring> parti;
        size_t inizio = 0;
        for (;;)
        {
            size_t tab = testo.find(L'\t', inizio);
            if (tab == std::wstring::npos)
            {
                parti.push_back(testo.substr(inizio));
                break;
            }
            parti.push_back(testo.substr(inizio, tab - inizio));
            inizio = tab + 1;
        }
        return parti;
    }

    std::wstring RichiestaAlbero(const std::vector<std::wstring>& parti)
    {
        if (parti.size() < 5)
        {
            return L"ERRORE\tformato";
        }
        HWND finestra = reinterpret_cast<HWND>(static_cast<INT_PTR>(_wcstoi64(parti[1].c_str(), nullptr, 10)));
        long idDocumento = wcstol(parti[2].c_str(), nullptr, 10);
        long massimo = wcstol(parti[3].c_str(), nullptr, 10);
        long tempo = wcstol(parti[4].c_str(), nullptr, 10);
        if (finestra == nullptr || !IsWindow(finestra))
        {
            return L"ERRORE\tfinestra";
        }
        DWORD pid = 0;
        DWORD thread = GetWindowThreadProcessId(finestra, &pid);
        if (thread == 0 || pid != GetCurrentProcessId())
        {
            return L"ERRORE\tprocesso";
        }
        if (massimo <= 0 || massimo > 200000)
        {
            massimo = 60000;
        }
        if (tempo <= 0 || tempo > 10000)
        {
            tempo = 1500;
        }
        Lavoro* lavoro = new Lavoro();
        lavoro->riferimenti = 2;
        lavoro->abbandonato = 0;
        lavoro->finestra = finestra;
        lavoro->thread = thread;
        lavoro->idDocumento = idDocumento;
        lavoro->massimoNodi = massimo;
        lavoro->tempoMassimo = static_cast<DWORD>(tempo);
        lavoro->fatto = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (lavoro->fatto == nullptr)
        {
            ScriviErrore("CreateEvent", GetLastError());
            delete lavoro;
            return L"ERRORE\tevento";
        }
        EnterCriticalSection(&g_bloccoCoda);
        g_coda->push_back(lavoro);
        LeaveCriticalSection(&g_bloccoCoda);
        if (!PostMessageW(finestra, g_messaggio, 0, 0))
        {
            DWORD codice = GetLastError();
            bool tolto = false;
            EnterCriticalSection(&g_bloccoCoda);
            for (size_t indice = 0; indice < g_coda->size(); ++indice)
            {
                if ((*g_coda)[indice] == lavoro)
                {
                    g_coda->erase(g_coda->begin() + static_cast<std::ptrdiff_t>(indice));
                    tolto = true;
                    break;
                }
            }
            LeaveCriticalSection(&g_bloccoCoda);
            ScriviErrore("PostMessage", codice);
            if (tolto)
            {
                RilasciaLavoro(lavoro);
            }
            RilasciaLavoro(lavoro);
            return L"ERRORE\tmessaggio";
        }
        std::wstring risposta;
        DWORD attesa = WaitForSingleObject(lavoro->fatto, lavoro->tempoMassimo + 4000);
        if (attesa == WAIT_OBJECT_0)
        {
            risposta.swap(lavoro->esito);
        }
        else
        {
            InterlockedExchange(&lavoro->abbandonato, 1);
            risposta = L"ERRORE\tattesa";
        }
        RilasciaLavoro(lavoro);
        return risposta;
    }

    void GestisciCliente(HANDLE tubo)
    {
        DWORD lunghezza = 0;
        if (!LeggiEsatti(tubo, &lunghezza, sizeof(lunghezza)))
        {
            return;
        }
        if (lunghezza == 0 || lunghezza > MassimaRichiesta || (lunghezza % sizeof(wchar_t)) != 0)
        {
            return;
        }
        std::wstring richiesta(lunghezza / sizeof(wchar_t), L'\0');
        if (!LeggiEsatti(tubo, &richiesta[0], lunghezza))
        {
            return;
        }
        std::wstring risposta;
        try
        {
            std::vector<std::wstring> parti = Dividi(richiesta);
            if (parti.empty())
            {
                risposta = L"ERRORE\tvuota";
            }
            else if (parti[0] == L"PING")
            {
                risposta = L"OK\t1\t";
                AccodaNumero(risposta, GetCurrentProcessId());
            }
            else if (parti[0] == L"ALBERO")
            {
                risposta = RichiestaAlbero(parti);
            }
            else
            {
                risposta = L"ERRORE\tcomando";
            }
        }
        catch (...)
        {
            ScriviErrore("GestisciCliente", 0);
            risposta = L"ERRORE\teccezione";
        }
        unsigned long long byte = static_cast<unsigned long long>(risposta.size()) * sizeof(wchar_t);
        if (byte > 0x7FFFFFFFULL)
        {
            risposta = L"ERRORE\tdimensione";
            byte = static_cast<unsigned long long>(risposta.size()) * sizeof(wchar_t);
        }
        DWORD dimensione = static_cast<DWORD>(byte);
        if (!ScriviEsatti(tubo, &dimensione, sizeof(dimensione)))
        {
            return;
        }
        if (dimensione > 0)
        {
            ScriviEsatti(tubo, risposta.data(), dimensione);
        }
        FlushFileBuffers(tubo);
    }

    DWORD WINAPI CicloServer(LPVOID)
    {
        wchar_t nome[128] = {};
        swprintf_s(nome, L"%s%lu", PrefissoTubo, GetCurrentProcessId());
        bool primo = true;
        DWORD erroriDiFila = 0;
        for (;;)
        {
            DWORD opzioni = PIPE_ACCESS_DUPLEX;
            if (primo)
            {
                opzioni |= FILE_FLAG_FIRST_PIPE_INSTANCE;
            }
            HANDLE tubo = CreateNamedPipeW(nome, opzioni, PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                PIPE_UNLIMITED_INSTANCES, 1 << 16, 1 << 16, 0, nullptr);
            if (tubo == INVALID_HANDLE_VALUE)
            {
                DWORD codice = GetLastError();
                if (primo && codice == ERROR_ACCESS_DENIED)
                {
                    return 0;
                }
                ++erroriDiFila;
                ScriviErrore("CreateNamedPipe", codice);
                if (erroriDiFila > 20)
                {
                    return 0;
                }
                Sleep(2000);
                continue;
            }
            primo = false;
            erroriDiFila = 0;
            BOOL connesso = ConnectNamedPipe(tubo, nullptr) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
            if (connesso)
            {
                GestisciCliente(tubo);
            }
            DisconnectNamedPipe(tubo);
            CloseHandle(tubo);
        }
    }

    void AvviaSeServe()
    {
        if (InterlockedCompareExchange(&g_avviato, 1, 0) != 0)
        {
            return;
        }
        HMODULE fissato = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, reinterpret_cast<LPCWSTR>(&AvviaSeServe), &fissato))
        {
            ScriviErrore("GetModuleHandleEx", GetLastError());
            return;
        }
        g_messaggio = RegisterWindowMessageW(NomeMessaggio);
        if (g_messaggio == 0)
        {
            ScriviErrore("RegisterWindowMessage", GetLastError());
            return;
        }
        HANDLE thread = CreateThread(nullptr, 0, CicloServer, nullptr, 0, nullptr);
        if (thread == nullptr)
        {
            ScriviErrore("CreateThread", GetLastError());
            return;
        }
        CloseHandle(thread);
    }
}

extern "C" LRESULT CALLBACK ProceduraMessaggi(int codice, WPARAM wParam, LPARAM lParam)
{
    if (codice == HC_ACTION && g_blocchiPronti)
    {
        try
        {
            AvviaSeServe();
            if (wParam == PM_REMOVE && lParam != 0 && g_messaggio != 0)
            {
                const MSG* messaggio = reinterpret_cast<const MSG*>(lParam);
                if (messaggio->message == g_messaggio)
                {
                    EseguiCoda();
                }
            }
        }
        catch (...)
        {
            ScriviErrore("ProceduraMessaggi", 0);
        }
    }
    return CallNextHookEx(nullptr, codice, wParam, lParam);
}

BOOL APIENTRY DllMain(HMODULE modulo, DWORD motivo, LPVOID)
{
    if (motivo == DLL_PROCESS_ATTACH)
    {
        g_modulo = modulo;
        InitializeCriticalSection(&g_bloccoCoda);
        g_coda = new (std::nothrow) std::vector<Lavoro*>();
        g_blocchiPronti = g_coda != nullptr;
    }
    return TRUE;
}
