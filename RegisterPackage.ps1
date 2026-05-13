# Registers the sparse package identity for BluetoothAudioReceiver.
# Run this script from an elevated PowerShell window.

param(
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$TargetFramework = "net8.0-windows10.0.19041.0"
$RuntimeIdentifier = "win-x64"
$ExePath = Join-Path $PSScriptRoot "bin\Release\$TargetFramework\$RuntimeIdentifier\BluetoothAudioReceiver.exe"
$ManifestPath = Join-Path $PSScriptRoot "Package.appxmanifest"

function Pause-IfNeeded {
    if (-not $NoPause) {
        Write-Host ""
        Write-Host "Press any key to exit..."
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

Write-Host "BluetoothAudioReceiver package identity registration"
Write-Host ""

if (-not (Test-IsAdministrator)) {
    Write-Host "ERROR: Run this script from an elevated PowerShell window." -ForegroundColor Red
    Pause-IfNeeded
    exit 1
}

if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: Release executable not found: $ExePath" -ForegroundColor Red
    Write-Host "Run .\Setup.ps1 -Build first."
    Pause-IfNeeded
    exit 1
}

if (-not (Test-Path $ManifestPath)) {
    Write-Host "ERROR: Package.appxmanifest not found: $ManifestPath" -ForegroundColor Red
    Pause-IfNeeded
    exit 1
}

try {
    $existing = Get-AppxPackage -Name "BluetoothAudioReceiver" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Removing existing package identity..."
        Remove-AppxPackage $existing.PackageFullName -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    $exeDir = Split-Path $ExePath -Parent
    Write-Host "Registering sparse package..."
    Write-Host "EXE:      $ExePath"
    Write-Host "Manifest: $ManifestPath"

    Add-AppxPackage -Path $ManifestPath -ExternalLocation $exeDir -AllowUnsigned -ErrorAction Stop

    Write-Host ""
    Write-Host "Package identity registration succeeded."
    Write-Host "You can now start: $ExePath"
}
catch {
    Write-Host "ERROR: Package registration failed." -ForegroundColor Red
    Write-Host $_
    Write-Host ""
    Write-Host "Common fixes:"
    Write-Host "  1. Run PowerShell as Administrator."
    Write-Host "  2. Enable Windows Developer Mode."
}
finally {
    Pause-IfNeeded
}
