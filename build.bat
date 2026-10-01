@echo off
cd /d "%~dp0"
if /i "%~1"=="cli" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Cli %2
    if /i not "%~2"=="-Run" pause
    exit /b
)
rem Windows Terminal ignores -WindowStyle Hidden; a headless console shows no window at all.
start "" conhost.exe --headless powershell -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0build.ps1"
