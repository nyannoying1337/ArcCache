// Arc Cache's generic adapter for a locally supplied recorder. No recorder implementation is included here.
// Only the supplied DLL's loader imports are redirected. Its recording, permission checks and other imports stay intact.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>

static HMODULE own_module;

// exe.local can return the game's proxy for an absolute System32\d3d12.dll request. Use a separately named,
// unchanged copy of that Windows DLL instead. Windows and the supplied recorder still create the actual device.
static bool system_request(LPCWSTR name, wchar_t* target, DWORD capacity) {
    if (!name) return false;
    wchar_t system[MAX_PATH];
    UINT n = GetSystemDirectoryW(system, MAX_PATH);
    if (!n || n + 11 >= MAX_PATH) return false;
    if (wcscat_s(system, L"\\d3d12.dll") || _wcsicmp(name, system)) return false;
    DWORD used = GetModuleFileNameW(own_module, target, capacity);
    if (!used || used >= capacity) return false;
    wchar_t* slash = wcsrchr(target, L'\\');
    if (!slash) return false;
    slash[1] = 0;
    return wcscat_s(target, capacity, L"ArcCache.System\\d3d12.arccache-system.dll") == 0;
}

extern "C" __declspec(dllexport) HMODULE WINAPI ArcCacheLoadLibraryW(LPCWSTR name) {
    wchar_t target[32768];
    return LoadLibraryW(system_request(name, target, 32768) ? target : name);
}

extern "C" __declspec(dllexport) HMODULE WINAPI ArcCacheLoadLibraryExW(LPCWSTR name, HANDLE file, DWORD flags) {
    wchar_t target[32768];
    return LoadLibraryExW(system_request(name, target, 32768) ? target : name, file, flags);
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { own_module = self; DisableThreadLibraryCalls(self); }
    return TRUE;
}
