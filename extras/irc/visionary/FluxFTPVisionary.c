#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>

#define VERSION "1.00"
#define PIPE_PATH "\\\\.\\pipe\\FluxFTP.Visionary.v1"
typedef struct LOADINFO { DWORD mVersion; HWND mHwnd; BOOL mKeep; BOOL mUnicode; DWORD mBeta; DWORD mBytes; } LOADINFO;
static DWORD g_capacity = 4096;

static void result(char *dst, const char *src)
{
    size_t n = g_capacity > 1 ? (size_t)g_capacity : 4096;
    if (!dst) return;
    strncpy(dst, src ? src : "", n - 1); dst[n - 1] = 0;
}

__declspec(dllexport) void __stdcall LoadDll(LOADINFO *info)
{
    if (!info) return;
    info->mKeep = FALSE; info->mUnicode = FALSE;
    if (info->mBytes >= 512) g_capacity = info->mBytes;
}
__declspec(dllexport) int __stdcall UnloadDll(int timeout) { (void)timeout; return 1; }

/* Input: section release source-site target-site */
__declspec(dllexport) int __stdcall FluxFTPTransfer(
    HWND main_window, HWND active_window, char *data, char *parms, BOOL show, BOOL no_pause)
{
    char input[4096], request[4096], response[4096];
    char *section, *release, *source, *target;
    HANDLE pipe; DWORD written = 0, read = 0;
    (void)main_window; (void)active_window; (void)parms; (void)show; (void)no_pause;
    if (!data) return 3;
    strncpy(input, data, sizeof(input) - 1); input[sizeof(input) - 1] = 0;
    section = strtok(input, " "); release = strtok(NULL, " "); source = strtok(NULL, " "); target = strtok(NULL, " ");
    if (!section || !release || !source || !target) { result(data, "ERROR usage: section release source target"); return 3; }
    snprintf(request, sizeof(request), "TRANSFER\t%s\t%s\t%s\t%s\n", section, release, source, target);
    if (!WaitNamedPipeA(PIPE_PATH, 10000)) {
        char message[160]; snprintf(message, sizeof(message), "ERROR FluxFTP pipe unavailable (%lu)", GetLastError());
        result(data, message); return 3;
    }
    pipe = CreateFileA(PIPE_PATH, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
    if (pipe == INVALID_HANDLE_VALUE) {
        char message[160]; snprintf(message, sizeof(message), "ERROR opening FluxFTP pipe (%lu)", GetLastError());
        result(data, message); return 3;
    }
    if (!WriteFile(pipe, request, (DWORD)strlen(request), &written, NULL) || !ReadFile(pipe, response, sizeof(response) - 1, &read, NULL)) {
        char message[160]; snprintf(message, sizeof(message), "ERROR FluxFTP pipe I/O (%lu)", GetLastError());
        CloseHandle(pipe); result(data, message); return 3;
    }
    CloseHandle(pipe); response[read] = 0;
    while (read && (response[read - 1] == '\r' || response[read - 1] == '\n')) response[--read] = 0;
    result(data, response); return 3;
}

__declspec(dllexport) int __stdcall FluxFTPVersion(HWND m, HWND a, char *data, char *parms, BOOL show, BOOL no_pause)
{
    (void)m; (void)a; (void)parms; (void)show; (void)no_pause;
    result(data, VERSION); return 3;
}
