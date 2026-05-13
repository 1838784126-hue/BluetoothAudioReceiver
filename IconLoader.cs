using System;
using System.Drawing;
using System.IO;

namespace BluetoothAudioReceiver
{
    /// <summary>
    /// 图标加载器 - 安全加载自定义图标，支持回退到系统图标
    /// </summary>
    public static class IconLoader
    {
        // 图标文件名
        private const string IconFileName = "tech_app.ico";
        
        // 缓存已加载的图标
        private static Icon? _cachedIcon = null;
        
        /// <summary>
        /// 获取托盘图标（带安全回退）
        /// </summary>
        /// <returns>Icon 对象，失败时返回系统信息图标</returns>
        public static Icon GetTrayIcon()
        {
            // 如果已经缓存，直接返回
            if (_cachedIcon != null)
            {
                return _cachedIcon;
            }
            
            try
            {
                // 使用绝对路径加载自定义图标
                string icoPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    IconFileName
                );
                
                if (File.Exists(icoPath))
                {
                    // 加载自定义图标并缓存
                    _cachedIcon = new Icon(icoPath);
                    return _cachedIcon;
                }
                else
                {
                    // 文件不存在，使用系统图标并缓存
                    _cachedIcon = SystemIcons.Information;
                    return _cachedIcon;
                }
            }
            catch (Exception ex)
            {
                // 任何异常都回退到系统图标
                Console.WriteLine($"[IconLoader] Failed to load custom icon: {ex.Message}");
                _cachedIcon = SystemIcons.Application;
                return _cachedIcon;
            }
        }
        
        /// <summary>
        /// 检查自定义图标是否存在
        /// </summary>
        /// <returns>是否存在自定义图标文件</returns>
        public static bool IsCustomIconAvailable()
        {
            try
            {
                string icoPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    IconFileName
                );
                return File.Exists(icoPath);
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// 获取图标路径
        /// </summary>
        /// <returns>图标文件的完整路径</returns>
        public static string GetIconPath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                IconFileName
            );
        }
        
        /// <summary>
        /// 清除图标缓存（用于重新加载）
        /// </summary>
        public static void ClearCache()
        {
            if (_cachedIcon != null)
            {
                try
                {
                    _cachedIcon.Dispose();
                }
                catch { }
                _cachedIcon = null;
            }
        }
    }
}
