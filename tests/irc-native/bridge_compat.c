#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stddef.h>
#include <stdio.h>
#include <string.h>
typedef struct { DWORD version; HWND hwnd; BOOL keep; BOOL unicode; DWORD beta; DWORD bytes; } Info;
typedef void (__stdcall *Load)(Info *);
typedef int (__stdcall *Version)(HWND, HWND, char *, char *, BOOL, BOOL);
int main(int argc, char **argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
    if (argc != 2) return 1;
    HMODULE dll = LoadLibraryA(argv[1]);
    if (!dll) return 2;
    Load load = (Load)(void *)GetProcAddress(dll, "LoadDll");
    Version version = (Version)(void *)GetProcAddress(dll, "FluxFTPVersion");
    if (!load || !version) return 3;
    SYSTEM_INFO system; GetSystemInfo(&system);
    char *pages = VirtualAlloc(NULL, system.dwPageSize * 2, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    DWORD previous;
    if (!pages || !VirtualProtect(pages + system.dwPageSize, system.dwPageSize, PAGE_NOACCESS, &previous)) return 4;
    /* Put the absent mBytes field on an inaccessible page, like a short legacy struct. */
    Info *legacy = (Info *)(pages + system.dwPageSize - offsetof(Info, bytes));
    memset(legacy, 0, offsetof(Info, bytes));
    legacy->version = MAKELONG(52, 7);
    load(legacy);
    char data[4097]; memset(data, 'X', sizeof(data));
    if (version(NULL, NULL, data, NULL, FALSE, FALSE) != 3 || data[4096] != 'X' || strlen(data) > 10 || data[20] != 'X') return 5;
    Info modern = {0}; modern.version = MAKELONG(7, 64); modern.bytes = 2;
    load(&modern); memset(data, 'X', sizeof(data));
    version(NULL, NULL, data, NULL, FALSE, FALSE);
    if (data[1] != 0 || data[2] != 'X') return 6;
    load(legacy); memset(data, 'X', sizeof(data));
    version(NULL, NULL, data, NULL, FALSE, FALSE);
    if (strlen(data) < 3 || data[20] != 'X') return 7;
    legacy->version = MAKELONG(7, 52); load(legacy);
    modern.version = MAKELONG(64, 7); load(&modern);
    memset(data, 'X', sizeof(data)); version(NULL, NULL, data, NULL, FALSE, FALSE);
    if (data[1] != 0 || data[2] != 'X') return 8;
    VirtualFree(pages, 0, MEM_RELEASE); FreeLibrary(dll);
    puts("PASS: legacy guarded LOADINFO, modern small buffer, reload, no padding writes");
    return 0;
}
