@echo off
setlocal
pushd "%~dp0"
where cl >nul 2>nul
if errorlevel 1 call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
if not exist bin mkdir bin
cl /nologo /EHsc /O2 /MT /Fe:bin\ArcCache.DriverProbe.exe /Fo:bin\ DriverProbe.cpp d3d12.lib dxgi.lib d3dcompiler.lib psapi.lib
exit /b %errorlevel%
