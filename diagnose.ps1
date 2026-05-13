# BluetoothAudioReceiver startup diagnostic script

$ErrorActionPreference = "Stop"

$projectPath = $PSScriptRoot
$targetFramework = "net8.0-windows10.0.19041.0"
$runtimeIdentifier = "win-x64"
$releaseDir = Join-Path $projectPath "bin\Release\$targetFramework\$runtimeIdentifier"
$releaseExe = Join-Path $releaseDir "BluetoothAudioReceiver.exe"
$releaseDll = Join-Path $releaseDir "BluetoothAudioReceiver.dll"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "     BluetoothAudioReceiver Startup Diagnostic" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Build Status Check:" -ForegroundColor Yellow
Write-Host ""

if (Test-Path $releaseDll) {
    Write-Host "   [OK] Release version compiled" -ForegroundColor Green
}
else {
    Write-Host "   [FAIL] Release version not compiled" -ForegroundColor Red
}

Write-Host ""
Write-Host "Dependency Check:" -ForegroundColor Yellow
Write-Host ""

$releaseDlls = @()
if (Test-Path $releaseDir) {
    $releaseDlls = Get-ChildItem $releaseDir -Filter "*.dll" -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Name
}

$requiredDlls = @("Hardcodet.NotifyIcon.Wpf.dll", "NAudio.dll")

foreach ($dll in $requiredDlls) {
    if ($releaseDlls -contains $dll) {
        Write-Host "   [OK] $dll (Release)" -ForegroundColor Green
    }
    else {
        Write-Host "   [MISSING] $dll (Release)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "App Icon Check:" -ForegroundColor Yellow
Write-Host ""

$iconPath = Join-Path $projectPath "tech_app.ico"
if (Test-Path $iconPath) {
    $iconSize = (Get-Item $iconPath).Length
    if ($iconSize -gt 0) {
        Write-Host "   [OK] tech_app.ico exists ($iconSize bytes)" -ForegroundColor Green
    }
    else {
        Write-Host "   [FAIL] tech_app.ico is empty" -ForegroundColor Red
    }
}
else {
    Write-Host "   [FAIL] tech_app.ico not found" -ForegroundColor Red
}

Write-Host ""
Write-Host "Recommendations:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Run the Release version:" -ForegroundColor Gray
Write-Host "     & '$releaseExe'" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. If it is missing, rebuild:" -ForegroundColor Gray
Write-Host "     cd '$projectPath'" -ForegroundColor Gray
Write-Host "     .\Setup.ps1 -Build" -ForegroundColor Gray
Write-Host ""
Write-Host "  3. Check Windows Event Viewer for crash details." -ForegroundColor Gray
Write-Host ""
