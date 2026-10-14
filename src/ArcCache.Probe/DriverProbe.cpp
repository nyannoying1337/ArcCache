#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <d3dcompiler.h>
#include <psapi.h>
#include <wrl/client.h>
#include <iostream>
#include <string>
using Microsoft::WRL::ComPtr;
std::wstring targetModule;
bool PrivateDriverLoaded(){HMODULE modules[4096];DWORD needed=0;if(!EnumProcessModules(GetCurrentProcess(),modules,sizeof(modules),&needed))return false;wchar_t own[4096];GetModuleFileNameW(nullptr,own,4096);std::wstring expected=std::wstring(own)+L".local\\"+targetModule;for(DWORD i=0;i<needed/sizeof(HMODULE)&&i<4096;i++){wchar_t path[4096];if(GetModuleFileNameW(modules[i],path,4096)&&_wcsicmp(std::wstring(path).substr(std::wstring(path).find_last_of(L"\\")+1).c_str(),targetModule.c_str())==0){std::wcout<<L"Observed Intel module: "<<path<<L'\n';return _wcsicmp(path,expected.c_str())==0;}}return false;}
int wmain(int argc,wchar_t** argv){
 if(argc!=3||(_wcsicmp(argv[1],L"igd12um64xe2.dll")!=0&&_wcsicmp(argv[1],L"igd12um64xe3.dll")!=0))return 11;targetModule=argv[1];
 SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);
 ComPtr<IDXGIFactory6> factory;if(FAILED(CreateDXGIFactory2(0,IID_PPV_ARGS(&factory))))return 2;
 for(UINT i=0;i<32;i++){
  ComPtr<IDXGIAdapter1> adapter;if(factory->EnumAdapterByGpuPreference(i,DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,IID_PPV_ARGS(&adapter))==DXGI_ERROR_NOT_FOUND)break;
  DXGI_ADAPTER_DESC1 desc={};if(!adapter||FAILED(adapter->GetDesc1(&desc))||desc.VendorId!=0x8086||_wcsicmp(desc.Description,argv[2])!=0||(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE))continue;
  std::wcout<<L"Intel adapter: "<<desc.Description<<L'\n';ComPtr<ID3D12Device> device;HRESULT hr=D3D12CreateDevice(adapter.Get(),D3D_FEATURE_LEVEL_12_0,IID_PPV_ARGS(&device));
  std::cout<<"Device HRESULT: 0x"<<std::hex<<(unsigned)hr<<'\n';if(FAILED(hr)||!PrivateDriverLoaded())continue;
  const char* source="[numthreads(1,1,1)] void main(uint3 id : SV_DispatchThreadID) {}";
  ComPtr<ID3DBlob> shader,error,rootData;hr=D3DCompile(source,strlen(source),"ArcCacheDriverProbe",nullptr,nullptr,"main","cs_5_0",0,0,&shader,&error);if(FAILED(hr))return 5;
  D3D12_ROOT_SIGNATURE_DESC rootDesc={};if(FAILED(D3D12SerializeRootSignature(&rootDesc,D3D_ROOT_SIGNATURE_VERSION_1,&rootData,&error)))return 6;
  ComPtr<ID3D12RootSignature> root;if(FAILED(device->CreateRootSignature(0,rootData->GetBufferPointer(),rootData->GetBufferSize(),IID_PPV_ARGS(&root))))return 7;
  D3D12_COMPUTE_PIPELINE_STATE_DESC pipeline={};pipeline.pRootSignature=root.Get();pipeline.CS={shader->GetBufferPointer(),shader->GetBufferSize()};ComPtr<ID3D12PipelineState> state;
  hr=device->CreateComputePipelineState(&pipeline,IID_PPV_ARGS(&state));std::cout<<"Compute pipeline HRESULT: 0x"<<std::hex<<(unsigned)hr<<'\n';if(FAILED(hr))return 8;
  std::cout<<"PASS: test driver copy observed; device and trivial compute pipeline created. No dispatch or game test performed. Capacity is unmeasured.\n";return 0;
 }
 std::cout<<"NOT VERIFIED: no Intel adapter loaded the private Intel module. Check matching graphics hardware, active package and private-routing restart.\n";return 10;
}
