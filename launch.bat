@echo off
setlocal enabledelayedexpansion

set "PROJECT_PATH=%~dp0"
set "TARGET_FRAMEWORK=net8.0-windows10.0.19041.0"
set "RUNTIME_IDENTIFIER=win-x64"
set "RELEASE_EXE=%PROJECT_PATH%bin\Release\%TARGET_FRAMEWORK%\%RUNTIME_IDENTIFIER%\BluetoothAudioReceiver.exe"
set "DEBUG_EXE=%PROJECT_PATH%bin\Debug\%TARGET_FRAMEWORK%\%RUNTIME_IDENTIFIER%\BluetoothAudioReceiver.exe"

echo.
echo ========================================
echo  BluetoothAudioReceiver Launcher
echo ========================================
echo.

if exist "%RELEASE_EXE%" (
    echo Starting Release version...
    start "" "%RELEASE_EXE%"
    echo Program started.
    goto :end
)

if exist "%DEBUG_EXE%" (
    echo Starting Debug version...
    start "" "%DEBUG_EXE%"
    echo Program started.
    goto :end
)

echo Error: executable not found.
echo.
echo Build first:
echo   cd "%PROJECT_PATH%"
echo   .\Setup.ps1 -Build
echo.
pause
exit /b 1

:end
echo.
echo If the window does not appear, check the system tray.
echo.
pause
