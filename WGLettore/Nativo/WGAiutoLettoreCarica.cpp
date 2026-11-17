#include <windows.h>
#include <shellapi.h>
#include <cwchar>
#include <cstdio>

namespace
{
    void ScriviErrore(const char* origine, DWORD codice)
    {
        char riga[256] = {};
        if (sprintf_s(riga, "WGAiutoLettoreCarica %s: codice %lu\n", origine, codice) > 0)
        {
            OutputDebugStringA(riga);
        }
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    int numero = 0;
    LPWSTR* argomenti = CommandLineToArgvW(GetCommandLineW(), &numero);
    if (argomenti == nullptr)
    {
        ScriviErrore("CommandLineToArgvW", GetLastError());
        return 1;
    }
    if (numero < 5)
    {
        LocalFree(argomenti);
        ScriviErrore("argomenti", static_cast<DWORD>(numero));
        return 1;
    }
    DWORD thread = wcstoul(argomenti[1], nullptr, 10);
    DWORD pidOspite = wcstoul(argomenti[2], nullptr, 10);
    DWORD pidBersaglio = wcstoul(argomenti[3], nullptr, 10);
    HMODULE modulo = LoadLibraryW(argomenti[4]);
    LocalFree(argomenti);
    if (modulo == nullptr)
    {
        ScriviErrore("LoadLibrary", GetLastError());
        return 2;
    }
    HOOKPROC procedura = reinterpret_cast<HOOKPROC>(GetProcAddress(modulo, "ProceduraMessaggi"));
    if (procedura == nullptr)
    {
        ScriviErrore("GetProcAddress", GetLastError());
        return 3;
    }
    HHOOK aggancio = SetWindowsHookExW(WH_GETMESSAGE, procedura, modulo, thread);
    if (aggancio == nullptr)
    {
        ScriviErrore("SetWindowsHookEx", GetLastError());
        return 4;
    }
    PostThreadMessageW(thread, WM_NULL, 0, 0);
    HANDLE attese[2] = {};
    DWORD quante = 0;
    HANDLE ospite = OpenProcess(SYNCHRONIZE, FALSE, pidOspite);
    if (ospite != nullptr)
    {
        attese[quante++] = ospite;
    }
    HANDLE bersaglio = OpenProcess(SYNCHRONIZE, FALSE, pidBersaglio);
    if (bersaglio != nullptr)
    {
        attese[quante++] = bersaglio;
    }
    if (quante == 0)
    {
        ScriviErrore("OpenProcess", GetLastError());
    }
    else if (WaitForMultipleObjects(quante, attese, FALSE, INFINITE) == WAIT_FAILED)
    {
        ScriviErrore("WaitForMultipleObjects", GetLastError());
    }
    UnhookWindowsHookEx(aggancio);
    for (DWORD indice = 0; indice < quante; ++indice)
    {
        CloseHandle(attese[indice]);
    }
    return 0;
}
