@echo off
cd /d "%~dp0"
if /i "%~1"=="cli" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Cli %2
    if /i not "%~2"=="-Run" pause
    exit /b
)
start "" powershell -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0build.ps1"
