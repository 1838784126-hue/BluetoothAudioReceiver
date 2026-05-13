# BluetoothAudioReceiver setup script
# Builds the canonical Release output and creates a desktop shortcut.

param(
    [switch]$Build,
    [switch]$CreateShortcut,
    [switch]$All
)

$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$ProjectName = "BluetoothAudioReceiver"
$TargetFramework = "net8.0-windows10.0.19041.0"
$RuntimeIdentifier = "win-x64"
$ReleaseDir = Join-Path $ProjectRoot "bin\Release\$TargetFramework\$RuntimeIdentifier"
$ExePath = Join-Path $ReleaseDir "$ProjectName.exe"
$IconPath = Join-Path $ReleaseDir "tech_app.ico"
$DesktopPath = [Environment]::GetFolderPath("Desktop")
$ShortcutPath = Join-Path $DesktopPath "BluetoothAudioReceiver.lnk"

function Get-RunningReleaseProcess {
    Get-CimInstance Win32_Process -Filter "Name='$ProjectName.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -and ($_.ExecutablePath -ieq $ExePath) }
}

function Show-Usage {
    Write-Host "Usage:"
    Write-Host "  .\Setup.ps1 -Build           Build Release output"
    Write-Host "  .\Setup.ps1 -CreateShortcut  Create desktop shortcut"
    Write-Host "  .\Setup.ps1 -All             Build and create shortcut"
}

if ($All) {
    $Build = $true
    $CreateShortcut = $true
}

if (-not $Build -and -not $CreateShortcut) {
    Show-Usage
    exit 0
}

Write-Host "BluetoothAudioReceiver setup"
Write-Host "Release directory: $ReleaseDir"
Write-Host ""

if ($Build) {
    Write-Host "Building Release output..."

    $running = Get-RunningReleaseProcess
    if ($running) {
        Write-Host "ERROR: Release executable is currently running and cannot be overwritten." -ForegroundColor Red
        Write-Host "Close the tray app first, then retry. Running process:"
        $running | ForEach-Object {
            Write-Host "  PID $($_.ProcessId): $($_.ExecutablePath)"
        }
        exit 1
    }

    $dotnetVersion = dotnet --version 2>$null
    if (-not $dotnetVersion) {
        Write-Host "ERROR: .NET SDK was not found. Install .NET 8 SDK first." -ForegroundColor Red
        exit 1
    }

    Write-Host ".NET SDK: $dotnetVersion"

    Push-Location $ProjectRoot
    try {
        dotnet publish -c Release -r $RuntimeIdentifier -o $ReleaseDir --self-contained false
        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Build failed." -ForegroundColor Red
            exit 1
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Build succeeded."
    Write-Host ""
}

if ($CreateShortcut) {
    Write-Host "Creating desktop shortcut..."

    if (-not (Test-Path $ExePath)) {
        Write-Host "ERROR: Executable not found: $ExePath" -ForegroundColor Red
        Write-Host "Run .\Setup.ps1 -Build first."
        exit 1
    }

    $shortcutIcon = if (Test-Path $IconPath) { $IconPath } else { "$ExePath,0" }
    $wshShell = New-Object -ComObject WScript.Shell
    $shortcut = $wshShell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $ExePath
    $shortcut.WorkingDirectory = $ReleaseDir
    $shortcut.Description = "Bluetooth Audio Receiver"
    $shortcut.IconLocation = $shortcutIcon
    $shortcut.Save()

    [Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
    [Runtime.InteropServices.Marshal]::ReleaseComObject($wshShell) | Out-Null

    Write-Host "Shortcut created: $ShortcutPath"
}
