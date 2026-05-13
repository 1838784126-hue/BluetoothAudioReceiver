# BluetoothAudioReceiver

BluetoothAudioReceiver 是一款 Windows 桌面实用工具，可以让 Windows 电脑作为已配对手机的蓝牙音频接收器使用。它使用 Windows 的 Bluetooth AudioPlaybackConnection API，并提供一个小型 WPF 托盘应用，用于扫描、连接、断开连接和重新连接 A2DP Sink 设备。

## 功能

* 扫描已配对的蓝牙 A2DP Sink 设备。
* 连接或断开选中的手机。
* 自动重新连接上次使用的设备。
* 使用 `-background` 参数时，可隐藏启动到系统托盘。
* 可选的 Windows 开机自启动集成，通过当前用户的 Run 注册表项实现。
* 支持托盘菜单，以及左键点击托盘图标切换窗口显示/隐藏。

## 要求

* 支持蓝牙的 Windows 10/11。
* 用于从源码构建的 .NET 8 SDK。
* 一台已配对的手机，并且该手机向电脑暴露 A2DP Source 连接。
* 如需进行稀疏包注册，请以管理员身份运行 PowerShell，并在 Windows 要求时启用 Windows 开发者模式。

## 构建

```powershell
.\Setup.ps1 -Build
```

标准 Release 输出目录为：

```text
bin\Release\net8.0-windows10.0.19041.0\win-x64
```

也可以一步完成构建并创建桌面快捷方式：

```powershell
.\Setup.ps1 -All
```

## 运行

```powershell
.\launch.ps1
```

隐藏启动到系统托盘：

```powershell
.\bin\Release\net8.0-windows10.0.19041.0\win-x64\BluetoothAudioReceiver.exe -background
```

## 开机自启动

启用、查看或禁用开机自启动：

```powershell
.\AutoStart.ps1 -Enable
.\AutoStart.ps1 -Status
.\AutoStart.ps1 -Disable
```

## 稀疏包注册

部分蓝牙接收器 API 需要包标识。请先完成构建，然后在提升权限的 PowerShell 窗口中运行：

```powershell
.\RegisterPackage.ps1
```

## 诊断

```powershell
.\diagnose.ps1
```

## 项目结构

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
