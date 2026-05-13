# BluetoothAudioReceiver

BluetoothAudioReceiver is a Windows desktop utility that helps a Windows PC act as a Bluetooth audio receiver for paired phones. It uses the Windows Bluetooth AudioPlaybackConnection API and provides a small WPF tray app for scanning, connecting, disconnecting, and reconnecting A2DP sink devices.

## Features

- Scan paired Bluetooth A2DP sink devices.
- Connect or disconnect the selected phone.
- Reconnect the last used device automatically.
- Start hidden in the tray with `-background`.
- Optional Windows startup integration through the current user's Run key.
- Tray menu and tray left-click window toggle.

## Requirements

- Windows 10/11 with Bluetooth support.
- .NET 8 SDK for building from source.
- A paired phone that exposes an A2DP source connection to the PC.
- For sparse package registration, run PowerShell as Administrator and enable Windows Developer Mode if Windows requires it.

## Build

```powershell
.\Setup.ps1 -Build
```

The standard Release output is:

```text
bin\Release\net8.0-windows10.0.19041.0\win-x64
```

You can also build and create a desktop shortcut in one step:

```powershell
.\Setup.ps1 -All
```

## Run

```powershell
.\launch.ps1
```

Start hidden in the tray:

```powershell
.\bin\Release\net8.0-windows10.0.19041.0\win-x64\BluetoothAudioReceiver.exe -background
```

## Startup

Enable, inspect, or disable startup:

```powershell
.\AutoStart.ps1 -Enable
.\AutoStart.ps1 -Status
.\AutoStart.ps1 -Disable
```

## Sparse Package Registration

Some Bluetooth receiver APIs require package identity. Build first, then run an elevated PowerShell window:

```powershell
.\RegisterPackage.ps1
```

## Diagnostics

```powershell
.\diagnose.ps1
```

## Project Structure

```text
BluetoothAudioReceiver/
|- App.xaml
|- App.xaml.cs
|- MainWindow.xaml
|- MainWindow.xaml.cs
|- Services/
|  `- BluetoothService.cs
|- Assets/
|- Setup.ps1
|- AutoStart.ps1
|- RegisterPackage.ps1
`- BluetoothAudioReceiver.csproj
```

## Notes

Windows Bluetooth A2DP sink support depends on the local Bluetooth adapter, driver, Windows version, and package identity state. If a paired phone is not listed, remove and pair it again in Windows Bluetooth settings, then rerun the scan.
