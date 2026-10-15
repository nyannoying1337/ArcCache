#include <windows.h>
#include <fstream>
#include <iostream>
#include <cstring>
#include <cstdint>
int wmain(int argc,wchar_t**argv){if(argc!=3)return 2;for(int i=1;i<argc;i++){unsigned char code[114]={};std::ifstream input(argv[i],std::ios::binary);input.read((char*)code,114);if(input.gcount()!=113||code[0x55]!=0xe8)return 3;
 // Stub only the string-member helper. This tests constructor writes, not the DLL/GPU.
 memset(code+0x55,0x90,5);void* memory=VirtualAlloc(nullptr,4096,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);if(!memory)return 4;memcpy(memory,code,113);DWORD old;if(!VirtualProtect(memory,4096,PAGE_EXECUTE_READ,&old))return 5;FlushInstructionCache(GetCurrentProcess(),memory,113);
 alignas(16) unsigned char object[128];memset(object,0xcc,sizeof(object));auto constructor=(void*(*)(void*))memory;void* result=constructor(object);uint64_t budget=0,limit=0;memcpy(&budget,object+8,8);memcpy(&limit,object+16,8);if(result!=object||budget!=0x100000000ULL||limit!=0x40000000ULL||object[0]!=0||object[24]!=0||object[0x24]!=1||object[0x48]!=0||object[0x4d]!=1)return 6;VirtualFree(memory,0,MEM_RELEASE);std::cout<<"PASS Xe1 constructor: 1 GiB limit; 4 GiB budget and flags preserved; no driver loaded\n";}
return 0;}
