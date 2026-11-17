#include <winsock2.h>
#include <ws2tcpip.h>
#include <string>

#pragma comment(lib, "ws2_32.lib")

SOCKET sock = INVALID_SOCKET;
bool wsaAvviato = false;

extern "C" __declspec(dllexport) void Inizializza() {
    if (sock != INVALID_SOCKET) return;
    if (!wsaAvviato) {
        WSADATA wsaData;
        if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) return;
        wsaAvviato = true;
    }
    sock = socket(AF_INET, SOCK_STREAM, IPPROTO_TCP);
    if (sock == INVALID_SOCKET) return;
    sockaddr_in server = {};
    server.sin_family = AF_INET;
    server.sin_port = htons(12345);
    inet_pton(AF_INET, "127.0.0.1", &server.sin_addr);
    if (connect(sock, (SOCKADDR*)&server, sizeof(server)) != 0) {
        closesocket(sock);
        sock = INVALID_SOCKET;
    }
}

extern "C" __declspec(dllexport) void Invia(const wchar_t* prefisso, const wchar_t* testo) {
    if (sock == INVALID_SOCKET || prefisso == nullptr || testo == nullptr) return;
    std::wstring ws(prefisso);
    ws += L":";
    ws += testo;
    ws += L"\n";
    if (ws.empty()) return;
    int size = WideCharToMultiByte(CP_UTF8, 0, ws.c_str(), (int)ws.size(), NULL, 0, NULL, NULL);
    if (size <= 0) return;
    std::string utf8_str(static_cast<size_t>(size), '\0');
    if (WideCharToMultiByte(CP_UTF8, 0, ws.c_str(), (int)ws.size(), &utf8_str[0], size, NULL, NULL) <= 0) return;
    int inviato = 0;
    int totale = (int)utf8_str.length();
    const char* dati = utf8_str.c_str();
    while (inviato < totale) {
        int n = send(sock, dati + inviato, totale - inviato, 0);
        // Se il collegamento cade, il socket va chiuso: altrimenti resta valido e
        // Inizializza non riprova, quindi JAWS non riceve più né voce né braille.
        if (n <= 0) {
            closesocket(sock);
            sock = INVALID_SOCKET;
            return;
        }
        inviato += n;
    }
}

extern "C" __declspec(dllexport) void Chiudi() {
    if (sock != INVALID_SOCKET) {
        closesocket(sock);
        sock = INVALID_SOCKET;
    }
    if (wsaAvviato) {
        WSACleanup();
        wsaAvviato = false;
    }
}