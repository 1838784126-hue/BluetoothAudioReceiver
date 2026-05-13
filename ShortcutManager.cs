using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BluetoothAudioReceiver
{
    /// <summary>
    /// 快捷方式管理器 - 创建和管理桌面快捷方式
    /// 使用动态 COM 调用，不依赖 COM 引用
    /// </summary>
    public static class ShortcutManager
    {
        // 快捷方式名称
        private const string ShortcutName = "蓝牙音频接收器.lnk";
        
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateShortcut(
            string lpBuf,
            [MarshalAs(UnmanagedType.LPWStr)] string lpTarget
        );
        
        /// <summary>
        /// 创建桌面快捷方式
        /// </summary>
        /// <param name="exePath">可执行文件路径</param>
        /// <param name="description">快捷方式描述</param>
        /// <returns>是否创建成功</returns>
        public static bool CreateDesktopShortcut(string exePath, string description = "蓝牙音频接收器")
        {
            try
            {
                // 获取桌面路径
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, ShortcutName);
                
                // 如果已存在，删除旧的
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                }
                
                // 使用 SHCreateShortcut API 创建快捷方式
                // 格式: "目标路径,图标路径,描述"
                string arguments = $"\"{exePath}\"";
                
                // 创建包含完整属性的快捷方式字符串
                // 格式: 目标路径 + ", " + 图标位置（使用 exe 中的图标） + ", " + 描述
                string shortcutArguments = $"\"{exePath}\", \"{exePath},0\", \"{description}\"";
                
                // 使用 P/Invoke 创建快捷方式
                int result = SHCreateShortcut(shortcutPath, shortcutArguments);
                
                if (result == 0)
                {
                    // 成功创建后，再次设置图标（确保图标正确）
                    SetShortcutIcon(shortcutPath, exePath);
                    return true;
                }
                
                // 如果 API 失败，尝试使用 PowerShell
                return CreateShortcutViaPowerShell(exePath, shortcutPath, description);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShortcutManager] Failed to create shortcut: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 使用 PowerShell 创建快捷方式（备选方案）
        /// </summary>
        private static bool CreateShortcutViaPowerShell(string exePath, string shortcutPath, string description)
        {
            try
            {
                // 创建 WScript.Shell 对象
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                
                dynamic shell = Activator.CreateInstance(shellType)!;
                
                // 创建快捷方式
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                
                // 设置属性
                shortcut.TargetPath = exePath;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.Description = description;
                shortcut.IconLocation = exePath + ",0";  // 使用 exe 中的第 0 个图标
                shortcut.WindowStyle = 1;  // SW_SHOWNORMAL
                
                // 保存
                shortcut.Save();
                
                Marshal.ReleaseComObject(shell);
                Marshal.ReleaseComObject(shortcut);
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShortcutManager] PowerShell shortcut failed: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 设置快捷方式的图标
        /// </summary>
        private static void SetShortcutIcon(string shortcutPath, string iconSourcePath)
        {
            try
            {
                // 创建 WScript.Shell 对象
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                
                dynamic shell = Activator.CreateInstance(shellType)!;
                
                // 打开现有快捷方式
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                
                // 设置图标
                shortcut.IconLocation = iconSourcePath + ",0";
                
                // 保存
                shortcut.Save();
                
                Marshal.ReleaseComObject(shell);
                Marshal.ReleaseComObject(shortcut);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ShortcutManager] Failed to set icon: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 删除桌面快捷方式
        /// </summary>
        /// <returns>是否删除成功</returns>
        public static bool DeleteDesktopShortcut()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, ShortcutName);
                
                if (File.Exists(shortcutPath))
                {
                    File.Delete(shortcutPath);
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 检查桌面快捷方式是否存在
        /// </summary>
        /// <returns>是否存在</returns>
        public static bool ShortcutExists()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string shortcutPath = Path.Combine(desktopPath, ShortcutName);
                return File.Exists(shortcutPath);
            }
            catch
            {
                return false;
            }
        }
    }
}
