@echo off
setlocal
pushd "%~dp0"
where cl >nul 2>nul
if errorlevel 1 call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
if not exist bin mkdir bin
cl /nologo /LD /O2 /MT /W4 /WX /Fo:bin\ Loader.cpp /link /OUT:bin\ArcCache.Loader.dll /IMPLIB:bin\ArcCache.Loader.lib /INCREMENTAL:NO
exit /b %errorlevel%
