#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define FLUXFTP_VERSION "1.05"
#define FALLBACK_BUFFER_SIZE 4096

typedef struct LOADINFO {
    DWORD mVersion;
    HWND mHwnd;
    BOOL mKeep;
    BOOL mUnicode;
    DWORD mBeta;
    DWORD mBytes;
} LOADINFO;

static DWORD g_buffer_size = FALLBACK_BUFFER_SIZE;

static void copy_result(char *destination, const char *source)
{
    size_t capacity = g_buffer_size > 0 ? (size_t)g_buffer_size : FALLBACK_BUFFER_SIZE;
    if (!destination || capacity == 0) return;
    size_t length = strlen(source ? source : "");
    if (length >= capacity) length = capacity - 1;
    memcpy(destination, source ? source : "", length);
    destination[length] = '\0';
}

static void socket_error(char *output, const char *operation)
{
    char message[160];
    snprintf(message, sizeof(message), "ERROR %s failed (Winsock %d)", operation, WSAGetLastError());
    copy_result(output, message);
}

__declspec(dllexport) void __stdcall LoadDll(LOADINFO *info)
{
    if (!info) return;
    info->mKeep = FALSE;
    info->mUnicode = FALSE;
    /* mBytes does not exist before mIRC 7.64. Accept either word order; unknown versions use the legacy limit. */
    g_buffer_size = FALLBACK_BUFFER_SIZE;
    if ((HIWORD(info->mVersion) == 7 && LOWORD(info->mVersion) >= 64 && LOWORD(info->mVersion) <= 99) ||
        (LOWORD(info->mVersion) == 7 && HIWORD(info->mVersion) >= 64 && HIWORD(info->mVersion) <= 99)) {
        if (info->mBytes > 0 && info->mBytes < FALLBACK_BUFFER_SIZE)
            g_buffer_size = info->mBytes;
    }
}

__declspec(dllexport) int __stdcall UnloadDll(int timeout)
{
    (void)timeout;
    return 1;
}

/*
 * Input:  <IPv4-address> <port> <password> <raw|fxp|race|download arguments...>
 * Output: the FluxFTP UDP response, returned to $dll() with return code 3.
 */
__declspec(dllexport) int __stdcall FluxFTPCommand(
    HWND main_window, HWND active_window, char *data, char *parms, BOOL show, BOOL no_pause)
{
    char input[FALLBACK_BUFFER_SIZE];
    char *host, *port_text, *password, *command, *cursor;
    WSADATA winsock;
    SOCKET socket_handle = INVALID_SOCKET;
    struct sockaddr_in endpoint;
    DWORD timeout = 10000;
    int received;

    (void)main_window; (void)active_window; (void)parms; (void)show;
    if (!data) return 3;
    if (no_pause) {
        copy_result(data, "ERROR IRC client is in a critical routine; command not sent");
        return 3;
    }

    strncpy(input, data, sizeof(input) - 1);
    input[sizeof(input) - 1] = '\0';
    cursor = input;
    host = strtok(cursor, " ");
    port_text = strtok(NULL, " ");
    password = strtok(NULL, " ");
    command = strtok(NULL, "");
    if (!host || !port_text || !password || !command) {
        copy_result(data, "ERROR usage: <IPv4> <port> <password> <command>");
        return 3;
    }

    long port = strtol(port_text, NULL, 10);
    if (port < 1 || port > 65535) {
        copy_result(data, "ERROR invalid UDP port");
        return 3;
    }

    char payload[FALLBACK_BUFFER_SIZE];
    int payload_length = snprintf(payload, sizeof(payload), "%s %s", password, command);
    if (payload_length < 0 || payload_length >= (int)sizeof(payload)) {
        copy_result(data, "ERROR command is too long");
        return 3;
    }

    if (WSAStartup(MAKEWORD(2, 2), &winsock) != 0) {
        socket_error(data, "WSAStartup");
        return 3;
    }
    socket_handle = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (socket_handle == INVALID_SOCKET) {
        socket_error(data, "socket");
        WSACleanup();
        return 3;
    }
    setsockopt(socket_handle, SOL_SOCKET, SO_RCVTIMEO, (const char *)&timeout, sizeof(timeout));

    memset(&endpoint, 0, sizeof(endpoint));
    endpoint.sin_family = AF_INET;
    endpoint.sin_port = htons((u_short)port);
    if (InetPtonA(AF_INET, host, &endpoint.sin_addr) != 1) {
        copy_result(data, "ERROR invalid IPv4 address");
        closesocket(socket_handle); WSACleanup();
        return 3;
    }
    if (sendto(socket_handle, payload, payload_length, 0,
               (const struct sockaddr *)&endpoint, sizeof(endpoint)) == SOCKET_ERROR) {
        socket_error(data, "sendto");
        closesocket(socket_handle); WSACleanup();
        return 3;
    }

    received = recvfrom(socket_handle, data, (int)g_buffer_size - 1, 0, NULL, NULL);
    if (received == SOCKET_ERROR) socket_error(data, "receive (timeout or network error)");
    else data[received] = '\0';
    closesocket(socket_handle);
    WSACleanup();
    return 3;
}

__declspec(dllexport) int __stdcall FluxFTPVersion(
    HWND main_window, HWND active_window, char *data, char *parms, BOOL show, BOOL no_pause)
{
    (void)main_window; (void)active_window; (void)parms; (void)show; (void)no_pause;
    copy_result(data, FLUXFTP_VERSION);
    return 3;
}
