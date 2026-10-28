// Runtime test for the generic adapter. The recorder to test is supplied locally, never compiled into this fixture.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d12.h>
#include <stdio.h>

int wmain() {
    HMODULE recorder=LoadLibraryW(L"d3d12.dll");
    if(!recorder){printf("FAIL recorder load: %lu\n",GetLastError());return 1;}
    if(!GetModuleHandleW(L"ArcCache.Loader.dll")||!GetModuleHandleW(L"d3d12.arccache-system.dll")){puts("FAIL adapter or renamed Windows runtime not loaded");return 2;}
    auto create=(PFN_D3D12_CREATE_DEVICE)GetProcAddress(recorder,"D3D12CreateDevice");
    ID3D12Device* device=nullptr;
    HRESULT result=create?create(nullptr,D3D_FEATURE_LEVEL_11_0,IID_PPV_ARGS(&device)):E_NOINTERFACE;
    if(FAILED(result)||!device){printf("FAIL device creation: 0x%08lx\n",result);return 3;}
    device->Release();puts("PASS adapted user recorder creates a DirectX 12 device beside exe.local");return 0;
}
