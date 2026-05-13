# BluetoothAudioReceiver autostart script
# Manages the same HKCU Run entry used by the in-app "Auto launch" option.

param(
    [switch]$Enable,
    [switch]$Disable,
    [switch]$Status
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$ProjectName = "BluetoothAudioReceiver"
$TargetFramework = "net8.0-windows10.0.19041.0"
$RuntimeIdentifier = "win-x64"
$ReleaseDir = Join-Path $ProjectRoot "bin\Release\$TargetFramework\$RuntimeIdentifier"
$ExePath = Join-Path $ReleaseDir "$ProjectName.exe"
$RunKeyPath = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$RunValue = "`"$ExePath`" -background"

function Show-Usage {
    Write-Host "Usage:"
    Write-Host "  .\AutoStart.ps1 -Enable   Enable HKCU Run autostart"
    Write-Host "  .\AutoStart.ps1 -Disable  Disable HKCU Run autostart"
    Write-Host "  .\AutoStart.ps1 -Status   Show current status"
}

function Get-AutoStartValue {
    $key = Get-ItemProperty -Path $RunKeyPath -Name $ProjectName -ErrorAction SilentlyContinue
    if ($key) { return $key.$ProjectName }
    return $null
}

if (-not $Enable -and -not $Disable -and -not $Status) {
    $Status = $true
}

if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: Executable not found: $ExePath" -ForegroundColor Red
    Write-Host "Run .\Setup.ps1 -Build first."
    exit 1
}

if ($Enable) {
    if (-not (Test-Path $RunKeyPath)) {
        New-Item -Path $RunKeyPath -Force | Out-Null
    }

    Set-ItemProperty -Path $RunKeyPath -Name $ProjectName -Value $RunValue
    Write-Host "Autostart enabled."
    Write-Host "Value: $RunValue"
}

if ($Disable) {
    Remove-ItemProperty -Path $RunKeyPath -Name $ProjectName -ErrorAction SilentlyContinue
    Write-Host "Autostart disabled."
}

if ($Status) {
    $current = Get-AutoStartValue
    if ($current) {
        Write-Host "Autostart: enabled"
        Write-Host "Value: $current"
    }
    else {
        Write-Host "Autostart: disabled"
    }
}

if (-not $Status -and -not $Enable -and -not $Disable) {
    Show-Usage
}
