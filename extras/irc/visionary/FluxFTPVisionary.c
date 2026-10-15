#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>

#define VERSION "2.0"
#define PIPE_PATH "\\\\.\\pipe\\FluxFTP.Visionary.v1"
typedef struct LOADINFO { DWORD mVersion; HWND mHwnd; BOOL mKeep; BOOL mUnicode; DWORD mBeta; DWORD mBytes; } LOADINFO;
static DWORD g_capacity = 4096;

static void result(char *dst, const char *src)
{
    size_t n = (size_t)g_capacity;
    if (!dst) return;
    size_t length = strlen(src ? src : "");
    if (length >= n) length = n - 1;
    memcpy(dst, src ? src : "", length); dst[length] = 0;
}

__declspec(dllexport) void __stdcall LoadDll(LOADINFO *info)
{
    if (!info) return;
    info->mKeep = FALSE; info->mUnicode = FALSE;
    /* mBytes does not exist before mIRC 7.64. Accept either word order; unknown versions use the legacy limit. */
    g_capacity = 4096;
    if ((HIWORD(info->mVersion) == 7 && LOWORD(info->mVersion) >= 64 && LOWORD(info->mVersion) <= 99) ||
        (LOWORD(info->mVersion) == 7 && HIWORD(info->mVersion) >= 64 && HIWORD(info->mVersion) <= 99)) {
        if (info->mBytes > 0 && info->mBytes < 4096) g_capacity = info->mBytes;
    }
}
__declspec(dllexport) int __stdcall UnloadDll(int timeout) { (void)timeout; return 1; }

/* Input: section release source-site target-site */
static int transfer_request(
    HWND main_window, HWND active_window, char *data, char *parms, BOOL show, BOOL no_pause, BOOL batch)
{
    char input[4096], request[4096], response[4096];
    char *section, *release, *source, *target, *tick;
    ULONGLONG entered = GetTickCount64(), chain = 0;
    HANDLE pipe; DWORD written = 0, read = 0;
    (void)main_window; (void)active_window; (void)parms; (void)show; (void)no_pause;
    if (!data) return 3;
    strncpy(input, data, sizeof(input) - 1); input[sizeof(input) - 1] = 0;
    section = strtok(input, " "); release = strtok(NULL, " "); source = strtok(NULL, " "); target = strtok(NULL, " ");
    if (!section || !release || !source || !target) { result(data, "ERROR usage: section release source target"); return 3; }
    tick = strtok(NULL, " ");
    if (tick) {
        ULONGLONG raw = strtoull(tick, NULL, 10);
        if (raw > 0 && raw <= entered && entered - raw <= 300000) chain = raw;
        else if (raw > 0 && raw <= 0xffffffffULL) {
            DWORD age = (DWORD)entered - (DWORD)raw;
            if (age <= 300000 && entered >= age) chain = entered - age;
        }
    }
    if (batch) {
        /* Batch input: section release chains tick; source is the chain list, target is its timestamp. */
        tick = target;
        ULONGLONG raw = strtoull(tick, NULL, 10);
        DWORD age = (DWORD)entered - (DWORD)raw;
        chain = raw > 0 && age <= 300000 && entered >= age ? entered - age : 0;
        if (strlen(source) + strlen(section) + strlen(release) + 96 >= sizeof(request)) { result(data, "ERROR chain batch too large"); return 3; }
        snprintf(request, sizeof(request), "CHAINS\t%s\t%s\t%s\t%llu\t%llu\n", section, release, source, chain, entered);
    } else snprintf(request, sizeof(request), "TRANSFER\t%s\t%s\t%s\t%s\t%llu\t%llu\n", section, release, source, target, chain, entered);
    {
        ULONGLONG deadline = GetTickCount64() + 10000;
        DWORD pipe_error = ERROR_FILE_NOT_FOUND;
        pipe = INVALID_HANDLE_VALUE;
        do {
            if (WaitNamedPipeA(PIPE_PATH, 250)) {
                pipe = CreateFileA(PIPE_PATH, GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
                if (pipe != INVALID_HANDLE_VALUE) break;
            }
            pipe_error = GetLastError();
            if (pipe_error != ERROR_FILE_NOT_FOUND && pipe_error != ERROR_PIPE_BUSY && pipe_error != ERROR_SEM_TIMEOUT) break;
            Sleep(25);
        } while (GetTickCount64() < deadline);
        if (pipe == INVALID_HANDLE_VALUE) SetLastError(pipe_error);
    }
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

__declspec(dllexport) int __stdcall FluxFTPTransfer(HWND main_window, HWND active_window, char *data, char *parms, BOOL show, BOOL no_pause)
{ return transfer_request(main_window, active_window, data, parms, show, no_pause, FALSE); }
__declspec(dllexport) int __stdcall FluxFTPChains(HWND main_window, HWND active_window, char *data, char *parms, BOOL show, BOOL no_pause)
{ return transfer_request(main_window, active_window, data, parms, show, no_pause, TRUE); }
