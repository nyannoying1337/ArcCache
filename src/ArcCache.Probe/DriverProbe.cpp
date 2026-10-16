#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <psapi.h>
#include <wrl/client.h>
#include <iostream>
#include <string>
#include <vector>
#include <unordered_map>
#include <set>
#include <map>
#include <mutex>
#include <cwctype>
#include <cstring>
#include <cstdio>

using Microsoft::WRL::ComPtr;

namespace {
using CreateFileWFn = HANDLE(WINAPI*)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
using CreateFile2Fn = HANDLE(WINAPI*)(LPCWSTR, DWORD, DWORD, DWORD, LPCREATEFILE2_EXTENDED_PARAMETERS);
using ReadFileFn = BOOL(WINAPI*)(HANDLE, LPVOID, DWORD, LPDWORD, LPOVERLAPPED);
using WriteFileFn = BOOL(WINAPI*)(HANDLE, LPCVOID, DWORD, LPDWORD, LPOVERLAPPED);
using GetOverlappedResultFn = BOOL(WINAPI*)(HANDLE, LPOVERLAPPED, LPDWORD, BOOL);
using GetOverlappedResultExFn = BOOL(WINAPI*)(HANDLE, LPOVERLAPPED, LPDWORD, DWORD, BOOL);
using ReadFileExFn = BOOL(WINAPI*)(HANDLE, LPVOID, DWORD, LPOVERLAPPED, LPOVERLAPPED_COMPLETION_ROUTINE);
using CreateFileMappingWFn = HANDLE(WINAPI*)(HANDLE, LPSECURITY_ATTRIBUTES, DWORD, DWORD, DWORD, LPCWSTR);
using MapViewOfFileFn = LPVOID(WINAPI*)(HANDLE, DWORD, DWORD, DWORD, SIZE_T);
using UnmapViewOfFileFn = BOOL(WINAPI*)(LPCVOID);
using FlushViewOfFileFn = BOOL(WINAPI*)(LPCVOID, SIZE_T);

std::wstring targetModule;
CreateFileWFn realCreateFileW = nullptr;
CreateFile2Fn realCreateFile2 = nullptr;
ReadFileFn realReadFile = nullptr;
WriteFileFn realWriteFile = nullptr;
GetOverlappedResultFn realGetOverlappedResult = nullptr;
GetOverlappedResultExFn realGetOverlappedResultEx = nullptr;
ReadFileExFn realReadFileEx = nullptr;
CreateFileMappingWFn realCreateFileMappingW = nullptr;
MapViewOfFileFn realMapViewOfFile = nullptr;
UnmapViewOfFileFn realUnmapViewOfFile = nullptr;
FlushViewOfFileFn realFlushViewOfFile = nullptr;

struct PendingIo { std::wstring path; bool write; };
struct FileSnapshot { bool exists = false; unsigned long long size = 0; FILETIME writeTime{}; };
struct OpenFile { std::wstring path; FileSnapshot before; };
struct MappedView { std::wstring path; FileSnapshot before; bool writable; };
std::mutex fileMutex;
std::unordered_map<HANDLE, OpenFile> fileHandles;
std::unordered_map<HANDLE, OpenFile> mappingHandles;
std::unordered_map<LPCVOID, MappedView> mappedViews;
std::unordered_map<LPOVERLAPPED, PendingIo> pendingIo;
std::set<std::wstring> writtenFiles;
std::map<std::wstring, FileSnapshot> cacheSnapshots;
std::vector<std::wstring> firstOpenPaths;
std::vector<std::wstring> cacheOpenDetails;
std::set<std::string> importedFileApis;
std::set<std::wstring> expectedCacheFiles;
unsigned long long observedReadBytes = 0;
unsigned long long observedMappedBytes = 0;
// Overlapped reads that were issued on the cache file but whose completion the probe could
// not observe (drivers often wait on the event and test HasOverlappedIoCompleted, a macro,
// instead of calling GetOverlappedResult). Issuing the read is what proves the reload path ran.
unsigned long long issuedPendingReadBytes = 0;
unsigned long long cacheReadCalls = 0;
unsigned long long cacheWriteCalls = 0;
std::vector<std::wstring> cacheIoLog;
void LogCacheIo(const wchar_t* api, bool write, BOOL ok, DWORD error, DWORD requested, DWORD done) {
    if (cacheIoLog.size() >= 12) return;
    wchar_t line[192];
    swprintf_s(line, L"%s %s ok=%d error=%lu requested=%lu done=%lu", api, write ? L"write" : L"read", ok ? 1 : 0, error, requested, done);
    cacheIoLog.push_back(line);
}

bool IsExpectedCacheFile(const std::wstring& normalized) { return expectedCacheFiles.count(normalized) != 0; }

std::wstring NormalizePath(std::wstring path) {
    for (auto& c : path) c = (wchar_t)towlower(c);
    if (path.rfind(L"\\\\?\\", 0) == 0) path = path.substr(4);
    return path;
}

FileSnapshot Snapshot(const std::wstring& path) {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) return {};
    FileSnapshot result;
    result.exists = true;
    result.size = ((unsigned long long)data.nFileSizeHigh << 32) | data.nFileSizeLow;
    result.writeTime = data.ftLastWriteTime;
    return result;
}

bool IsCachePath(const std::wstring& path) {
    return path.find(L"shadercache") != std::wstring::npos || path.find(L"shader cache") != std::wstring::npos || path.find(L"shader_cache") != std::wstring::npos;
}

bool Changed(const FileSnapshot& before, const FileSnapshot& after) {
    return before.exists != after.exists || before.size != after.size || before.writeTime.dwHighDateTime != after.writeTime.dwHighDateTime || before.writeTime.dwLowDateTime != after.writeTime.dwLowDateTime;
}

void ObserveOpen(const wchar_t* path, HANDLE handle, DWORD access, DWORD disposition) {
    if (path) {
        std::wstring normalized = NormalizePath(path);
        FileSnapshot before = Snapshot(path);
        std::lock_guard<std::mutex> lock(fileMutex);
        if (firstOpenPaths.size() < 20) firstOpenPaths.push_back(path);
        if (IsCachePath(normalized) && cacheOpenDetails.size() < 20) {
            wchar_t detail[64];
            swprintf_s(detail, L" access=0x%08lX disposition=%lu", access, disposition);
            cacheOpenDetails.push_back(path + std::wstring(detail));
        }
        if (IsCachePath(normalized) && cacheSnapshots.find(normalized) == cacheSnapshots.end()) cacheSnapshots.emplace(normalized, before);
        if (handle != INVALID_HANDLE_VALUE) fileHandles[handle] = { normalized, before };
    }
}

HANDLE WINAPI HookCreateFileW(LPCWSTR path, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES sa,
    DWORD disposition, DWORD flags, HANDLE templ) {
    HANDLE h = realCreateFileW(path, access, share, sa, disposition, flags, templ);
    ObserveOpen(path, h, access, disposition);
    return h;
}

HANDLE WINAPI HookCreateFile2(LPCWSTR path, DWORD access, DWORD share, DWORD disposition,
    LPCREATEFILE2_EXTENDED_PARAMETERS parameters) {
    HANDLE h = realCreateFile2(path, access, share, disposition, parameters);
    ObserveOpen(path, h, access, disposition);
    return h;
}

void RecordIo(HANDLE h, bool write, DWORD bytes) {
    if (!bytes) return;
    std::lock_guard<std::mutex> lock(fileMutex);
    auto i = fileHandles.find(h);
    if (i == fileHandles.end()) return;
    if (write && IsCachePath(i->second.path)) writtenFiles.insert(i->second.path);
    else if (!write && IsExpectedCacheFile(i->second.path)) observedReadBytes += bytes;
}

BOOL WINAPI HookWriteFile(HANDLE h, LPCVOID data, DWORD count, LPDWORD done, LPOVERLAPPED ov) {
    BOOL ok = realWriteFile(h, data, count, done, ov);
    DWORD error = ok ? ERROR_SUCCESS : GetLastError();
    {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto f = fileHandles.find(h);
        if (f != fileHandles.end() && IsCachePath(f->second.path)) {
            ++cacheWriteCalls;
            LogCacheIo(L"WriteFile", true, ok, error, count, ok && done ? *done : 0);
            if (!ok && error == ERROR_IO_PENDING) writtenFiles.insert(f->second.path);
        }
    }
    if (!ok) SetLastError(error);
    if (ok) {
        DWORD bytes = done ? *done : ov ? (DWORD)ov->InternalHigh : 0;
        RecordIo(h, true, bytes);
    } else if (ov && error == ERROR_IO_PENDING) {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = fileHandles.find(h);
        if (i != fileHandles.end()) pendingIo[ov] = { i->second.path, true };
    }
    return ok;
}

BOOL WINAPI HookReadFile(HANDLE h, LPVOID data, DWORD count, LPDWORD done, LPOVERLAPPED ov) {
    BOOL ok = realReadFile(h, data, count, done, ov);
    DWORD error = ok ? ERROR_SUCCESS : GetLastError();
    {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto f = fileHandles.find(h);
        if (f != fileHandles.end() && IsCachePath(f->second.path)) {
            ++cacheReadCalls;
            LogCacheIo(L"ReadFile", false, ok, error, count, ok && done ? *done : 0);
            if (!ok && error == ERROR_IO_PENDING && IsExpectedCacheFile(f->second.path)) issuedPendingReadBytes += count;
        }
    }
    if (!ok) SetLastError(error);
    if (ok) {
        DWORD bytes = done ? *done : ov ? (DWORD)ov->InternalHigh : 0;
        RecordIo(h, false, bytes);
    } else if (ov && error == ERROR_IO_PENDING) {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = fileHandles.find(h);
        if (i != fileHandles.end()) pendingIo[ov] = { i->second.path, false };
    }
    return ok;
}

BOOL WINAPI HookGetOverlappedResult(HANDLE h, LPOVERLAPPED ov, LPDWORD done, BOOL wait) {
    BOOL ok = realGetOverlappedResult(h, ov, done, wait);
    DWORD error = ok ? ERROR_SUCCESS : GetLastError();
    PendingIo pending{};
    bool found = false;
    {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = pendingIo.find(ov);
        auto file = fileHandles.find(h);
        if (i != pendingIo.end() && file != fileHandles.end() && file->second.path == i->second.path) {
            pending = i->second;
            found = true;
            if (ok || error != ERROR_IO_INCOMPLETE) pendingIo.erase(i);
        }
    }
    if (ok && found && done && *done) RecordIo(h, pending.write, *done);
    if (!ok) SetLastError(error);
    return ok;
}

BOOL WINAPI HookGetOverlappedResultEx(HANDLE h, LPOVERLAPPED ov, LPDWORD done, DWORD timeout, BOOL alertable) {
    BOOL ok = realGetOverlappedResultEx(h, ov, done, timeout, alertable);
    DWORD error = ok ? ERROR_SUCCESS : GetLastError();
    PendingIo pending{};
    bool found = false;
    {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = pendingIo.find(ov);
        auto file = fileHandles.find(h);
        if (i != pendingIo.end() && file != fileHandles.end() && file->second.path == i->second.path) {
            pending = i->second;
            found = true;
            if (ok || (error != ERROR_IO_INCOMPLETE && error != WAIT_TIMEOUT)) pendingIo.erase(i);
        }
    }
    if (ok && found && done && *done) RecordIo(h, pending.write, *done);
    if (!ok) SetLastError(error);
    return ok;
}

BOOL WINAPI HookReadFileEx(HANDLE h, LPVOID data, DWORD count, LPOVERLAPPED ov, LPOVERLAPPED_COMPLETION_ROUTINE routine) {
    BOOL ok = realReadFileEx(h, data, count, ov, routine);
    DWORD error = ok ? ERROR_SUCCESS : GetLastError();
    {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto f = fileHandles.find(h);
        if (f != fileHandles.end() && IsCachePath(f->second.path)) {
            ++cacheReadCalls;
            LogCacheIo(L"ReadFileEx", false, ok, error, count, 0);
            if (ok && IsExpectedCacheFile(f->second.path)) issuedPendingReadBytes += count;
        }
    }
    if (!ok) SetLastError(error);
    return ok;
}

HANDLE WINAPI HookCreateFileMappingW(HANDLE file, LPSECURITY_ATTRIBUTES sa, DWORD protect,
    DWORD maxHigh, DWORD maxLow, LPCWSTR name) {
    HANDLE mapping = realCreateFileMappingW(file, sa, protect, maxHigh, maxLow, name);
    if (mapping && mapping != INVALID_HANDLE_VALUE) {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = fileHandles.find(file);
        if (i != fileHandles.end()) mappingHandles[mapping] = i->second;
    }
    return mapping;
}

LPVOID WINAPI HookMapViewOfFile(HANDLE mapping, DWORD access, DWORD offHigh, DWORD offLow, SIZE_T bytes) {
    LPVOID view = realMapViewOfFile(mapping, access, offHigh, offLow, bytes);
    if (view) {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = mappingHandles.find(mapping);
        if (i != mappingHandles.end()) {
            mappedViews[view] = { i->second.path, i->second.before,
                (access & (FILE_MAP_WRITE | FILE_MAP_ALL_ACCESS | FILE_MAP_COPY)) != 0 };
            if ((access & (FILE_MAP_READ | FILE_MAP_ALL_ACCESS | FILE_MAP_COPY)) && IsExpectedCacheFile(i->second.path)) {
                MEMORY_BASIC_INFORMATION info{};
                SIZE_T mapped = bytes;
                if (!mapped && VirtualQuery(view, &info, sizeof(info))) mapped = info.RegionSize;
                observedMappedBytes += mapped ? mapped : 1;
            }
        }
    }
    return view;
}

BOOL WINAPI HookFlushViewOfFile(LPCVOID view, SIZE_T bytes) {
    BOOL ok = realFlushViewOfFile(view, bytes);
    if (ok) {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = mappedViews.find(view);
        if (i != mappedViews.end() && i->second.writable && IsCachePath(i->second.path) &&
            Changed(i->second.before, Snapshot(i->second.path))) writtenFiles.insert(i->second.path);
    }
    return ok;
}

BOOL WINAPI HookUnmapViewOfFile(LPCVOID view) {
    BOOL ok = realUnmapViewOfFile(view);
    if (ok) {
        std::lock_guard<std::mutex> lock(fileMutex);
        auto i = mappedViews.find(view);
        if (i != mappedViews.end()) {
            if (i->second.writable && IsCachePath(i->second.path) &&
                Changed(i->second.before, Snapshot(i->second.path))) writtenFiles.insert(i->second.path);
            mappedViews.erase(i);
        }
    }
    return ok;
}

template<class T>
void PatchImportSlot(IMAGE_THUNK_DATA64* slot, T replacement, T& original) {
    DWORD old = 0;
    if (!VirtualProtect(&slot->u1.Function, sizeof(ULONG_PTR), PAGE_READWRITE, &old)) return;
    if (!original) original = (T)(ULONG_PTR)slot->u1.Function;
    slot->u1.Function = (ULONG_PTR)replacement;
    DWORD ignored;
    VirtualProtect(&slot->u1.Function, sizeof(ULONG_PTR), old, &ignored);
}

template<class T>
void HookThunk(HMODULE module, const char* wanted, T replacement, T& original, bool driverModule = false) {
    auto base = (BYTE*)module;
    auto dos = (IMAGE_DOS_HEADER*)base;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE) return;
    auto nt = (IMAGE_NT_HEADERS64*)(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC) return;
    auto dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!dir.VirtualAddress) return;
    auto imp = (IMAGE_IMPORT_DESCRIPTOR*)(base + dir.VirtualAddress);
    for (; imp->Name; ++imp) {
        auto slots = (IMAGE_THUNK_DATA64*)(base + imp->FirstThunk);
        if (imp->OriginalFirstThunk) {
            auto names = (IMAGE_THUNK_DATA64*)(base + imp->OriginalFirstThunk);
            for (; names->u1.AddressOfData; ++names, ++slots) {
                if (IMAGE_SNAP_BY_ORDINAL64(names->u1.Ordinal)) continue;
                auto by = (IMAGE_IMPORT_BY_NAME*)(base + names->u1.AddressOfData);
                if (strcmp((char*)by->Name, wanted) != 0) continue;
                if (driverModule) importedFileApis.insert(wanted);
                PatchImportSlot(slots, replacement, original);
            }
        } else {
            HMODULE imported = GetModuleHandleA((char*)(base + imp->Name));
            FARPROC target = imported ? GetProcAddress(imported, wanted) : nullptr;
            if (!target) continue;
            for (; slots->u1.Function; ++slots) {
                if (slots->u1.Function != (ULONG_PTR)target) continue;
                if (driverModule) importedFileApis.insert(wanted);
                PatchImportSlot(slots, replacement, original);
            }
        }
    }
}

void HookLoadedFileApis(HMODULE driver) {
    HMODULE modules[4096];
    DWORD needed = 0;
    if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &needed)) return;
    for (DWORD i = 0; i < needed / sizeof(HMODULE) && i < 4096; ++i) {
        HMODULE module = modules[i];
        if (!module || module == GetModuleHandleW(nullptr)) continue;
        wchar_t path[32768];
        DWORD n = GetModuleFileNameW(module, path, (DWORD)_countof(path));
        if (!n || n >= _countof(path)) continue;
        // Cache I/O may be delegated to a helper DLL rather than imported by
        // the primary driver module. Hook all loaded process modules in this
        // isolated probe, before device creation and again after new modules load.
        HookThunk(module, "CreateFileW", HookCreateFileW, realCreateFileW, module == driver);
        HookThunk(module, "CreateFile2", HookCreateFile2, realCreateFile2, module == driver);
        HookThunk(module, "ReadFile", HookReadFile, realReadFile, module == driver);
        HookThunk(module, "WriteFile", HookWriteFile, realWriteFile, module == driver);
        HookThunk(module, "GetOverlappedResult", HookGetOverlappedResult, realGetOverlappedResult, module == driver);
        HookThunk(module, "GetOverlappedResultEx", HookGetOverlappedResultEx, realGetOverlappedResultEx, module == driver);
        HookThunk(module, "ReadFileEx", HookReadFileEx, realReadFileEx, module == driver);
        HookThunk(module, "CreateFileMappingW", HookCreateFileMappingW, realCreateFileMappingW, module == driver);
        HookThunk(module, "MapViewOfFile", HookMapViewOfFile, realMapViewOfFile, module == driver);
        HookThunk(module, "UnmapViewOfFile", HookUnmapViewOfFile, realUnmapViewOfFile, module == driver);
        HookThunk(module, "FlushViewOfFile", HookFlushViewOfFile, realFlushViewOfFile, module == driver);
    }
}

bool LoadAndHookPrivateDriver(const std::wstring& expected) {
    HMODULE exe = GetModuleHandleW(nullptr);
    wchar_t own[32768];
    DWORD n = GetModuleFileNameW(exe, own, (DWORD)_countof(own));
    if (!n || n >= _countof(own)) return false;
    std::wstring path = std::wstring(own) + L".local\\" + targetModule;
    HMODULE driver = LoadLibraryExW(path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!driver) {
        std::wcout << L"Private driver preload failed: " << path << L" · Win32 " << GetLastError() << L'\n';
        return false;
    }
    wchar_t loadedPath[32768];
    n = GetModuleFileNameW(driver, loadedPath, (DWORD)_countof(loadedPath));
    if (!n || n >= _countof(loadedPath) || _wcsicmp(loadedPath, path.c_str()) != 0) {
        std::wcout << L"Preloaded module path did not match the private driver copy.\n";
        FreeLibrary(driver);
        return false;
    }
    std::wcout << L"Preloaded private driver: " << loadedPath << L'\n';
    expectedCacheFiles.clear();
    for (size_t start = 0; start <= expected.size();) {
        size_t end = expected.find(L'|', start);
        if (end == std::wstring::npos) end = expected.size();
        std::wstring item = expected.substr(start, end - start);
        if (!item.empty()) expectedCacheFiles.insert(NormalizePath(item));
        start = end + 1;
    }
    HookLoadedFileApis(driver);
    std::wcout << L"Driver imports:";
    for (const auto& api : importedFileApis) std::wcout << L' ' << std::wstring(api.begin(), api.end());
    std::wcout << L'\n';
    return (realCreateFileW || realCreateFile2) && realReadFile && realWriteFile;
}

bool PrivateDriverLoaded(bool controlRun, std::wstring& loaded) {
    HMODULE modules[4096];
    DWORD needed = 0;
    if (!EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &needed)) return false;
    wchar_t own[32768];
    GetModuleFileNameW(nullptr, own, (DWORD)_countof(own));
    std::wstring local = std::wstring(own) + L".local\\";
    std::vector<std::wstring> found;
    for (DWORD i = 0; i < needed / sizeof(HMODULE) && i < 4096; i++) {
        wchar_t path[32768];
        if (!GetModuleFileNameW(modules[i], path, (DWORD)_countof(path))) continue;
        std::wstring full(path), leaf = full.substr(full.find_last_of(L"\\") + 1);
        if (_wcsicmp(leaf.c_str(), L"igd12um64xe2.dll") && _wcsicmp(leaf.c_str(), L"igd12um64xe3.dll") && _wcsicmp(leaf.c_str(), L"igd12um64xeh.dll")) continue;
        std::wcout << L"Observed Intel module: " << full << L'\n';
        if (_wcsicmp(full.c_str(), (local + leaf).c_str()) == 0) found.push_back(leaf);
    }
    if (found.size() != 1) return false;
    loaded = found[0];
    std::wcout << L"LoadedCacheDriver: " << loaded << L'\n';
    return controlRun || _wcsicmp(loaded.c_str(), targetModule.c_str()) == 0;
}

void PrintFileObservation() {
    std::lock_guard<std::mutex> lock(fileMutex);
    for (const auto& entry : cacheSnapshots) {
        FileSnapshot after = Snapshot(entry.first);
        if (after.exists && after.size > 0 && Changed(entry.second, after)) writtenFiles.insert(entry.first);
    }
    std::wcout << L"Driver CreateFile paths (first " << firstOpenPaths.size() << L"):\n";
    for (size_t i = 0; i < firstOpenPaths.size(); ++i)
        std::wcout << L"DriverOpen[" << (i + 1) << L"]: " << firstOpenPaths[i] << L'\n';
    for (const auto& detail : cacheOpenDetails) std::wcout << L"CacheOpen: " << detail << L'\n';
    for (const auto& path : writtenFiles) std::wcout << L"CacheFileWritten: " << path << L'\n';
    for (const auto& line : cacheIoLog) std::wcout << L"CacheIo: " << line << L'\n';
    std::wcout << L"CacheReadCalls: " << std::dec << cacheReadCalls << L'\n';
    std::wcout << L"CacheWriteCalls: " << std::dec << cacheWriteCalls << L'\n';
    std::wcout << L"CacheCompletedReadBytes: " << std::dec << observedReadBytes << L'\n';
    std::wcout << L"CacheIssuedPendingReadBytes: " << std::dec << issuedPendingReadBytes << L'\n';
    std::wcout << L"CacheMappedBytes: " << std::dec << observedMappedBytes << L'\n';
    std::wcout << L"CacheReadBytes: " << std::dec << (observedReadBytes + observedMappedBytes + issuedPendingReadBytes) << L'\n';
}
}

int Run(int argc, wchar_t** argv) {
    if (argc != 6 || (_wcsicmp(argv[1], L"igd12um64xe2.dll") != 0 && _wcsicmp(argv[1], L"igd12um64xe3.dll") != 0 && _wcsicmp(argv[1], L"igd12um64xeh.dll") != 0) || (_wcsicmp(argv[3], L"control") && _wcsicmp(argv[3], L"size")) || (_wcsicmp(argv[4], L"write") && _wcsicmp(argv[4], L"reload"))) return 11;
    targetModule = argv[1];
    bool controlRun = _wcsicmp(argv[3], L"control") == 0;
    bool reloadRun = _wcsicmp(argv[4], L"reload") == 0;
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    if (!LoadAndHookPrivateDriver(reloadRun ? argv[5] : L"")) {
        std::cout << "File observation hooks unavailable; cache reload is not verified.\n";
        return 13;
    }

    ComPtr<IDXGIFactory6> factory;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)))) return 2;
    HookLoadedFileApis(GetModuleHandleW(targetModule.c_str()));
    for (UINT i = 0; i < 32; i++) {
        ComPtr<IDXGIAdapter1> adapter;
        if (factory->EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, IID_PPV_ARGS(&adapter)) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 desc = {};
        if (!adapter || FAILED(adapter->GetDesc1(&desc)) || desc.VendorId != 0x8086 || _wcsicmp(desc.Description, argv[2]) != 0 || (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE)) continue;
        std::wcout << L"Intel adapter: " << desc.Description << L'\n';
        ComPtr<ID3D12Device> device;
        HRESULT hr = D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&device));
        HookLoadedFileApis(GetModuleHandleW(targetModule.c_str()));
        std::wcout << L"Device HRESULT: 0x" << std::hex << (unsigned)hr << std::dec << L'\n';
        std::wstring loaded;
        if (FAILED(hr) || !PrivateDriverLoaded(controlRun, loaded)) continue;

        ComPtr<ID3DBlob> error, rootData;
        std::vector<ComPtr<ID3D12PipelineState>> states;
        states.reserve(32);
        D3D12_DESCRIPTOR_RANGE range = {};
        range.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
        range.NumDescriptors = 1;
        range.BaseShaderRegister = 0;
        range.RegisterSpace = 0;
        range.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
        D3D12_ROOT_PARAMETER rootParameter = {};
        rootParameter.ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        rootParameter.DescriptorTable.NumDescriptorRanges = 1;
        rootParameter.DescriptorTable.pDescriptorRanges = &range;
        rootParameter.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;
        D3D12_ROOT_SIGNATURE_DESC rootDesc = {};
        rootDesc.NumParameters = 1;
        rootDesc.pParameters = &rootParameter;
        if (FAILED(D3D12SerializeRootSignature(&rootDesc, D3D_ROOT_SIGNATURE_VERSION_1, &rootData, &error))) return 6;
        ComPtr<ID3D12RootSignature> root;
        if (FAILED(device->CreateRootSignature(0, rootData->GetBufferPointer(), rootData->GetBufferSize(), IID_PPV_ARGS(&root)))) return 7;
        // Same salt for the write and reload run of one trial (same private exe path),
        // different salt for every new trial folder. The reload run then requests
        // exactly the pipelines the write run stored, so they come from the cache file.
        unsigned long long runSalt = 1469598103934665603ull;
        {
            wchar_t ownPath[32768];
            DWORD ownLength = GetModuleFileNameW(nullptr, ownPath, (DWORD)_countof(ownPath));
            for (DWORD c = 0; c < ownLength && c < _countof(ownPath); ++c) { runSalt ^= (unsigned long long)towlower(ownPath[c]); runSalt *= 1099511628211ull; }
        }
        for (unsigned threads = 1; threads <= 32; threads++) {
            // Make each run compile cache-distinct shaders. A fixed no-op shader
            // would become a cache hit after the first test and prove no write.
            const unsigned salt = static_cast<unsigned>((runSalt >> (threads % 32)) ^ (threads * 0x45D9F3Bu));
            std::string source = "RWByteAddressBuffer output : register(u0); [numthreads(" + std::to_string(threads) + ",1,1)] void main(uint3 id : SV_DispatchThreadID) { output.Store(id.x * 4, id.x ^ " + std::to_string(salt) + "); if ((id.x & " + std::to_string(salt) + ") == 1) GroupMemoryBarrierWithGroupSync(); }";
            ComPtr<ID3DBlob> shader;
            hr = D3DCompile(source.c_str(), source.size(), "ArcCacheDriverProbe", nullptr, nullptr, "main", "cs_5_0", 0, 0, &shader, &error);
            if (FAILED(hr)) return 5;
            D3D12_COMPUTE_PIPELINE_STATE_DESC pipeline = {};
            pipeline.pRootSignature = root.Get();
            pipeline.CS = { shader->GetBufferPointer(), shader->GetBufferSize() };
            ComPtr<ID3D12PipelineState> state;
            hr = device->CreateComputePipelineState(&pipeline, IID_PPV_ARGS(&state));
            if (FAILED(hr)) return 8;
            states.push_back(state);
        }
        std::wcout << L"PASS: " << (reloadRun ? L"reload" : L"write") << L" run created 32 distinct compute pipelines.\n";

        // Dispatch every distinct pipeline. PSO creation alone is too weak a
        // stimulus to prove the real driver shader-cache write/reload path.
        D3D12_DESCRIPTOR_HEAP_DESC heapDesc = {};
        heapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        heapDesc.NumDescriptors = 1;
        heapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ComPtr<ID3D12DescriptorHeap> uavHeap;
        if (FAILED(device->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&uavHeap)))) return 9;
        D3D12_HEAP_PROPERTIES heapProps = {};
        heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;
        D3D12_RESOURCE_DESC bufferDesc = {};
        bufferDesc.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
        bufferDesc.Width = 4096;
        bufferDesc.Height = 1;
        bufferDesc.DepthOrArraySize = 1;
        bufferDesc.MipLevels = 1;
        bufferDesc.SampleDesc.Count = 1;
        bufferDesc.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
        bufferDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ComPtr<ID3D12Resource> outputBuffer;
        if (FAILED(device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &bufferDesc,
            D3D12_RESOURCE_STATE_UNORDERED_ACCESS, nullptr, IID_PPV_ARGS(&outputBuffer)))) return 9;
        D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.Format = DXGI_FORMAT_R32_TYPELESS;
        uavDesc.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
        uavDesc.Buffer.NumElements = 1024;
        uavDesc.Buffer.Flags = D3D12_BUFFER_UAV_FLAG_RAW;
        device->CreateUnorderedAccessView(outputBuffer.Get(), nullptr, &uavDesc, uavHeap->GetCPUDescriptorHandleForHeapStart());
        D3D12_COMMAND_QUEUE_DESC queueDesc = {};
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ComPtr<ID3D12CommandQueue> queue;
        if (FAILED(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)))) return 9;
        ComPtr<ID3D12CommandAllocator> allocator;
        if (FAILED(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator)))) return 9;
        ComPtr<ID3D12GraphicsCommandList> commandList;
        if (FAILED(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, allocator.Get(), nullptr, IID_PPV_ARGS(&commandList)))) return 9;
        commandList->SetComputeRootSignature(root.Get());
        ID3D12DescriptorHeap* heaps[] = { uavHeap.Get() };
        commandList->SetDescriptorHeaps(1, heaps);
        commandList->SetComputeRootDescriptorTable(0, uavHeap->GetGPUDescriptorHandleForHeapStart());
        for (const auto& state : states) {
            commandList->SetPipelineState(state.Get());
            commandList->Dispatch(1, 1, 1);
        }
        if (FAILED(commandList->Close())) return 9;
        ID3D12CommandList* lists[] = { commandList.Get() };
        queue->ExecuteCommandLists(1, lists);
        ComPtr<ID3D12Fence> fence;
        if (FAILED(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence)))) return 9;
        queue->Signal(fence.Get(), 1);
        HANDLE eventHandle = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!eventHandle) return 9;
        if (fence->GetCompletedValue() < 1) {
            fence->SetEventOnCompletion(1, eventHandle);
            WaitForSingleObject(eventHandle, 10000);
        }
        CloseHandle(eventHandle);
        if (fence->GetCompletedValue() < 1) return 9;

        // Some drivers flush their pipeline-cache file only when the device is destroyed.
        states.clear();
        commandList.Reset();
        allocator.Reset();
        queue.Reset();
        outputBuffer.Reset();
        uavHeap.Reset();
        fence.Reset();
        root.Reset();
        rootData.Reset();
        error.Reset();
        device.Reset();
        adapter.Reset();
        factory.Reset();
        // Driver cache persistence can be deferred to a worker after device release.
        Sleep(2000);
        PrintFileObservation();
        if (reloadRun && observedReadBytes + observedMappedBytes + issuedPendingReadBytes == 0) {
            std::wcout << L"NOT VERIFIED: the exact file written by the first run was not read.\n";
            return 12;
        }
        if (!reloadRun && writtenFiles.empty()) {
            std::wcout << L"NOT VERIFIED: no driver file write was observed.\n";
            return 12;
        }
        return 0;
    }
    std::wcout << L"NOT VERIFIED: no Intel adapter loaded the private Intel module. Check matching graphics hardware, active package and private-routing restart.\n";
    return 10;
}

// The probe patches import slots of every loaded module. Normal process exit runs CRT
// static destructors (the hook maps and mutex) while driver worker threads and DLL
// detach code can still call the hooked file APIs, which caused 0xC0000005 at exit.
// Flush the report and end the process without any teardown instead.
int wmain(int argc, wchar_t** argv) {
    int code = Run(argc, argv);
    std::wcout.flush();
    std::cout.flush();
    fflush(stdout);
    fflush(stderr);
    TerminateProcess(GetCurrentProcess(), (UINT)code);
    return code;
}
