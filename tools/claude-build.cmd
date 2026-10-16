@echo off
rem Build + test Arc Cache and SCSKiller-Arc. Log: tools\claude-build.log
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0claude-build.ps1" %*
echo.
echo Fertig. Das Fenster kann geschlossen werden.
timeout /t 15 >nul
