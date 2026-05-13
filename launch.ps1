# BluetoothAudioReceiver launcher

$ErrorActionPreference = "Stop"

$projectPath = $PSScriptRoot
$targetFramework = "net8.0-windows10.0.19041.0"
$runtimeIdentifier = "win-x64"
$releaseExe = Join-Path $projectPath "bin\Release\$targetFramework\$runtimeIdentifier\BluetoothAudioReceiver.exe"
$debugExe = Join-Path $projectPath "bin\Debug\$targetFramework\$runtimeIdentifier\BluetoothAudioReceiver.exe"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  BluetoothAudioReceiver Launcher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $releaseExe) {
    Write-Host "Starting Release version..." -ForegroundColor Green
    Start-Process -FilePath $releaseExe -WindowStyle Hidden
    Write-Host "Program started." -ForegroundColor Green
    exit 0
}

if (Test-Path $debugExe) {
    Write-Host "Starting Debug version..." -ForegroundColor Yellow
    Start-Process -FilePath $debugExe -WindowStyle Hidden
    Write-Host "Program started." -ForegroundColor Green
    exit 0
}

Write-Host "Error: executable not found." -ForegroundColor Red
Write-Host ""
Write-Host "Build first:" -ForegroundColor Yellow
Write-Host "  cd '$projectPath'" -ForegroundColor Gray
Write-Host "  .\Setup.ps1 -Build" -ForegroundColor Gray
Write-Host ""
exit 1
