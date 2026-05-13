@echo off
REM BluetoothAudioReceiver setup wrapper.
REM Delegates to Setup.ps1 so there is only one setup implementation.

setlocal
set "SCRIPT_DIR=%~dp0"
set "SETUP_PS1=%SCRIPT_DIR%Setup.ps1"

if "%~1"=="" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SETUP_PS1%"
    exit /b %ERRORLEVEL%
)

if /I "%~1"=="build" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SETUP_PS1%" -Build
    exit /b %ERRORLEVEL%
)

if /I "%~1"=="shortcut" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SETUP_PS1%" -CreateShortcut
    exit /b %ERRORLEVEL%
)

if /I "%~1"=="all" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SETUP_PS1%" -All
    exit /b %ERRORLEVEL%
)

echo Usage:
echo   setup.bat build
echo   setup.bat shortcut
echo   setup.bat all
exit /b 1
