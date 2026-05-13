# Verify that the Release executable contains a resource section.

$ErrorActionPreference = "Stop"

$targetFramework = "net8.0-windows10.0.19041.0"
$runtimeIdentifier = "win-x64"
$exePath = Join-Path $PSScriptRoot "bin\Release\$targetFramework\$runtimeIdentifier\BluetoothAudioReceiver.exe"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Icon Embedding Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $exePath)) {
    Write-Host "[ERROR] Executable not found: $exePath" -ForegroundColor Red
    exit 1
}

$fileInfo = Get-Item $exePath
Write-Host "[OK] Executable: $($fileInfo.Name)" -ForegroundColor Green
Write-Host "     Size: $([math]::Round($fileInfo.Length / 1KB, 2)) KB"
Write-Host "     Modified: $($fileInfo.LastWriteTime)"
Write-Host ""

try {
    $bytes = [System.IO.File]::ReadAllBytes($exePath)

    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
        Write-Host "[ERROR] Invalid MZ header." -ForegroundColor Red
        exit 1
    }

    $peOffset = [BitConverter]::ToUInt32($bytes, 60)
    $peHeader = [System.Text.Encoding]::ASCII.GetString($bytes, $peOffset, 4)
    if ($peHeader -ne "PE`0`0") {
        Write-Host "[ERROR] Invalid PE header." -ForegroundColor Red
        exit 1
    }

    $numSections = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
    $resourceRawSize = 0

    for ($i = 0; $i -lt $numSections; $i++) {
        $sectionStart = $peOffset + 24 + ($i * 40)
        $sectionName = [System.Text.Encoding]::ASCII.GetString($bytes, $sectionStart, 8).TrimEnd([char]0)

        if ($sectionName -eq ".rsrc") {
            $resourceRawSize = [BitConverter]::ToUInt32($bytes, $sectionStart + 16)
            Write-Host "[OK] .rsrc section found." -ForegroundColor Green
            Write-Host "     Raw size: $resourceRawSize bytes"
            break
        }
    }

    if ($resourceRawSize -gt 1000) {
        Write-Host ""
        Write-Host "[SUCCESS] Resource section looks valid." -ForegroundColor Green
        exit 0
    }

    Write-Host "[WARN] Resource section is missing or very small." -ForegroundColor Yellow
    exit 1
}
catch {
    Write-Host "[ERROR] Failed to inspect executable: $_" -ForegroundColor Red
    exit 1
}
