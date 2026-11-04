using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Management;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace PcManagerLite
{
    internal static class AppPaths
    {
        public static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PcManagerLite");
            }
        }

        public static string BackupFile
        {
            get { return Path.Combine(DataDirectory, "settings-backup.txt"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(DataDirectory, "manager.log"); }
        }

        public static string CleanupLogFile
        {
            get
            {
                return Path.Combine(
                    DataDirectory,
                    "cleanup-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
        }
    }

    internal static class FileLogger
    {
        private static readonly object SyncRoot = new object();

        public static void Write(string message)
        {
            try
            {
                lock (SyncRoot)
                {
                    if (!Directory.Exists(AppPaths.DataDirectory))
                    {
                        Directory.CreateDirectory(AppPaths.DataDirectory);
                    }
                    if (File.Exists(AppPaths.LogFile) &&
                        new FileInfo(AppPaths.LogFile).Length > 2L * 1024L * 1024L)
                    {
                        File.Delete(AppPaths.LogFile);
                    }
                    File.AppendAllText(
                        AppPaths.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                        message + "\r\n\r\n",
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }

        public static void WriteCleanupAuditBatch(string details)
        {
            if (String.IsNullOrWhiteSpace(details))
            {
                return;
            }
            try
            {
                lock (SyncRoot)
                {
                    if (!Directory.Exists(AppPaths.DataDirectory))
                    {
                        Directory.CreateDirectory(AppPaths.DataDirectory);
                    }
                    File.AppendAllText(
                        AppPaths.CleanupLogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        " cleanup start\r\n" + details +
                        "cleanup end\r\n\r\n",
                        Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class MemoryMetrics
    {
        public long TotalPhysical;
        public long AvailablePhysical;
        public double UsedPercent
        {
            get
            {
                if (TotalPhysical <= 0)
                {
                    return 0;
                }
                return (TotalPhysical - AvailablePhysical) * 100.0 / TotalPhysical;
            }
        }
    }

    internal static class NativeSystem
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(
            IntPtr process,
            IntPtr minimumWorkingSetSize,
            IntPtr maximumWorkingSetSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(
            IntPtr hwnd,
            int message,
            IntPtr wParam,
            string lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(
            string rootPath,
            ref SHQUERYRBINFO queryInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(
            IntPtr hwnd,
            string rootPath,
            uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int valueSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr hwnd,
            string subApplicationName,
            string subIdList);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public static MemoryMetrics GetMemory()
        {
            try
            {
                MEMORYSTATUSEX status = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(status))
                {
                    return null;
                }
                MemoryMetrics metrics = new MemoryMetrics();
                metrics.TotalPhysical = (long)Math.Min(status.ullTotalPhys, (ulong)Int64.MaxValue);
                metrics.AvailablePhysical =
                    (long)Math.Min(status.ullAvailPhys, (ulong)Int64.MaxValue);
                return metrics;
            }
            catch
            {
                return null;
            }
        }

        public static void TrimCurrentProcessWorkingSet()
        {
            try
            {
                SetProcessWorkingSetSize(new IntPtr(-1), new IntPtr(-1), new IntPtr(-1));
            }
            catch
            {
            }
        }

        public static void ApplyWindowTheme(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            try
            {
                int enabled = dark ? 1 : 0;
                int result = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(hwnd, 19, ref enabled, sizeof(int));
                }
            }
            catch
            {
            }
        }

        public static void SetCueBanner(IntPtr hwnd, string text)
        {
            if (hwnd == IntPtr.Zero || String.IsNullOrWhiteSpace(text))
            {
                return;
            }
            try
            {
                SendMessage(hwnd, 0x1501, (IntPtr)1, text);
            }
            catch
            {
            }
        }

        public static void ApplyControlTheme(IntPtr hwnd, bool dark, bool combo)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            try
            {
                string theme = dark
                    ? combo ? "DarkMode_CFD" : "DarkMode_Explorer"
                    : null;
                SetWindowTheme(hwnd, theme, null);
            }
            catch
            {
            }
        }

        public static RecycleBinInfo QueryRecycleBin()
        {
            RecycleBinInfo result = new RecycleBinInfo();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    {
                        continue;
                    }
                    SHQUERYRBINFO info = new SHQUERYRBINFO();
                    info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                    if (SHQueryRecycleBin(drive.RootDirectory.FullName, ref info) == 0)
                    {
                        result.SizeBytes += info.i64Size;
                        result.ItemCount += info.i64NumItems;
                    }
                }
                catch
                {
                }
            }
            return result;
        }

        public static void EmptyRecycleBin()
        {
            const uint noConfirmation = 0x00000001;
            const uint noProgress = 0x00000002;
            const uint noSound = 0x00000004;
            int code = SHEmptyRecycleBin(
                IntPtr.Zero,
                null,
                noConfirmation | noProgress | noSound);
            if (code != 0 && code != 1 && code != -2147418113)
            {
                throw new InvalidOperationException("清空回收站失败，系统返回码: " + code);
            }
        }

        public static int GetForegroundProcessId()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                return (int)processId;
            }
            catch
            {
                return 0;
            }
        }

        public static bool IsWindowFullscreen(IntPtr window)
        {
            try
            {
                RECT windowRect;
                if (!GetWindowRect(window, out windowRect))
                {
                    return false;
                }
                IntPtr monitor = MonitorFromWindow(window, 2);
                MONITORINFO info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref info))
                {
                    return false;
                }
                int tolerance = 3;
                return Math.Abs(windowRect.Left - info.rcMonitor.Left) <= tolerance &&
                    Math.Abs(windowRect.Top - info.rcMonitor.Top) <= tolerance &&
                    Math.Abs(windowRect.Right - info.rcMonitor.Right) <= tolerance &&
                    Math.Abs(windowRect.Bottom - info.rcMonitor.Bottom) <= tolerance;
            }
            catch
            {
                return false;
            }
        }

        public static IntPtr ForegroundWindow()
        {
            return GetForegroundWindow();
        }

        public static void ActivateWindow(IntPtr window)
        {
            if (window == IntPtr.Zero)
            {
                return;
            }
            try
            {
                ShowWindow(window, 9);
                SetForegroundWindow(window);
            }
            catch
            {
            }
        }
    }

    internal sealed class RecycleBinInfo
    {
        public long SizeBytes;
        public long ItemCount;
    }

    internal sealed class CleanupCategory
    {
        public string Id;
        public string Title;
        public string Description;
        public string Recommendation = "建议清理";
        public string RecommendationNote = "可安全清理，删除后会自动重建或重新下载。";
        public bool DefaultSelected;
        public bool IsRecycleBin;
        public bool TopLevelPatterns;
        public int MinimumAgeHours = 24;
        public string[] Patterns;
        public readonly List<string> Roots = new List<string>();
        public long SizeBytes;
        public long FileCount;
        public int AccessErrors;
        public bool BrowserRunning;

        public CleanupCategory(
            string id,
            string title,
            string description,
            bool defaultSelected)
        {
            Id = id;
            Title = title;
            Description = description;
            DefaultSelected = defaultSelected;
            Patterns = new string[0];
        }
    }

    internal sealed class CleanupScanResult
    {
        public readonly List<CleanupCategory> Categories = new List<CleanupCategory>();
        public long TotalBytes;
        public long TotalFiles;
        public DateTime CompletedAt = DateTime.Now;
        public string Error;
    }

    internal sealed class CleanupResult
    {
        public long DeletedBytes;
        public long DeletedFiles;
        public int FailedItems;
        public int SkippedTooNew;
        public int SkippedUnsafe;
        public bool RecycleBinEmptied;
        public string Error;
        public readonly StringBuilder AuditDetails = new StringBuilder();
    }

    internal static class CleanupScanner
    {
        public static CleanupScanResult ScanAll(Action<string> progress)
        {
            CleanupScanResult result = new CleanupScanResult();
            List<CleanupCategory> categories = BuildCategories();
            try
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    CleanupCategory category = categories[i];
                    if (progress != null)
                    {
                        progress(String.Format(
                            "正在扫描 {0}（{1}/{2}）",
                            category.Title,
                            i + 1,
                            categories.Count));
                    }

                    if (category.IsRecycleBin)
                    {
                        RecycleBinInfo recycle = NativeSystem.QueryRecycleBin();
                        category.SizeBytes = recycle.SizeBytes;
                        category.FileCount = recycle.ItemCount;
                    }
                    else
                    {
                        foreach (string root in category.Roots)
                        {
                            MeasureDirectory(
                                root,
                                category.Patterns,
                                category.TopLevelPatterns,
                                category.MinimumAgeHours,
                                category);
                        }
                    }
                    result.TotalBytes += category.SizeBytes;
                    result.TotalFiles += category.FileCount;
                    result.Categories.Add(category);
                }
                result.CompletedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("垃圾扫描失败: " + ex);
            }
            return result;
        }

        public static CleanupResult CleanSelected(
            List<CleanupCategory> categories,
            Action<string> progress)
        {
            CleanupResult result = new CleanupResult();
            try
            {
                for (int i = 0; i < categories.Count; i++)
                {
                    CleanupCategory category = categories[i];
                    if (progress != null)
                    {
                        progress(String.Format(
                            "正在清理 {0}（{1}/{2}）",
                            category.Title,
                            i + 1,
                            categories.Count));
                    }
                    if (category.IsRecycleBin)
                    {
                        NativeSystem.EmptyRecycleBin();
                        result.RecycleBinEmptied = true;
                        result.DeletedBytes += category.SizeBytes;
                        result.DeletedFiles += category.FileCount;
                        continue;
                    }

                    foreach (string root in category.Roots)
                    {
                        DeleteDirectoryContents(
                            root,
                            category.Patterns,
                            category.TopLevelPatterns,
                            category.MinimumAgeHours,
                            result);
                    }
                }
                FileLogger.Write(String.Format(
                    "垃圾清理完成: 文件 {0}, 大小 {1}, 失败 {2}",
                    result.DeletedFiles,
                    result.DeletedBytes,
                    result.FailedItems));
                FileLogger.WriteCleanupAuditBatch(result.AuditDetails.ToString());
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("垃圾清理失败: " + ex);
            }
            return result;
        }

        internal static bool RunSafeDeleteSelfTest(out string details)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "PcManagerLiteSelfTest_" + Guid.NewGuid().ToString("N"));
            details = String.Empty;
            try
            {
                Directory.CreateDirectory(root);
                string nested = Path.Combine(root, "nested");
                Directory.CreateDirectory(nested);
                string first = Path.Combine(root, "one.tmp");
                string second = Path.Combine(nested, "two.tmp");
                File.WriteAllText(first, "one");
                File.WriteAllText(second, "two");

                CleanupResult result = new CleanupResult();
                File.SetCreationTimeUtc(first, DateTime.UtcNow.AddDays(-2));
                File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-2));
                File.SetCreationTimeUtc(second, DateTime.UtcNow.AddDays(-2));
                File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddDays(-2));
                DeleteDirectoryContents(root, new string[0], false, 24, result);
                bool passed =
                    Directory.Exists(root) &&
                    !File.Exists(first) &&
                    !File.Exists(second) &&
                    result.DeletedFiles == 2;
                details = String.Format(
                    "rootExists={0}, firstExists={1}, secondExists={2}, deleted={3}",
                    Directory.Exists(root),
                    File.Exists(first),
                    File.Exists(second),
                    result.DeletedFiles);
                return passed;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static List<CleanupCategory> BuildCategories()
        {
            List<CleanupCategory> categories = new List<CleanupCategory>();

            CleanupCategory temp = new CleanupCategory(
                "user-temp",
                "用户临时文件",
                "应用安装、解压和运行时产生的临时文件",
                true);
            AddUniqueDirectory(temp.Roots, Path.GetTempPath());
            AddUniqueDirectory(
                temp.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Temp"));
            categories.Add(temp);

            CleanupCategory windowsTemp = new CleanupCategory(
                "windows-temp",
                "Windows 临时文件",
                "系统组件运行时留下的临时内容",
                true);
            AddUniqueDirectory(
                windowsTemp.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "Temp"));
            categories.Add(windowsTemp);

            CleanupCategory updateCache = new CleanupCategory(
                "windows-update-cache",
                "Windows 更新下载缓存",
                "已下载的更新载荷；正在使用的更新文件会自动跳过",
                true);
            AddUniqueDirectory(
                updateCache.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"SoftwareDistribution\Download"));
            categories.Add(updateCache);

            CleanupCategory crashes = new CleanupCategory(
                "crash-reports",
                "崩溃转储与错误报告",
                "应用崩溃后保留的诊断文件，不影响程序运行",
                true);
            AddUniqueDirectory(
                crashes.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CrashDumps"));
            AddUniqueDirectory(
                crashes.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\WER\ReportQueue"));
            AddUniqueDirectory(
                crashes.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows\WER\ReportArchive"));
            categories.Add(crashes);

            CleanupCategory thumbnails = new CleanupCategory(
                "thumbnail-cache",
                "缩略图缓存",
                "资源管理器缩略图数据库，删除后会自动重建",
                true);
            AddUniqueDirectory(
                thumbnails.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Windows\Explorer"));
            thumbnails.TopLevelPatterns = true;
            thumbnails.Patterns = new string[] { "thumbcache_*.db", "iconcache_*.db" };
            categories.Add(thumbnails);

            CleanupCategory directX = new CleanupCategory(
                "directx-shader-cache",
                "DirectX 着色器缓存",
                "驱动更新后清除有助于修复着色器卡顿；下次启动游戏会重新编译",
                false);
            directX.MinimumAgeHours = 0;
            AddUniqueDirectory(
                directX.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "D3DSCache"));
            categories.Add(directX);

            CleanupCategory gpuCaches = new CleanupCategory(
                "gpu-shader-caches",
                "显卡着色器缓存",
                "NVIDIA/AMD/Intel 可重建缓存；首次进入游戏可能短暂重新编译",
                false);
            gpuCaches.MinimumAgeHours = 0;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"NVIDIA\DXCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"NVIDIA\GLCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"NVIDIA\OptixCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(roaming, @"NVIDIA\ComputeCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"AMD\DxCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"AMD\DxcCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"AMD\GLCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"AMD\VkCache"));
            AddUniqueDirectory(gpuCaches.Roots, Path.Combine(local, @"Intel\ShaderCache"));
            categories.Add(gpuCaches);

            bool edgeRunning = IsProcessRunning("msedge");
            CleanupCategory edge = new CleanupCategory(
                "edge-cache",
                "Microsoft Edge 缓存",
                edgeRunning
                    ? "Edge 正在运行，默认不勾选；关闭浏览器后清理更完整"
                    : "网页图片和脚本缓存，不包含密码、书签和历史记录",
                !edgeRunning);
            edge.BrowserRunning = edgeRunning;
            AddBrowserCacheRoots(
                edge.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\User Data"));
            categories.Add(edge);

            bool chromeRunning = IsProcessRunning("chrome");
            CleanupCategory chrome = new CleanupCategory(
                "chrome-cache",
                "Google Chrome 缓存",
                chromeRunning
                    ? "Chrome 正在运行，默认不勾选；关闭浏览器后清理更完整"
                    : "网页图片和脚本缓存，不包含密码、书签和历史记录",
                !chromeRunning);
            chrome.BrowserRunning = chromeRunning;
            AddBrowserCacheRoots(
                chrome.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data"));
            categories.Add(chrome);

            CleanupCategory systemLogs = new CleanupCategory(
                "system-logs",
                "系统日志与安装记录",
                "CBS、DISM、Windows 更新等诊断日志；排查系统问题时才需要",
                true);
            systemLogs.MinimumAgeHours = 24;
            systemLogs.TopLevelPatterns = true;
            systemLogs.Patterns = new string[] { "*.log", "*.etl", "*.cab" };
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            AddUniqueDirectory(systemLogs.Roots, Path.Combine(windows, @"Logs\CBS"));
            AddUniqueDirectory(systemLogs.Roots, Path.Combine(windows, @"Logs\DISM"));
            AddUniqueDirectory(systemLogs.Roots, Path.Combine(windows, @"Logs\WindowsUpdate"));
            AddUniqueDirectory(systemLogs.Roots, Path.Combine(windows, @"Logs\SIH"));
            AddUniqueDirectory(systemLogs.Roots, Path.Combine(windows, @"Logs\waasmedic"));
            AddUniqueDirectory(systemLogs.Roots, Path.Combine(windows, @"Panther"));
            categories.Add(systemLogs);

            CleanupCategory deliveryOptimization = new CleanupCategory(
                "delivery-optimization",
                "传递优化缓存",
                "Windows 更新和商店的分发缓存，删除后系统会按需重新下载",
                true);
            AddUniqueDirectory(
                deliveryOptimization.Roots,
                Path.Combine(windows, @"SoftwareDistribution\DeliveryOptimization"));
            AddUniqueDirectory(
                deliveryOptimization.Roots,
                Path.Combine(
                    windows,
                    @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"));
            categories.Add(deliveryOptimization);

            CleanupCategory memoryDumps = new CleanupCategory(
                "memory-dumps",
                "内存转储文件",
                "蓝屏或程序崩溃生成的转储；删除后无法再分析那次崩溃",
                true);
            memoryDumps.MinimumAgeHours = 24;
            memoryDumps.TopLevelPatterns = true;
            memoryDumps.Patterns = new string[] { "*.dmp" };
            AddUniqueDirectory(memoryDumps.Roots, Path.Combine(windows, "Minidump"));
            categories.Add(memoryDumps);

            bool firefoxRunning = IsProcessRunning("firefox");
            CleanupCategory firefox = new CleanupCategory(
                "firefox-cache",
                "Mozilla Firefox 缓存",
                firefoxRunning
                    ? "Firefox 正在运行，默认不勾选；关闭浏览器后清理更完整"
                    : "网页图片和脚本缓存，不包含密码、书签和历史记录",
                !firefoxRunning);
            firefox.BrowserRunning = firefoxRunning;
            AddMozillaCacheRoots(firefox.Roots, Path.Combine(local, @"Mozilla\Firefox\Profiles"));
            categories.Add(firefox);

            CleanupCategory developerCaches = new CleanupCategory(
                "developer-caches",
                "开发工具缓存",
                "pip、npm、NuGet、Yarn 的下载缓存；下次安装依赖时会重新下载",
                false);
            AddUniqueDirectory(developerCaches.Roots, Path.Combine(local, @"pip\Cache"));
            AddUniqueDirectory(
                developerCaches.Roots,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "npm-cache"));
            AddUniqueDirectory(developerCaches.Roots, Path.Combine(local, @"NuGet\v3-cache"));
            AddUniqueDirectory(developerCaches.Roots, Path.Combine(local, @"Yarn\Cache"));
            categories.Add(developerCaches);

            CleanupCategory windowsOld = new CleanupCategory(
                "windows-old",
                "旧版 Windows 安装文件",
                "升级后保留的回滚文件，体积通常很大；删除后无法回退到旧版本",
                false);
            windowsOld.MinimumAgeHours = 0;
            AddUniqueDirectory(windowsOld.Roots, Path.Combine(@"C:\", "Windows.old"));
            categories.Add(windowsOld);

            CleanupCategory recycle = new CleanupCategory(
                "recycle-bin",
                "回收站",
                "已删除但尚未永久清除的文件；清理后无法恢复",
                false);
            recycle.IsRecycleBin = true;
            categories.Add(recycle);

            foreach (CleanupCategory category in categories)
            {
                ApplyRecommendation(category);
            }
            return categories;
        }

        private static void ApplyRecommendation(CleanupCategory category)
        {
            switch (category.Id)
            {
                case "directx-shader-cache":
                case "gpu-shader-caches":
                    category.Recommendation = "谨慎清理";
                    category.RecommendationNote =
                        "首次进入游戏会重新编译着色器，可能短暂卡顿；只在排查卡顿时清理。";
                    break;
                case "recycle-bin":
                    category.Recommendation = "谨慎清理";
                    category.RecommendationNote =
                        "删除后无法恢复，请先确认回收站里没有需要的文件。";
                    break;
                case "windows-old":
                    category.Recommendation = "不建议清理";
                    category.RecommendationNote =
                        "这是系统回滚文件；确认不再退回旧版本时再手动勾选。";
                    break;
                case "developer-caches":
                    category.Recommendation = "谨慎清理";
                    category.RecommendationNote =
                        "会删除 pip/npm/NuGet/Yarn 下载缓存，下次安装依赖需要重新下载。";
                    break;
                case "memory-dumps":
                    category.Recommendation = "建议清理";
                    category.RecommendationNote =
                        "只有需要分析蓝屏原因时才保留这些转储文件。";
                    break;
                default:
                    if (category.BrowserRunning)
                    {
                        category.Recommendation = "谨慎清理";
                        category.RecommendationNote =
                            "浏览器正在运行，建议关闭浏览器后再清理，否则部分文件会被跳过。";
                    }
                    break;
            }
        }

        private static void AddBrowserCacheRoots(List<string> roots, string userData)
        {
            if (!Directory.Exists(userData))
            {
                return;
            }
            try
            {
                foreach (string profile in Directory.GetDirectories(userData))
                {
                    string name = Path.GetFileName(profile);
                    if (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))
                    {
                        AddUniqueDirectory(roots, Path.Combine(profile, "Cache"));
                        AddUniqueDirectory(roots, Path.Combine(profile, "Code Cache"));
                        AddUniqueDirectory(roots, Path.Combine(profile, "GPUCache"));
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddMozillaCacheRoots(List<string> roots, string profilesRoot)
        {
            if (!Directory.Exists(profilesRoot))
            {
                return;
            }
            try
            {
                foreach (string profile in Directory.GetDirectories(profilesRoot))
                {
                    AddUniqueDirectory(roots, Path.Combine(profile, "cache2"));
                    AddUniqueDirectory(roots, Path.Combine(profile, "startupCache"));
                    AddUniqueDirectory(roots, Path.Combine(profile, "thumbnails"));
                }
            }
            catch
            {
            }
        }

        public static CleanupResult CleanCategories(
            string[] categoryIds,
            Action<string> progress)
        {
            List<CleanupCategory> selected = new List<CleanupCategory>();
            if (categoryIds != null && categoryIds.Length > 0)
            {
                foreach (CleanupCategory category in BuildCategories())
                {
                    foreach (string id in categoryIds)
                    {
                        if (String.Equals(
                            category.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            selected.Add(category);
                            break;
                        }
                    }
                }
            }
            return CleanSelected(selected, progress);
        }

        private static void AddUniqueDirectory(List<string> roots, string path)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    return;
                }
                string full = Path.GetFullPath(path).TrimEnd('\\');
                foreach (string existing in roots)
                {
                    if (String.Equals(existing, full, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                roots.Add(full);
            }
            catch
            {
            }
        }

        private static bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void MeasureDirectory(
            string root,
            string[] patterns,
            bool topLevelOnly,
            int minimumAgeHours,
            CleanupCategory category)
        {
            try
            {
                if (!Directory.Exists(root) || IsReparsePoint(root))
                {
                    return;
                }
                string normalizedRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
                if (patterns != null && patterns.Length > 0 && topLevelOnly)
                {
                    foreach (string pattern in patterns)
                    {
                        foreach (string file in Directory.GetFiles(root, pattern))
                        {
                            MeasureFile(file, minimumAgeHours, category);
                        }
                    }
                    return;
                }

                Stack<string> pending = new Stack<string>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    string directory = pending.Pop();
                    try
                    {
                        foreach (string file in Directory.GetFiles(directory))
                        {
                            string full = Path.GetFullPath(file);
                            if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            MeasureFile(full, minimumAgeHours, category);
                        }
                        foreach (string child in Directory.GetDirectories(directory))
                        {
                            if (IsReparsePoint(child))
                            {
                                continue;
                            }
                            pending.Push(child);
                        }
                    }
                    catch
                    {
                        category.AccessErrors++;
                    }
                }
            }
            catch
            {
                category.AccessErrors++;
            }
        }

        private static void MeasureFile(
            string file,
            int minimumAgeHours,
            CleanupCategory category)
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists || IsUnsafeFile(info.Attributes))
                {
                    return;
                }
                DateTime cutoff = DateTime.UtcNow.AddHours(-minimumAgeHours);
                DateTime effective = info.LastWriteTimeUtc > info.CreationTimeUtc
                    ? info.LastWriteTimeUtc
                    : info.CreationTimeUtc;
                if (minimumAgeHours > 0 && effective > cutoff)
                {
                    return;
                }
                category.SizeBytes += info.Length;
                category.FileCount++;
            }
            catch
            {
                category.AccessErrors++;
            }
        }

        private static void DeleteDirectoryContents(
            string root,
            string[] patterns,
            bool topLevelOnly,
            int minimumAgeHours,
            CleanupResult result)
        {
            if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || IsReparsePoint(root))
            {
                return;
            }

            string normalizedRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (patterns != null && patterns.Length > 0 && topLevelOnly)
            {
                foreach (string pattern in patterns)
                {
                    try
                    {
                        foreach (string file in Directory.GetFiles(root, pattern))
                        {
                            DeleteSingleFile(file, normalizedRoot, minimumAgeHours, result);
                        }
                    }
                    catch
                    {
                        result.FailedItems++;
                    }
                }
                return;
            }

            List<string> directories = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                directories.Add(directory);
                try
                {
                    foreach (string file in Directory.GetFiles(directory))
                    {
                        DeleteSingleFile(file, normalizedRoot, minimumAgeHours, result);
                    }
                    foreach (string child in Directory.GetDirectories(directory))
                    {
                        if (IsReparsePoint(child))
                        {
                            continue;
                        }
                        pending.Push(child);
                    }
                }
                catch
                {
                    result.FailedItems++;
                }
            }

            directories.Sort(delegate(string left, string right)
            {
                return right.Length.CompareTo(left.Length);
            });
            foreach (string directory in directories)
            {
                try
                {
                    if (!PathsEqual(directory, root))
                    {
                        Directory.Delete(directory, false);
                    }
                }
                catch
                {
                }
            }
        }

        private static void DeleteSingleFile(
            string file,
            string normalizedRoot,
            int minimumAgeHours,
            CleanupResult result)
        {
            try
            {
                string full = Path.GetFullPath(file);
                if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    result.FailedItems++;
                    return;
                }
                FileInfo info = new FileInfo(full);
                if (!info.Exists)
                {
                    return;
                }
                if (IsUnsafeFile(info.Attributes))
                {
                    result.SkippedUnsafe++;
                    return;
                }
                DateTime cutoff = DateTime.UtcNow.AddHours(-minimumAgeHours);
                DateTime effective = info.LastWriteTimeUtc > info.CreationTimeUtc
                    ? info.LastWriteTimeUtc
                    : info.CreationTimeUtc;
                if (minimumAgeHours > 0 && effective > cutoff)
                {
                    result.SkippedTooNew++;
                    return;
                }
                long size = info.Length;
                info.Attributes = FileAttributes.Normal;
                File.Delete(full);
                result.AuditDetails.AppendLine(
                    "DEL  " + size.ToString().PadLeft(12) + "  " + full);
                result.DeletedFiles++;
                result.DeletedBytes += size;
            }
            catch
            {
                result.FailedItems++;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return true;
            }
        }

        private static bool IsUnsafeFile(FileAttributes attributes)
        {
            int raw = (int)attributes;
            const int recallOnDataAccess = 0x00400000;
            const int recallOnOpen = 0x00040000;
            return (attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Offline) != 0 ||
                (raw & recallOnDataAccess) != 0 ||
                (raw & recallOnOpen) != 0;
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return String.Equals(
                    Path.GetFullPath(left).TrimEnd('\\'),
                    Path.GetFullPath(right).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class NetworkReport
    {
        public bool Available;
        public bool Tested;
        public string AdapterName = "未检测到";
        public string LinkSpeed = "-";
        public string LocalAddress = "-";
        public string Gateway = "-";
        public string DnsServers = "-";
        public double GatewayLatencyMs;
        public double InternetLatencyMs;
        public double JitterMs;
        public double PacketLossPercent;
        public bool DnsResolved;
        public bool InternetReachable;
        public string Summary = "尚未检测";
        public DateTime CheckedAt = DateTime.Now;
    }

    internal sealed class RepairResult
    {
        public bool Success;
        public bool RestartRequired;
        public readonly List<string> Lines = new List<string>();
    }

    internal static class NetworkDiagnostics
    {
        public static NetworkReport ReadAdapter()
        {
            NetworkReport report = new NetworkReport();
            try
            {
                NetworkInterface selected = null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }
                    if (selected == null || ni.Speed > selected.Speed)
                    {
                        selected = ni;
                    }
                }

                if (selected == null)
                {
                    report.Summary = "没有可用的网络连接";
                    return report;
                }

                report.Available = true;
                report.AdapterName = selected.Name;
                report.LinkSpeed = FormatLinkSpeed(selected.Speed);
                IPInterfaceProperties properties = selected.GetIPProperties();
                try
                {
                    IPv4InterfaceProperties ipv4Properties = properties.GetIPv4Properties();
                    if (ipv4Properties != null && ipv4Properties.IsDhcpEnabled)
                    {
                        report.AdapterName += " · DHCP";
                    }
                }
                catch
                {
                }

                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        report.LocalAddress = address.Address.ToString();
                        break;
                    }
                }
                foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        report.Gateway = gateway.Address.ToString();
                        break;
                    }
                }
                List<string> dns = new List<string>();
                foreach (IPAddress address in properties.DnsAddresses)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        dns.Add(address.ToString());
                    }
                }
                report.DnsServers = dns.Count > 0 ? String.Join(", ", dns.ToArray()) : "自动/未知";
                report.InternetReachable = NetworkInterface.GetIsNetworkAvailable();
                report.Summary = report.InternetReachable ? "网络已连接" : "网络未连接";
            }
            catch (Exception ex)
            {
                report.Summary = "读取网络信息失败";
                FileLogger.Write("读取网络信息失败: " + ex);
            }
            return report;
        }

        public static NetworkReport Test(Action<string> progress)
        {
            NetworkReport report = ReadAdapter();
            if (!report.Available)
            {
                return report;
            }
            report.Tested = true;

            try
            {
                if (progress != null)
                {
                    progress("正在测试网关延迟…");
                }
                PingSample gateway = PingAddress(report.Gateway, 4, 900);
                report.GatewayLatencyMs = gateway.AverageMs;

                if (progress != null)
                {
                    progress("正在测试互联网延迟…");
                }
                PingSample internet = PingAddress("223.5.5.5", 5, 1200);
                report.InternetLatencyMs = internet.AverageMs;
                report.PacketLossPercent = internet.LossPercent;
                report.JitterMs = internet.JitterMs;
                report.InternetReachable = internet.SuccessCount > 0;

                if (progress != null)
                {
                    progress("正在测试域名解析…");
                }
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses("store.steampowered.com");
                    report.DnsResolved = addresses != null && addresses.Length > 0;
                }
                catch
                {
                    report.DnsResolved = false;
                }

                if (!report.InternetReachable)
                {
                    report.Summary = "互联网连接异常";
                }
                else if (!report.DnsResolved)
                {
                    report.Summary = "可以联网，但域名解析异常";
                }
                else if (report.PacketLossPercent >= 20)
                {
                    report.Summary = "连接不稳定，存在明显丢包";
                }
                else if (report.InternetLatencyMs >= 150)
                {
                    report.Summary = "网络可用，但延迟偏高";
                }
                else
                {
                    report.Summary = "网络连接正常";
                }
                report.CheckedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                report.Summary = "网络检测失败";
                FileLogger.Write("网络检测失败: " + ex);
            }
            return report;
        }

        public static RepairResult QuickRepair()
        {
            RepairResult result = new RepairResult();
            result.Lines.Add(RunRepairCommand("ipconfig.exe", "/flushdns"));
            result.Lines.Add(RunRepairCommand("netsh.exe", "interface ip delete arpcache"));
            result.Lines.Add(RunRepairCommand("ipconfig.exe", "/registerdns"));
            result.Success = true;
            result.RestartRequired = false;
            FileLogger.Write("网络快速修复:\r\n" + String.Join("\r\n", result.Lines.ToArray()));
            return result;
        }

        public static RepairResult AdvancedRepair()
        {
            RepairResult result = new RepairResult();
            result.Lines.Add(RunRepairCommand("netsh.exe", "winsock reset"));
            result.Lines.Add(RunRepairCommand("netsh.exe", "int ip reset"));
            result.Success = true;
            result.RestartRequired = true;
            FileLogger.Write("网络高级修复:\r\n" + String.Join("\r\n", result.Lines.ToArray()));
            return result;
        }

        private static string RunRepairCommand(string fileName, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = fileName;
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.Default;
                startInfo.StandardErrorEncoding = Encoding.Default;
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(8000);
                    string text = (output + "\r\n" + error).Trim();
                    if (text.Length == 0)
                    {
                        text = "命令完成";
                    }
                    return fileName + " " + arguments + "：" + text;
                }
            }
            catch (Exception ex)
            {
                return fileName + " " + arguments + "：失败 - " + ex.Message;
            }
        }

        private static PingSample PingAddress(string address, int attempts, int timeout)
        {
            PingSample sample = new PingSample();
            if (String.IsNullOrWhiteSpace(address) || address == "-")
            {
                return sample;
            }
            try
            {
                using (Ping ping = new Ping())
                {
                    List<double> successful = new List<double>();
                    for (int i = 0; i < attempts; i++)
                    {
                        try
                        {
                            PingReply reply = ping.Send(address, timeout);
                            if (reply.Status == IPStatus.Success)
                            {
                                successful.Add(reply.RoundtripTime);
                                sample.SuccessCount++;
                            }
                        }
                        catch
                        {
                        }
                        if (i < attempts - 1)
                        {
                            Thread.Sleep(120);
                        }
                    }
                    sample.LossPercent =
                        (attempts - sample.SuccessCount) * 100.0 / attempts;
                    if (successful.Count > 0)
                    {
                        double total = 0;
                        for (int i = 0; i < successful.Count; i++)
                        {
                            total += successful[i];
                            if (i > 0)
                            {
                                sample.JitterMs += Math.Abs(successful[i] - successful[i - 1]);
                            }
                        }
                        sample.AverageMs = total / successful.Count;
                        if (successful.Count > 1)
                        {
                            sample.JitterMs /= successful.Count - 1;
                        }
                    }
                }
            }
            catch
            {
            }
            return sample;
        }

        private static string FormatLinkSpeed(long bitsPerSecond)
        {
            if (bitsPerSecond <= 0)
            {
                return "-";
            }
            if (bitsPerSecond >= 1000000000L)
            {
                return (bitsPerSecond / 1000000000.0).ToString("0.#") + " Gbps";
            }
            return (bitsPerSecond / 1000000.0).ToString("0") + " Mbps";
        }

        private sealed class PingSample
        {
            public int SuccessCount;
            public double AverageMs;
            public double LossPercent;
            public double JitterMs;
        }
    }

    internal sealed class SteamGameInfo
    {
        public string Name;
        public string InstallPath;
    }

    internal sealed class GameProcessInfo
    {
        public int ProcessId;
        public string ProcessName;
        public string DisplayName;
        public string WindowTitle;
        public IntPtr WindowHandle;
        public bool IsForeground;

        public override string ToString()
        {
            return DisplayName + (IsForeground ? "（当前前台）" : "");
        }
    }

    internal sealed class GameSnapshot
    {
        public int InstalledSteamGames;
        public readonly List<GameProcessInfo> RunningGames = new List<GameProcessInfo>();

        public string Summary
        {
            get
            {
                if (RunningGames.Count == 0)
                {
                    return InstalledSteamGames > 0
                        ? "已识别 " + InstalledSteamGames + " 个 Steam 游戏"
                        : "未检测到正在运行的游戏";
                }
                if (RunningGames.Count == 1)
                {
                    return RunningGames[0].DisplayName;
                }
                return RunningGames.Count + " 个游戏正在运行";
            }
        }
    }

    internal static class GameDetector
    {
        private static readonly object CacheLock = new object();
        private static readonly HashSet<string> NonGameProcesses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "explorer", "dwm", "taskmgr", "steam", "steamwebhelper",
                "steamservice", "gameoverlayui", "gameoverlayui64", "msedge", "chrome", "firefox",
                "brave", "opera", "vivaldi", "qq", "chatgpt", "wechat", "weixin",
                "discord", "teams", "telegram", "gameviewerserver", "gameviewerservice",
                "uu_launcher", "uu", "cmd", "powershell", "pwsh", "windowsterminal",
                "devenv", "code", "notepad", "notepad++", "electron"
            };
        private static List<SteamGameInfo> _steamGames = new List<SteamGameInfo>();
        private static DateTime _steamCacheTime = DateTime.MinValue;

        public static GameSnapshot Snapshot()
        {
            GameSnapshot snapshot = new GameSnapshot();
            List<SteamGameInfo> steamGames = GetSteamGames();
            snapshot.InstalledSteamGames = steamGames.Count;
            int currentSession;
            int currentPid;
            using (Process self = Process.GetCurrentProcess())
            {
                currentSession = self.SessionId;
                currentPid = self.Id;
            }
            int foregroundPid = NativeSystem.GetForegroundProcessId();
            IntPtr foregroundWindow = NativeSystem.ForegroundWindow();
            bool foregroundFullscreen =
                foregroundWindow != IntPtr.Zero &&
                NativeSystem.IsWindowFullscreen(foregroundWindow);

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == currentPid ||
                        process.SessionId != currentSession)
                    {
                        continue;
                    }

                    string path = null;
                    try
                    {
                        path = process.MainModule.FileName;
                    }
                    catch
                    {
                    }

                    SteamGameInfo steamGame = FindSteamGame(path, steamGames);
                    bool isForeground = process.Id == foregroundPid;
                    bool fullscreenGame =
                        isForeground &&
                        foregroundFullscreen &&
                        !NonGameProcesses.Contains(process.ProcessName);
                    bool steamGameRunning =
                        steamGame != null &&
                        !String.Equals(
                            process.ProcessName,
                            "steam",
                            StringComparison.OrdinalIgnoreCase);
                    if (!fullscreenGame && !steamGameRunning)
                    {
                        continue;
                    }

                    GameProcessInfo info = new GameProcessInfo();
                    info.ProcessId = process.Id;
                    info.ProcessName = process.ProcessName;
                    info.WindowTitle = SafeWindowTitle(process);
                    info.WindowHandle = process.MainWindowHandle;
                    info.IsForeground = isForeground;
                    if (steamGame != null)
                    {
                        info.DisplayName = steamGame.Name;
                    }
                    else if (!String.IsNullOrWhiteSpace(info.WindowTitle))
                    {
                        info.DisplayName = info.WindowTitle.Length > 44
                            ? info.WindowTitle.Substring(0, 43) + "…"
                            : info.WindowTitle;
                    }
                    else
                    {
                        info.DisplayName = process.ProcessName;
                    }

                    bool duplicate = false;
                    foreach (GameProcessInfo existing in snapshot.RunningGames)
                    {
                        if (existing.ProcessId == info.ProcessId ||
                            (existing.DisplayName == info.DisplayName &&
                             existing.ProcessName == info.ProcessName))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        snapshot.RunningGames.Add(info);
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return snapshot;
        }

        public static List<SteamGameInfo> GetSteamGames()
        {
            lock (CacheLock)
            {
                if ((DateTime.UtcNow - _steamCacheTime).TotalSeconds < 30)
                {
                    return new List<SteamGameInfo>(_steamGames);
                }

                List<SteamGameInfo> games = new List<SteamGameInfo>();
                List<string> libraries = new List<string>();
                string steamRoot = ReadSteamRoot();
                if (Directory.Exists(steamRoot))
                {
                    AddUniquePath(libraries, steamRoot);
                    string libraryFile = Path.Combine(
                        steamRoot,
                        @"steamapps\libraryfolders.vdf");
                    if (File.Exists(libraryFile))
                    {
                        try
                        {
                            string text = File.ReadAllText(libraryFile, Encoding.UTF8);
                            MatchCollection matches = Regex.Matches(
                                text,
                                "\\\"path\\\"\\s*\\\"([^\\\"]+)\\\"",
                                RegexOptions.IgnoreCase);
                            foreach (Match match in matches)
                            {
                                if (match.Groups.Count > 1)
                                {
                                    AddUniquePath(
                                        libraries,
                                        match.Groups[1].Value.Replace("\\\\", "\\"));
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }

                foreach (string library in libraries)
                {
                    string apps = Path.Combine(library, "steamapps");
                    if (!Directory.Exists(apps))
                    {
                        continue;
                    }
                    try
                    {
                        foreach (string manifest in Directory.GetFiles(apps, "appmanifest_*.acf"))
                        {
                            try
                            {
                                string text = File.ReadAllText(manifest, Encoding.UTF8);
                                Match nameMatch = Regex.Match(
                                    text,
                                    "\\\"name\\\"\\s*\\\"([^\\\"]+)\\\"",
                                    RegexOptions.IgnoreCase);
                                Match installMatch = Regex.Match(
                                    text,
                                    "\\\"installdir\\\"\\s*\\\"([^\\\"]+)\\\"",
                                    RegexOptions.IgnoreCase);
                                if (!nameMatch.Success || !installMatch.Success)
                                {
                                    continue;
                                }
                                string install = Path.Combine(
                                    apps,
                                    "common",
                                    installMatch.Groups[1].Value.Replace("\\\\", "\\"));
                                if (!Directory.Exists(install))
                                {
                                    continue;
                                }
                                SteamGameInfo game = new SteamGameInfo();
                                game.Name = nameMatch.Groups[1].Value;
                                game.InstallPath = install;
                                AddUniqueGame(games, game);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                _steamGames = games;
                _steamCacheTime = DateTime.UtcNow;
                return new List<SteamGameInfo>(games);
            }
        }

        private static SteamGameInfo FindSteamGame(string processPath, List<SteamGameInfo> games)
        {
            if (String.IsNullOrWhiteSpace(processPath))
            {
                return null;
            }
            string normalizedProcess = SafeFullPath(processPath);
            foreach (SteamGameInfo game in games)
            {
                string root = SafeFullPath(game.InstallPath).TrimEnd('\\') + "\\";
                if (normalizedProcess.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return game;
                }
            }
            return null;
        }

        private static string ReadSteamRoot()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam",
                    "SteamPath",
                    null);
                if (value != null)
                {
                    string path = Convert.ToString(value).Replace('/', '\\');
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                    "InstallPath",
                    null);
                if (value != null)
                {
                    string path = Convert.ToString(value);
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
            }
            return String.Empty;
        }

        private static string SafeWindowTitle(Process process)
        {
            try
            {
                return process.MainWindowTitle ?? String.Empty;
            }
            catch
            {
                return String.Empty;
            }
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path ?? String.Empty;
            }
        }

        private static void AddUniquePath(List<string> paths, string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return;
            }
            string normalized = SafeFullPath(path).TrimEnd('\\');
            foreach (string existing in paths)
            {
                if (String.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            paths.Add(normalized);
        }

        private static void AddUniqueGame(List<SteamGameInfo> games, SteamGameInfo game)
        {
            foreach (SteamGameInfo existing in games)
            {
                if (String.Equals(
                    existing.InstallPath,
                    game.InstallPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            games.Add(game);
        }
    }

    internal sealed class FrameCaptureResult
    {
        public bool Success;
        public double AverageFps;
        public double OnePercentLowFps;
        public double AverageFrameTimeMs;
        public double MedianFrameTimeMs;
        public int FrameCount;
        public int StutterCount;
        public string CsvPath;
        public string Error;
    }

    internal static class FrameCaptureService
    {
        public static string FindPresentMon()
        {
            try
            {
                string local = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "PresentMon_x64.exe");
                if (File.Exists(local))
                {
                    return local;
                }
                string nvidia = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"NVIDIA Corporation\FrameViewSDK\bin\PresentMon_x64.exe");
                if (File.Exists(nvidia))
                {
                    return nvidia;
                }
                string intel = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    @"Intel\PresentMon\PresentMon_x64.exe");
                if (File.Exists(intel))
                {
                    return intel;
                }
                string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
                foreach (string directory in pathVariable.Split(';'))
                {
                    try
                    {
                        string candidate = Path.Combine(directory.Trim(), "PresentMon_x64.exe");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return String.Empty;
        }

        public static FrameCaptureResult Capture(
            int processId,
            int seconds,
            Action<string> progress)
        {
            FrameCaptureResult result = new FrameCaptureResult();
            string presentMon = FindPresentMon();
            if (String.IsNullOrWhiteSpace(presentMon))
            {
                result.Error = "未找到 PresentMon。安装 NVIDIA FrameView 或 Intel PresentMon 后可使用帧时间诊断。";
                return result;
            }
            try
            {
                if (!Directory.Exists(AppPaths.DataDirectory))
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                }
                string captures = Path.Combine(AppPaths.DataDirectory, "captures");
                if (!Directory.Exists(captures))
                {
                    Directory.CreateDirectory(captures);
                }
                string csv = Path.Combine(
                    captures,
                    "game-" + processId + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                string arguments = String.Format(
                    "--process_id {0} --output_file \"{1}\" --timed {2} " +
                    "--terminate_after_timed --exclude_dropped --no_console_stats " +
                    "--session_name PcManagerLite{0}",
                    processId,
                    csv,
                    seconds);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = presentMon;
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using (Process process = Process.Start(startInfo))
                {
                    if (progress != null)
                    {
                        progress("正在采样，请保持游戏在前台…");
                    }
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit((seconds + 15) * 1000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                    }
                }

                result.CsvPath = csv;
                ParseCsv(csv, result);
                if (result.FrameCount == 0 && String.IsNullOrWhiteSpace(result.Error))
                {
                    result.Error = "没有捕获到游戏帧。请确认游戏正在渲染，并使用全屏或无边框窗口模式。";
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("帧时间采样失败: " + ex);
            }
            return result;
        }

        public static void ParseCsv(string path, FrameCaptureResult result)
        {
            if (!File.Exists(path))
            {
                result.Error = "采样文件不存在。";
                return;
            }
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 2)
            {
                result.Error = "采样数据为空。";
                return;
            }
            List<string> header = ParseCsvLine(lines[0]);
            int displayIndex = IndexOf(header, "msBetweenDisplayChange");
            int presentIndex = IndexOf(header, "msBetweenPresents");
            List<double> frameTimes = new List<double>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (String.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }
                List<string> row = ParseCsvLine(lines[i]);
                double value = 0;
                if (displayIndex >= 0 && displayIndex < row.Count)
                {
                    Double.TryParse(
                        row[displayIndex],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value);
                }
                if (value <= 0 &&
                    presentIndex >= 0 &&
                    presentIndex < row.Count)
                {
                    Double.TryParse(
                        row[presentIndex],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out value);
                }
                if (value > 0 && value < 5000)
                {
                    frameTimes.Add(value);
                }
            }
            if (frameTimes.Count < 2)
            {
                result.Error = "捕获到的有效帧不足。";
                return;
            }

            double total = 0;
            foreach (double value in frameTimes)
            {
                total += value;
            }
            result.FrameCount = frameTimes.Count;
            result.AverageFrameTimeMs = total / frameTimes.Count;
            result.AverageFps = 1000.0 / result.AverageFrameTimeMs;

            frameTimes.Sort();
            result.MedianFrameTimeMs = Percentile(frameTimes, 0.50);
            double p99 = Percentile(frameTimes, 0.99);
            result.OnePercentLowFps = p99 > 0 ? 1000.0 / p99 : 0;
            double stutterThreshold = Math.Max(33.4, result.MedianFrameTimeMs * 2.0);
            foreach (double value in frameTimes)
            {
                if (value > stutterThreshold)
                {
                    result.StutterCount++;
                }
            }
            result.Success = true;
        }

        internal static bool RunParserSelfTest(out string details)
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "PcManagerLiteFrameTest-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Application,ProcessID,msBetweenPresents,msBetweenDisplayChange");
                for (int i = 0; i < 95; i++)
                {
                    csv.AppendLine("test,1,10,10");
                }
                for (int i = 0; i < 5; i++)
                {
                    csv.AppendLine("test,1,40,40");
                }
                File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
                FrameCaptureResult result = new FrameCaptureResult();
                ParseCsv(path, result);
                details = String.Format(
                    "success={0}, frames={1}, avgFps={2:0.0}, low1={3:0.0}, stutters={4}",
                    result.Success,
                    result.FrameCount,
                    result.AverageFps,
                    result.OnePercentLowFps,
                    result.StutterCount);
                return result.Success &&
                    result.FrameCount == 100 &&
                    result.OnePercentLowFps < 30 &&
                    result.StutterCount == 5;
            }
            catch (Exception ex)
            {
                details = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                }
            }
        }

        private static double Percentile(List<double> sorted, double percentile)
        {
            if (sorted.Count == 0)
            {
                return 0;
            }
            int index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
            index = Math.Max(0, Math.Min(sorted.Count - 1, index));
            return sorted[index];
        }

        private static int IndexOf(List<string> header, string name)
        {
            for (int i = 0; i < header.Count; i++)
            {
                if (String.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static List<string> ParseCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (c == ',' && !quoted)
                {
                    fields.Add(field.ToString());
                    field.Length = 0;
                }
                else
                {
                    field.Append(c);
                }
            }
            fields.Add(field.ToString());
            return fields;
        }
    }

    internal enum RiskLevel
    {
        Low,
        Medium,
        High
    }

    internal sealed class ProcessGroup
    {
        public string ProcessName;
        public string DisplayName;
        public string Recommendation;
        public string Note;
        public RiskLevel Risk;
        public bool DefaultSelected;
        public readonly List<int> ProcessIds = new List<int>();
        public long WorkingSetBytes;
    }

    internal sealed class TerminationResult
    {
        public int ClosedProcesses;
        public long WorkingSetBytes;
        public readonly List<string> Errors = new List<string>();
    }

    internal sealed class CandidateDefinition
    {
        public readonly string DisplayName;
        public readonly string Note;
        public readonly RiskLevel Risk;
        public readonly bool DefaultSelected;

        public CandidateDefinition(
            string displayName,
            string note,
            RiskLevel risk,
            bool defaultSelected)
        {
            DisplayName = displayName;
            Note = note;
            Risk = risk;
            DefaultSelected = defaultSelected;
        }
    }

    internal static class ProcessScanner
    {
        private static readonly Dictionary<string, CandidateDefinition> Candidates =
            BuildCandidates();

        public static List<ProcessGroup> ScanGroups()
        {
            Dictionary<string, ProcessGroup> groups =
                new Dictionary<string, ProcessGroup>(StringComparer.OrdinalIgnoreCase);
            int currentSession;
            int currentPid;
            using (Process self = Process.GetCurrentProcess())
            {
                currentSession = self.SessionId;
                currentPid = self.Id;
            }

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    CandidateDefinition definition;
                    if (!Candidates.TryGetValue(process.ProcessName, out definition))
                    {
                        continue;
                    }
                    if (process.Id == currentPid ||
                        process.SessionId != currentSession)
                    {
                        continue;
                    }

                    ProcessGroup group;
                    if (!groups.TryGetValue(process.ProcessName, out group))
                    {
                        group = new ProcessGroup();
                        group.ProcessName = process.ProcessName;
                        group.DisplayName = definition.DisplayName;
                        group.Note = definition.Note;
                        group.Risk = definition.Risk;
                        group.DefaultSelected = definition.DefaultSelected;
                        group.Recommendation =
                            definition.Risk == RiskLevel.Low ? "优先关闭" :
                            definition.Risk == RiskLevel.Medium ? "按需关闭" : "谨慎";
                        groups[process.ProcessName] = group;
                    }
                    group.ProcessIds.Add(process.Id);
                    try
                    {
                        group.WorkingSetBytes += process.WorkingSet64;
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            List<ProcessGroup> result = new List<ProcessGroup>(groups.Values);
            result.Sort(delegate(ProcessGroup left, ProcessGroup right)
            {
                int memory = right.WorkingSetBytes.CompareTo(left.WorkingSetBytes);
                if (memory != 0)
                {
                    return memory;
                }
                return String.Compare(
                    left.DisplayName,
                    right.DisplayName,
                    StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public static TerminationResult TerminateGroups(List<ProcessGroup> selected)
        {
            TerminationResult result = new TerminationResult();
            int currentSession = Process.GetCurrentProcess().SessionId;

            foreach (ProcessGroup group in selected)
            {
                List<Process> processes = new List<Process>();
                foreach (int processId in group.ProcessIds)
                {
                    Process process = null;
                    bool retained = false;
                    try
                    {
                        process = Process.GetProcessById(processId);
                        if (process.SessionId != currentSession ||
                            !String.Equals(
                                process.ProcessName,
                                group.ProcessName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        processes.Add(process);
                        retained = true;
                        process.CloseMainWindow();
                    }
                    catch (Exception ex)
                    {
                        string error = group.DisplayName + " (" + processId + "): " + ex.Message;
                        if (!result.Errors.Contains(error))
                        {
                            result.Errors.Add(error);
                        }
                    }
                    finally
                    {
                        if (process != null && !retained)
                        {
                            process.Dispose();
                        }
                    }
                }

                Thread.Sleep(280);
                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(800);
                        }
                        if (process.HasExited)
                        {
                            result.ClosedProcesses++;
                            result.WorkingSetBytes += group.WorkingSetBytes /
                                Math.Max(1, group.ProcessIds.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        string error = group.DisplayName + " (" + process.Id + "): " + ex.Message;
                        if (!result.Errors.Contains(error))
                        {
                            result.Errors.Add(error);
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            return result;
        }

        private static Dictionary<string, CandidateDefinition> BuildCandidates()
        {
            Dictionary<string, CandidateDefinition> map =
                new Dictionary<string, CandidateDefinition>(StringComparer.OrdinalIgnoreCase);
            Add(map, "qq", "QQ", "聊天软件；关闭会退出当前会话窗口", RiskLevel.Low, true);
            Add(map, "chatgpt", "ChatGPT", "桌面客户端；关闭后需要重新打开", RiskLevel.Low, true);
            Add(map, "msedge", "Microsoft Edge", "浏览器；关闭当前所有普通窗口", RiskLevel.Low, true);
            Add(map, "chrome", "Google Chrome", "浏览器；关闭当前所有普通窗口", RiskLevel.Low, true);
            Add(map, "firefox", "Firefox", "浏览器；关闭当前所有普通窗口", RiskLevel.Low, true);
            Add(map, "wechat", "微信", "聊天软件；关闭会退出当前会话窗口", RiskLevel.Low, true);
            Add(map, "weixin", "微信", "聊天软件；关闭会退出当前会话窗口", RiskLevel.Low, true);
            Add(map, "telegram", "Telegram", "聊天软件；关闭后需要重新打开", RiskLevel.Low, true);
            Add(map, "discord", "Discord", "语音软件；会退出当前语音频道", RiskLevel.Medium, false);
            Add(map, "teams", "Microsoft Teams", "会议软件；关闭后需要重新打开", RiskLevel.Medium, false);
            Add(map, "onedrive", "OneDrive", "云同步；关闭后暂停同步", RiskLevel.Medium, false);
            Add(map, "dropbox", "Dropbox", "云同步；关闭后暂停同步", RiskLevel.Medium, false);
            Add(map, "baidunetdisk", "百度网盘", "云盘同步；关闭后暂停同步", RiskLevel.Medium, false);
            Add(map, "thunder", "迅雷", "下载软件；关闭后下载停止", RiskLevel.Medium, false);
            Add(map, "xunlei", "迅雷", "下载软件；关闭后下载停止", RiskLevel.Medium, false);
            Add(map, "cloudmusic", "网易云音乐", "音乐软件；关闭后停止播放", RiskLevel.Low, true);
            Add(map, "neteasecloudmusic", "网易云音乐", "音乐软件；关闭后停止播放", RiskLevel.Low, true);
            Add(map, "qqmusic", "QQ 音乐", "音乐软件；关闭后停止播放", RiskLevel.Low, true);
            Add(map, "bilibili", "哔哩哔哩", "视频软件；关闭后停止播放", RiskLevel.Low, true);
            Add(map, "douyin", "抖音", "视频软件；关闭后停止播放", RiskLevel.Low, true);
            Add(
                map,
                "gameoverlayui",
                "Steam 游戏覆盖层",
                "关闭可降低少量覆盖层开销；不影响 Steam 主程序",
                RiskLevel.Medium,
                false);
            Add(
                map,
                "gameoverlayui64",
                "Steam 游戏覆盖层",
                "关闭可降低少量覆盖层开销；不影响 Steam 主程序",
                RiskLevel.Medium,
                false);
            Add(
                map,
                "gameviewerserver",
                "GameViewer 远程端",
                "关闭可能中断远程连接；仅本地游玩时选择",
                RiskLevel.High,
                false);
            Add(
                map,
                "uu_launcher",
                "网易 UU 启动器",
                "单机游戏可关闭；联机游戏请保留网络加速",
                RiskLevel.High,
                false);
            Add(
                map,
                "uu",
                "网易 UU",
                "联机游戏请保留网络加速",
                RiskLevel.High,
                false);
            return map;
        }

        private static void Add(
            Dictionary<string, CandidateDefinition> map,
            string processName,
            string displayName,
            string note,
            RiskLevel risk,
            bool defaultSelected)
        {
            map[processName] = new CandidateDefinition(
                displayName,
                note,
                risk,
                defaultSelected);
        }
    }

    internal static class SystemOptimizer
    {
        private const string MemoryManagementKey =
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

        private static readonly RegistrySetting[] GameSettings =
        {
            new RegistrySetting(
                @"HKEY_CURRENT_USER\Software\Microsoft\GameBar",
                "AutoGameModeEnabled",
                1),
            new RegistrySetting(
                @"HKEY_CURRENT_USER\Software\Microsoft\GameBar",
                "AllowAutoGameMode",
                1),
            new RegistrySetting(
                @"HKEY_CURRENT_USER\System\GameConfigStore",
                "GameDVR_Enabled",
                0),
            new RegistrySetting(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR",
                "AppCaptureEnabled",
                0),
            new RegistrySetting(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR",
                "HistoricalCaptureEnabled",
                0)
        };

        public static string[] GetPageFileEntries()
        {
            object value = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\" + MemoryManagementKey,
                "PagingFiles",
                null);
            string[] entries = value as string[];
            if (entries != null)
            {
                return entries;
            }
            string single = value as string;
            return String.IsNullOrWhiteSpace(single) ? new string[0] : new string[] { single };
        }

        public static string GetPageFileDisplay()
        {
            string[] entries = GetPageFileEntries();
            string[] active = GetActivePageFileEntries();
            if (entries.Length == 0)
            {
                return "未配置";
            }
            if (entries.Length == 1 &&
                entries[0].Length >= 2 &&
                active.Length > 0 &&
                !String.Equals(
                    entries[0].Substring(0, 2),
                    active[0].Length >= 2 ? active[0].Substring(0, 2) : "",
                    StringComparison.OrdinalIgnoreCase))
            {
                return DescribePageFileDrive(active[0]) + " 运行中 → " +
                    DescribePageFileDrive(entries[0]) + " 重启生效";
            }
            if (entries.Length == 1 && IsSystemManagedPageFile(entries[0]))
            {
                return "系统自动管理";
            }
            if (entries.Length == 1 && entries[0].Length >= 2)
            {
                bool automatic = entries[0].TrimEnd().EndsWith(
                    " 0 0",
                    StringComparison.Ordinal);
                return entries[0].Substring(0, 2) +
                    (automatic ? " 系统托管" : " 系统管理");
            }
            return entries.Length + " 个页面文件";
        }

        private static bool IsSystemManagedPageFile(string entry)
        {
            return !String.IsNullOrEmpty(entry) &&
                entry.TrimStart().StartsWith("?:", StringComparison.Ordinal);
        }

        private static string DescribePageFileDrive(string entry)
        {
            if (IsSystemManagedPageFile(entry))
            {
                return "系统托管";
            }
            return entry.Length >= 2 ? entry.Substring(0, 2) : entry;
        }

        public static string[] GetActivePageFileEntries()
        {
            List<string> entries = new List<string>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PageFileUsage"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        try
                        {
                            object name = item["Name"];
                            if (name != null)
                            {
                                entries.Add(Convert.ToString(name));
                            }
                        }
                        finally
                        {
                            item.Dispose();
                        }
                    }
                }
            }
            catch
            {
            }
            return entries.ToArray();
        }

        public static bool PageFileNeedsMove()
        {
            string[] entries = GetPageFileEntries();
            if (entries.Length == 0 || !Directory.Exists(@"D:\"))
            {
                return true;
            }
            foreach (string entry in entries)
            {
                if (!entry.TrimStart().StartsWith(
                    @"D:\pagefile.sys",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool CanMovePageFileToD()
        {
            try
            {
                if (!Directory.Exists(@"D:\"))
                {
                    return false;
                }
                DriveInfo drive = new DriveInfo("D");
                return drive.IsReady &&
                    drive.DriveType == DriveType.Fixed &&
                    drive.AvailableFreeSpace > 16L * 1024L * 1024L * 1024L &&
                    PageFileNeedsMove();
            }
            catch
            {
                return false;
            }
        }

        public static string GetPowerPlanName()
        {
            string output;
            int code;
            RunCommand("powercfg.exe", "/getactivescheme", out output, out code);
            if (code != 0 || String.IsNullOrWhiteSpace(output))
            {
                return "未知";
            }
            int open = output.LastIndexOf('(');
            int close = output.LastIndexOf(')');
            if (open >= 0 && close > open)
            {
                return output.Substring(open + 1, close - open - 1).Trim();
            }
            return "系统电源计划";
        }

        public static string GetPowerPlanGuid()
        {
            string output;
            int code;
            RunCommand("powercfg.exe", "/getactivescheme", out output, out code);
            if (code != 0)
            {
                return String.Empty;
            }
            Match match = Regex.Match(
                output,
                "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return match.Success ? match.Value : String.Empty;
        }

        public static int GetDword(string hiveUserPath, string name, int fallback)
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\" + StripCurrentUserPrefix(hiveUserPath),
                    name,
                    null);
                return value == null ? fallback : Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        public static void ApplyGameSettings(BackupStore backup)
        {
            for (int i = 0; i < GameSettings.Length; i++)
            {
                RegistrySetting setting = GameSettings[i];
                if (!backup.Contains("reg." + i))
                {
                    backup.Set("reg." + i, RegistrySnapshot.Capture(setting.Path, setting.Name));
                }
                RegistrySnapshot.SetDword(setting.Path, setting.Name, setting.TargetValue);
            }
        }

        public static void ApplyHighPerformancePower(BackupStore backup)
        {
            string current = GetPowerPlanGuid();
            if (!String.IsNullOrWhiteSpace(current) && !backup.Contains("power.guid"))
            {
                backup.Set("power.guid", current);
            }
            string output;
            int code;
            RunCommand("powercfg.exe", "/setactive SCHEME_MIN", out output, out code);
            if (code != 0)
            {
                throw new InvalidOperationException(
                    String.IsNullOrWhiteSpace(output) ? "切换电源计划失败" : output.Trim());
            }
        }

        public static void ApplyPageFileToD(BackupStore backup)
        {
            if (!CanMovePageFileToD())
            {
                throw new InvalidOperationException(
                    "D: 不可用、剩余空间不足或虚拟内存已在 D:。");
            }
            string[] current = GetPageFileEntries();
            if (!backup.Contains("pagefile.original"))
            {
                backup.Set("pagefile.original", EncodeLines(current));
            }
            SetPageFileEntries(new string[] { @"D:\pagefile.sys 0 0" });
        }

        public static List<string> RestoreAll(BackupStore backup, out bool restartRequired)
        {
            List<string> messages = new List<string>();
            restartRequired = false;

            string guid;
            if (backup.TryGet("power.guid", out guid) && !String.IsNullOrWhiteSpace(guid))
            {
                string output;
                int code;
                RunCommand("powercfg.exe", "/setactive " + guid, out output, out code);
                messages.Add(code == 0 ? "电源计划已恢复" : "电源计划恢复失败");
            }

            bool restored = false;
            for (int i = 0; i < GameSettings.Length; i++)
            {
                string snapshot;
                if (backup.TryGet("reg." + i, out snapshot))
                {
                    try
                    {
                        RegistrySnapshot.Restore(
                            GameSettings[i].Path,
                            GameSettings[i].Name,
                            snapshot);
                        restored = true;
                    }
                    catch
                    {
                    }
                }
            }
            if (restored)
            {
                messages.Add("游戏模式与 Game DVR 已恢复");
            }

            string pageFile;
            if (backup.TryGet("pagefile.original", out pageFile))
            {
                SetPageFileEntries(DecodeLines(pageFile));
                messages.Add("虚拟内存配置已恢复");
                restartRequired = true;
            }

            if (messages.Count == 0)
            {
                messages.Add("没有找到可恢复的系统设置");
            }
            return messages;
        }

        public static bool IsGameRunning()
        {
            try
            {
                return GameDetector.Snapshot().RunningGames.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SetPageFileEntries(string[] entries)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(MemoryManagementKey, true))
            {
                if (key == null)
                {
                    throw new UnauthorizedAccessException("无法打开虚拟内存配置。");
                }
                key.SetValue("PagingFiles", entries, RegistryValueKind.MultiString);
            }
        }

        private static string StripCurrentUserPrefix(string path)
        {
            const string prefix = @"HKEY_CURRENT_USER\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefix.Length)
                : path;
        }

        private static string EncodeLines(string[] lines)
        {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(String.Join("\n", lines ?? new string[0])));
        }

        private static string[] DecodeLines(string encoded)
        {
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return text.Length == 0
                ? new string[0]
                : text.Split(new string[] { "\n" }, StringSplitOptions.None);
        }

        private static void RunCommand(
            string fileName,
            string arguments,
            out string output,
            out int exitCode)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.Default;
            startInfo.StandardErrorEncoding = Encoding.Default;
            using (Process process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(6000);
                output = stdout + (String.IsNullOrWhiteSpace(stderr) ? "" : "\r\n" + stderr);
                exitCode = process.HasExited ? process.ExitCode : -1;
            }
        }
    }

    internal sealed class RegistrySetting
    {
        public readonly string Path;
        public readonly string Name;
        public readonly int TargetValue;

        public RegistrySetting(string path, string name, int targetValue)
        {
            Path = path;
            Name = name;
            TargetValue = targetValue;
        }
    }

    internal static class RegistrySnapshot
    {
        public static string Capture(string path, string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StripPrefix(path), false))
            {
                if (key == null || Array.IndexOf(key.GetValueNames(), name) < 0)
                {
                    return "MISSING";
                }
                object value = key.GetValue(name);
                if (value == null)
                {
                    return "NULL";
                }
                if (key.GetValueKind(name) == RegistryValueKind.DWord)
                {
                    return "DWORD:" + Convert.ToInt32(value).ToString();
                }
                return "STRING:" + Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(Convert.ToString(value)));
            }
        }

        public static void SetDword(string path, string name, int value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StripPrefix(path)))
            {
                if (key == null)
                {
                    throw new UnauthorizedAccessException("无法写入注册表。");
                }
                key.SetValue(name, value, RegistryValueKind.DWord);
            }
        }

        public static void Restore(string path, string name, string snapshot)
        {
            if (snapshot == "MISSING")
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StripPrefix(path), true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(name, false);
                    }
                }
                return;
            }
            if (snapshot == "NULL")
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StripPrefix(path)))
                {
                    if (key != null)
                    {
                        key.SetValue(name, null, RegistryValueKind.String);
                    }
                }
                return;
            }
            int colon = snapshot.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidDataException("无效的注册表备份。");
            }
            string kind = snapshot.Substring(0, colon);
            string value = snapshot.Substring(colon + 1);
            if (kind == "DWORD")
            {
                SetDword(path, name, Int32.Parse(value));
                return;
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StripPrefix(path)))
            {
                if (key == null)
                {
                    throw new UnauthorizedAccessException("无法恢复注册表。");
                }
                key.SetValue(
                    name,
                    Encoding.UTF8.GetString(Convert.FromBase64String(value)),
                    RegistryValueKind.String);
            }
        }

        private static string StripPrefix(string path)
        {
            const string prefix = @"HKEY_CURRENT_USER\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefix.Length)
                : path;
        }
    }

    internal sealed class BackupStore
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool HasSettings
        {
            get { return _values.Count > 0; }
        }

        public static BackupStore Load()
        {
            BackupStore store = new BackupStore();
            try
            {
                string source = File.Exists(AppPaths.BackupFile)
                    ? AppPaths.BackupFile
                    : LegacyBackupPath();
                if (File.Exists(source))
                {
                    foreach (string line in File.ReadAllLines(source, Encoding.UTF8))
                    {
                        int equals = line.IndexOf('=');
                        if (equals > 0)
                        {
                            store._values[line.Substring(0, equals)] =
                                Encoding.UTF8.GetString(
                                    Convert.FromBase64String(line.Substring(equals + 1)));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Write("读取设置备份失败: " + ex.Message);
            }
            return store;
        }

        private static string LegacyBackupPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameBoostLite",
                "settings-backup.txt");
        }

        public bool Contains(string key)
        {
            return _values.ContainsKey(key);
        }

        public void Set(string key, string value)
        {
            _values[key] = value;
        }

        public bool TryGet(string key, out string value)
        {
            return _values.TryGetValue(key, out value);
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataDirectory))
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                }
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> pair in _values)
                {
                    lines.Add(
                        pair.Key + "=" +
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value)));
                }
                File.WriteAllLines(AppPaths.BackupFile, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                FileLogger.Write("保存设置备份失败: " + ex.Message);
            }
        }
    }

    internal static class ShortcutManager
    {
        public static string DesktopShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "电脑管家 Lite.lnk");
            }
        }

        public static void CreateDesktopShortcut()
        {
            string target = ApplicationExecutablePath();
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("系统快捷方式组件不可用。");
            }
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { DesktopShortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember(
                "TargetPath",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { target });
            shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { Path.GetDirectoryName(target) });
            shortcutType.InvokeMember(
                "Description",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { "电脑管家 Lite - 清理、游戏加速与网络修复" });
            shortcutType.InvokeMember(
                "IconLocation",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { target + ",0" });
            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                null,
                shortcut,
                null);
        }

        public static string ApplicationExecutablePath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }
    }

    internal sealed class SystemRestoreResult
    {
        public bool Success;
        public bool Skipped;
        public string Message;
    }

    internal static class SystemRestoreService
    {
        public static SystemRestoreResult CreateRestorePoint()
        {
            SystemRestoreResult result = new SystemRestoreResult();
            try
            {
                string powershell = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\WindowsPowerShell\v1.0\powershell.exe");
                if (!File.Exists(powershell))
                {
                    result.Message = "系统自带 PowerShell 不可用。";
                    return result;
                }
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = powershell;
                startInfo.Arguments =
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command " +
                    "\"Checkpoint-Computer -Description '电脑管家 Lite 优化点' " +
                    "-RestorePointType MODIFY_SETTINGS\"";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.Default;
                startInfo.StandardErrorEncoding = Encoding.Default;
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(180000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        result.Message = "创建还原点超时。请打开系统保护设置检查配置。";
                        return result;
                    }
                    string combined = (output + "\r\n" + error).Trim();
                    if (process.ExitCode == 0 && String.IsNullOrWhiteSpace(error))
                    {
                        result.Success = true;
                        result.Message = "系统还原点已创建。";
                        return result;
                    }
                    if (combined.IndexOf("1440", StringComparison.Ordinal) >= 0 ||
                        combined.IndexOf("frequently", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        combined.IndexOf("频率", StringComparison.Ordinal) >= 0)
                    {
                        result.Skipped = true;
                        result.Message = "Windows 24 小时内已创建过还原点，本次跳过。";
                        return result;
                    }
                    if (combined.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        combined.IndexOf("禁用", StringComparison.Ordinal) >= 0)
                    {
                        result.Message = "系统保护未开启，无法创建还原点。";
                        return result;
                    }
                    result.Message = String.IsNullOrWhiteSpace(combined)
                        ? "创建还原点失败，系统未返回原因。"
                        : combined;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Message = ex.Message;
                FileLogger.Write("创建系统还原点失败: " + ex);
                return result;
            }
        }

        public static void OpenSystemRestore()
        {
            Process.Start("rstrui.exe");
        }

        public static void OpenLogs()
        {
            string directory = AppPaths.DataDirectory;
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            Process.Start("explorer.exe", "\"" + directory + "\"");
        }
    }

    internal sealed class ManagerSettings
    {
        public string Theme = "system";
        public string Accent = "blue";
        public bool CheckUpdatesOnStart = true;

        public static ManagerSettings Load()
        {
            ManagerSettings settings = new ManagerSettings();
            string path = Path.Combine(AppPaths.DataDirectory, "settings.ini");
            try
            {
                if (File.Exists(path))
                {
                    foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        int equals = line.IndexOf('=');
                        if (equals <= 0)
                        {
                            continue;
                        }
                        string key = line.Substring(0, equals).Trim();
                        string value = line.Substring(equals + 1).Trim();
                        if (key.Equals("Theme", StringComparison.OrdinalIgnoreCase))
                        {
                            if (value.Equals("light", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("dark", StringComparison.OrdinalIgnoreCase) ||
                                value.Equals("system", StringComparison.OrdinalIgnoreCase))
                            {
                                settings.Theme = value.ToLowerInvariant();
                            }
                        }
                        else if (key.Equals("Accent", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Accent = Palette.NormalizeAccent(value);
                        }
                        else if (key.Equals("CheckUpdatesOnStart", StringComparison.OrdinalIgnoreCase))
                        {
                            bool parsed;
                            if (Boolean.TryParse(value, out parsed))
                            {
                                settings.CheckUpdatesOnStart = parsed;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Write("读取设置失败: " + ex.Message);
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataDirectory))
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                }
                File.WriteAllLines(
                    Path.Combine(AppPaths.DataDirectory, "settings.ini"),
                    new string[]
                    {
                        "Theme=" + Theme,
                        "Accent=" + Accent,
                        "CheckUpdatesOnStart=" + CheckUpdatesOnStart.ToString()
                    },
                    Encoding.UTF8);
            }
            catch (Exception ex)
            {
                FileLogger.Write("保存设置失败: " + ex.Message);
            }
        }
    }

    internal static class AppStartupService
    {
        private const string RunKey =
            @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PcManagerLite";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    string value = Convert.ToString(key.GetValue(ValueName, String.Empty));
                    string exe = ShortcutManager.ApplicationExecutablePath();
                    return value.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null)
                {
                    throw new UnauthorizedAccessException("无法打开开机启动注册表。");
                }
                if (enabled)
                {
                    string executable = ShortcutManager.ApplicationExecutablePath();
                    key.SetValue(
                        ValueName,
                        "\"" + executable + "\" --minimized",
                        RegistryValueKind.String);
                }
                else
                {
                    key.DeleteValue(ValueName, false);
                }
            }
        }
    }

    internal sealed class UpdateInfo
    {
        public string Version;
        public long Size;
        public string Sha256;
        public string DownloadUrl;
        public readonly List<string> Notes = new List<string>();
    }

    internal sealed class UpdateCheckResult
    {
        public bool Available;
        public bool UpToDate;
        public string CurrentVersion;
        public string LatestVersion;
        public string Error;
        public UpdateInfo Info;
    }

    internal static class UpdateService
    {
        private sealed class TimedWebClient : WebClient
        {
            private readonly int _timeoutMs;

            public TimedWebClient(int timeoutMs)
            {
                _timeoutMs = timeoutMs;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                {
                    request.Timeout = _timeoutMs;
                    HttpWebRequest http = request as HttpWebRequest;
                    if (http != null)
                    {
                        http.ReadWriteTimeout = _timeoutMs;
                    }
                }
                return request;
            }
        }

        public const string ManifestUrl =
            "https://jokerx.top/downloads/pc-manager-lite/version.js";
        public const string DefaultDownloadUrl =
            "https://jokerx.top/downloads/pc-manager-lite/pc-manager-lite.exe";

        public static string CurrentVersion()
        {
            Version version = typeof(UpdateService).Assembly.GetName().Version;
            return version == null
                ? "0.0.0"
                : version.Major + "." + version.Minor + "." + Math.Max(0, version.Build);
        }

        public static UpdateCheckResult Check()
        {
            UpdateCheckResult result = null;
            using (ManualResetEventSlim completed = new ManualResetEventSlim(false))
            {
                Thread worker = new Thread(delegate()
                {
                    try
                    {
                        result = CheckCore();
                    }
                    catch (Exception ex)
                    {
                        result = new UpdateCheckResult
                        {
                            CurrentVersion = CurrentVersion(),
                            Error = ex.Message
                        };
                    }
                    finally
                    {
                        completed.Set();
                    }
                });
                worker.IsBackground = true;
                worker.Name = "PcManagerLiteUpdateCheck";
                worker.Start();
                if (!completed.Wait(15000))
                {
                    return new UpdateCheckResult
                    {
                        CurrentVersion = CurrentVersion(),
                        Error = "检查更新超时，请稍后重试。"
                    };
                }
            }
            return result ?? new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion(),
                Error = "检查更新未返回结果。"
            };
        }

        private static UpdateCheckResult CheckCore()
        {
            UpdateCheckResult result = new UpdateCheckResult();
            result.CurrentVersion = CurrentVersion();
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient client = new TimedWebClient(12000))
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.UserAgent] =
                        "PcManagerLite/" + result.CurrentVersion;
                    client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                    string url = ManifestUrl + "?t=" + DateTime.UtcNow.Ticks;
                    string text = client.DownloadString(url);
                    Match match = Regex.Match(
                        text,
                        @"=\s*(\{[\s\S]*\})\s*;?\s*$");
                    if (!match.Success)
                    {
                        throw new InvalidDataException("服务器更新清单格式不正确。");
                    }
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> data =
                        serializer.DeserializeObject(match.Groups[1].Value)
                        as Dictionary<string, object>;
                    if (data == null)
                    {
                        throw new InvalidDataException("服务器更新清单为空。");
                    }

                    UpdateInfo info = new UpdateInfo();
                    info.Version = Convert.ToString(GetValue(data, "versionName", ""));
                    info.Size = Convert.ToInt64(GetValue(data, "size", 0L));
                    info.Sha256 = Convert.ToString(GetValue(data, "sha256", ""));
                    info.DownloadUrl = Convert.ToString(
                        GetValue(data, "downloadUrl", DefaultDownloadUrl));
                    object notes = GetValue(data, "releaseNotes", null);
                    object[] noteArray = notes as object[];
                    if (noteArray != null)
                    {
                        foreach (object note in noteArray)
                        {
                            string line = Convert.ToString(note);
                            if (!String.IsNullOrWhiteSpace(line))
                            {
                                info.Notes.Add(line);
                            }
                        }
                    }

                    Uri downloadUri;
                    if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out downloadUri) ||
                        !downloadUri.Host.Equals(
                            "jokerx.top",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "更新下载地址不是 jokerx.top，已拒绝。");
                    }
                    if (String.IsNullOrWhiteSpace(info.Version) ||
                        String.IsNullOrWhiteSpace(info.Sha256))
                    {
                        throw new InvalidDataException("更新清单缺少版本或哈希。");
                    }

                    result.Info = info;
                    result.LatestVersion = info.Version;
                    Version current;
                    Version latest;
                    if (!Version.TryParse(result.CurrentVersion, out current) ||
                        !Version.TryParse(NormalizeVersion(info.Version), out latest))
                    {
                        throw new InvalidDataException("无法比较版本号。");
                    }
                    result.UpToDate = latest <= current;
                    result.Available = latest > current;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("检查更新失败: " + ex);
            }
            return result;
        }

        public static bool DownloadVerified(
            UpdateInfo info,
            Action<string> progress,
            out string downloadedPath,
            out string error)
        {
            downloadedPath = String.Empty;
            error = String.Empty;
            try
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "PcManagerLiteUpdate");
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string path = Path.Combine(
                    directory,
                    "pc-manager-lite-" + NormalizeVersion(info.Version) + ".exe");
                if (progress != null)
                {
                    progress("正在从 jokerx.top 下载更新…");
                }
                using (WebClient client = new TimedWebClient(90000))
                {
                    client.Headers[HttpRequestHeader.UserAgent] =
                        "PcManagerLite/" + CurrentVersion();
                    client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                    client.DownloadFile(info.DownloadUrl, path);
                }
                downloadedPath = path;
                if (progress != null)
                {
                    progress("正在校验更新文件…");
                }
                FileInfo file = new FileInfo(path);
                if (info.Size > 0 && file.Length != info.Size)
                {
                    throw new InvalidDataException(
                        "下载文件大小与发布清单不一致。");
                }
                string actualHash;
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    actualHash = BitConverter.ToString(sha.ComputeHash(stream))
                        .Replace("-", "")
                        .ToLowerInvariant();
                }
                if (!actualHash.Equals(
                    info.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "更新文件 SHA-256 校验失败。");
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try
                {
                    if (!String.IsNullOrWhiteSpace(downloadedPath))
                    {
                        File.Delete(downloadedPath);
                    }
                }
                catch
                {
                }
                FileLogger.Write("下载更新失败: " + ex);
                return false;
            }
        }

        public static bool StartUpdaterAndExit(string downloadedPath)
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataDirectory))
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                }
                string executable = ShortcutManager.ApplicationExecutablePath();
                int processId = Process.GetCurrentProcess().Id;
                string script = String.Format(
                    "while (Get-Process -Id {0} -ErrorAction SilentlyContinue) {{ " +
                    "Start-Sleep -Milliseconds 250 }}; Start-Sleep -Milliseconds 400; " +
                    "Copy-Item -LiteralPath '{2}' -Destination '{4}' -Force -ErrorAction SilentlyContinue; " +
                    "try {{ Copy-Item -LiteralPath '{1}' -Destination '{2}' -Force }} " +
                    "catch {{ Add-Content -LiteralPath '{3}' -Value $_.Exception.ToString() }}; " +
                    "Start-Process -FilePath '{2}'; " +
                    "if (Test-Path -LiteralPath '{1}') {{ Remove-Item -LiteralPath '{1}' -Force }}",
                    processId,
                    EscapePowerShell(downloadedPath),
                    EscapePowerShell(executable),
                    EscapePowerShell(Path.Combine(AppPaths.DataDirectory, "updater.log")),
                    EscapePowerShell(executable + ".bak"));
                string encoded = Convert.ToBase64String(
                    Encoding.Unicode.GetBytes(script));
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\WindowsPowerShell\v1.0\powershell.exe");
                startInfo.Arguments =
                    "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass " +
                    "-EncodedCommand " + encoded;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Write("启动更新程序失败: " + ex);
                return false;
            }
        }

        private static object GetValue(
            Dictionary<string, object> data,
            string key,
            object fallback)
        {
            object value;
            return data.TryGetValue(key, out value) ? value : fallback;
        }

        private static string NormalizeVersion(string version)
        {
            string value = (version ?? String.Empty).Trim();
            if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(1);
            }
            int dash = value.IndexOf('-');
            return dash >= 0 ? value.Substring(0, dash) : value;
        }

        private static string EscapePowerShell(string value)
        {
            return (value ?? String.Empty).Replace("'", "''");
        }
    }

    internal static class AppLifecycleService
    {
        public static bool RelaunchAfterExit()
        {
            try
            {
                string executable = ShortcutManager.ApplicationExecutablePath();
                int processId = Process.GetCurrentProcess().Id;
                string script = String.Format(
                    "while (Get-Process -Id {0} -ErrorAction SilentlyContinue) {{ " +
                    "Start-Sleep -Milliseconds 250 }}; Start-Sleep -Milliseconds 300; " +
                    "Start-Process -FilePath '{1}'",
                    processId,
                    executable.Replace("'", "''"));
                string encoded = Convert.ToBase64String(
                    Encoding.Unicode.GetBytes(script));
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    @"System32\WindowsPowerShell\v1.0\powershell.exe");
                startInfo.Arguments =
                    "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass " +
                    "-EncodedCommand " + encoded;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Write("重启管家失败: " + ex);
                return false;
            }
        }
    }

    internal enum StartupSourceType
    {
        Registry,
        StartupFolder,
        Service
    }

    internal sealed class StartupItem
    {
        public string Id;
        public string Name;
        public string Command;
        public string Location;
        public StartupSourceType SourceType;
        public string SourceLabel;
        public bool CanDisable;
        public string RiskLabel;
        public string Note;
        public RegistryHive RegistryHive;
        public RegistryView RegistryView;
        public string RegistrySubKey;
        public string RegistryValueName;
        public RegistryValueKind RegistryValueKind;
        public object RegistryValue;
        public string StartupFilePath;
        public string ServiceName;
        public string ServiceStartMode;
    }

    internal sealed class StartupActionResult
    {
        public int Changed;
        public int Failed;
        public readonly List<string> Messages = new List<string>();
    }

    internal sealed class StartupBackupRecord
    {
        public string Id;
        public StartupSourceType SourceType;
        public string Name;
        public string Command;
        public string RegistryHive;
        public string RegistryView;
        public string RegistrySubKey;
        public string RegistryValueName;
        public string RegistryKind;
        public string RegistryValue;
        public string OriginalFilePath;
        public string QuarantineFilePath;
        public string ServiceName;
        public string ServiceStartMode;

        public string Serialize()
        {
            string[] fields =
            {
                SourceType.ToString(),
                Encode(Id),
                Encode(Name),
                Encode(Command),
                Encode(RegistryHive),
                Encode(RegistryView),
                Encode(RegistrySubKey),
                Encode(RegistryValueName),
                Encode(RegistryKind),
                Encode(RegistryValue),
                Encode(OriginalFilePath),
                Encode(QuarantineFilePath),
                Encode(ServiceName),
                Encode(ServiceStartMode)
            };
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(String.Join("|", fields)));
        }

        public static StartupBackupRecord Deserialize(string encoded)
        {
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            string[] fields = text.Split('|');
            if (fields.Length < 14)
            {
                throw new InvalidDataException("Invalid startup backup record.");
            }
            StartupBackupRecord record = new StartupBackupRecord();
            record.SourceType = (StartupSourceType)Enum.Parse(
                typeof(StartupSourceType),
                fields[0]);
            record.Id = Decode(fields[1]);
            record.Name = Decode(fields[2]);
            record.Command = Decode(fields[3]);
            record.RegistryHive = Decode(fields[4]);
            record.RegistryView = Decode(fields[5]);
            record.RegistrySubKey = Decode(fields[6]);
            record.RegistryValueName = Decode(fields[7]);
            record.RegistryKind = Decode(fields[8]);
            record.RegistryValue = Decode(fields[9]);
            record.OriginalFilePath = Decode(fields[10]);
            record.QuarantineFilePath = Decode(fields[11]);
            record.ServiceName = Decode(fields[12]);
            record.ServiceStartMode = Decode(fields[13]);
            return record;
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string Decode(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }

    internal static class StartupManagerService
    {
        private static readonly string[] ProtectedTerms =
        {
            "securityhealth", "defender", "antivirus", "antimalware",
            "easyanticheat", "battleye", "ace-tray", "anticheatexpert",
            "iflyinput", "realtek", "audio", "nvidia", "amd", "intel graphics",
            "codex-windows-sandbox-service", "openai.codex"
        };

        private static string BackupFile
        {
            get { return Path.Combine(AppPaths.DataDirectory, "startup-backup.txt"); }
        }

        public static List<StartupItem> Scan()
        {
            List<StartupItem> items = new List<StartupItem>();
            ScanRegistryRun(
                items,
                RegistryHive.CurrentUser,
                RegistryView.Default,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "注册表 · 当前用户");
            ScanRegistryRun(
                items,
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "注册表 · 所有用户");
            ScanRegistryRun(
                items,
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                "注册表 · 32 位");
            ScanStartupFolder(
                items,
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                "启动文件夹 · 当前用户");
            ScanStartupFolder(
                items,
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                "启动文件夹 · 所有用户");
            ScanServices(items);

            List<StartupItem> result = new List<StartupItem>();
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StartupItem item in items)
            {
                if (!String.IsNullOrWhiteSpace(item.Id) && ids.Add(item.Id))
                {
                    result.Add(item);
                }
            }
            result.Sort(delegate(StartupItem left, StartupItem right)
            {
                int protection = right.CanDisable.CompareTo(left.CanDisable);
                if (protection != 0)
                {
                    return protection;
                }
                return String.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public static StartupActionResult Disable(List<StartupItem> items)
        {
            StartupActionResult result = new StartupActionResult();
            List<StartupBackupRecord> backups = LoadBackups();
            foreach (StartupItem item in items)
            {
                if (!item.CanDisable)
                {
                    result.Failed++;
                    result.Messages.Add(item.Name + "：受保护，未修改");
                    continue;
                }
                try
                {
                    StartupBackupRecord backup = BuildBackup(item);
                    if (item.SourceType == StartupSourceType.Registry)
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                            item.RegistryHive,
                            item.RegistryView))
                        using (RegistryKey key = baseKey.OpenSubKey(item.RegistrySubKey, true))
                        {
                            if (key == null)
                            {
                                throw new InvalidOperationException("注册表启动项不存在");
                            }
                            key.DeleteValue(item.RegistryValueName, false);
                        }
                    }
                    else if (item.SourceType == StartupSourceType.StartupFolder)
                    {
                        string quarantineDirectory = Path.Combine(
                            AppPaths.DataDirectory,
                            "startup-quarantine");
                        if (!Directory.Exists(quarantineDirectory))
                        {
                            Directory.CreateDirectory(quarantineDirectory);
                        }
                        string target = Path.Combine(
                            quarantineDirectory,
                            Guid.NewGuid().ToString("N") + "_" +
                            Path.GetFileName(item.StartupFilePath));
                        File.Move(item.StartupFilePath, target);
                        backup.QuarantineFilePath = target;
                    }
                    else
                    {
                        string output;
                        int code;
                        RunCommand(
                            "sc.exe",
                            "config \"" + item.ServiceName + "\" start= disabled",
                            out output,
                            out code);
                        if (code != 0)
                        {
                            throw new InvalidOperationException(output.Trim());
                        }
                    }

                    backups.RemoveAll(delegate(StartupBackupRecord existing)
                    {
                        return String.Equals(
                            existing.Id,
                            backup.Id,
                            StringComparison.OrdinalIgnoreCase);
                    });
                    backups.Add(backup);
                    SaveBackups(backups);
                    result.Changed++;
                    result.Messages.Add("已禁用 " + item.Name);
                    FileLogger.Write("禁用启动项: " + item.Name + " @ " + item.Location);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Messages.Add(item.Name + "：" + ex.Message);
                    FileLogger.Write("禁用启动项失败: " + item.Name + " - " + ex);
                }
            }
            return result;
        }

        public static StartupActionResult RestoreAll()
        {
            StartupActionResult result = new StartupActionResult();
            List<StartupBackupRecord> backups = LoadBackups();
            foreach (StartupBackupRecord backup in backups.ToArray())
            {
                try
                {
                    if (backup.SourceType == StartupSourceType.Registry)
                    {
                        RegistryHive hive = (RegistryHive)Enum.Parse(
                            typeof(RegistryHive),
                            backup.RegistryHive);
                        RegistryView view = (RegistryView)Enum.Parse(
                            typeof(RegistryView),
                            backup.RegistryView);
                        RegistryValueKind kind = (RegistryValueKind)Enum.Parse(
                            typeof(RegistryValueKind),
                            backup.RegistryKind);
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey key = baseKey.CreateSubKey(backup.RegistrySubKey))
                        {
                            if (key == null)
                            {
                                throw new InvalidOperationException("无法打开注册表启动项");
                            }
                            key.SetValue(backup.RegistryValueName, backup.RegistryValue, kind);
                        }
                    }
                    else if (backup.SourceType == StartupSourceType.StartupFolder)
                    {
                        if (!File.Exists(backup.QuarantineFilePath))
                        {
                            throw new FileNotFoundException("启动文件备份不存在");
                        }
                        string directory = Path.GetDirectoryName(backup.OriginalFilePath);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.Copy(backup.QuarantineFilePath, backup.OriginalFilePath, true);
                        File.Delete(backup.QuarantineFilePath);
                    }
                    else
                    {
                        string startMode = String.IsNullOrWhiteSpace(backup.ServiceStartMode)
                            ? "auto"
                            : backup.ServiceStartMode.ToLowerInvariant();
                        string output;
                        int code;
                        RunCommand(
                            "sc.exe",
                            "config \"" + backup.ServiceName + "\" start= " + startMode,
                            out output,
                            out code);
                        if (code != 0)
                        {
                            throw new InvalidOperationException(output.Trim());
                        }
                    }
                    backups.Remove(backup);
                    result.Changed++;
                    result.Messages.Add("已恢复 " + backup.Name);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Messages.Add(backup.Name + "：" + ex.Message);
                }
            }
            SaveBackups(backups);
            FileLogger.Write(
                "恢复启动项: 成功 " + result.Changed + ", 失败 " + result.Failed);
            return result;
        }

        public static int DisabledBackupCount()
        {
            return LoadBackups().Count;
        }

        private static void ScanRegistryRun(
            List<StartupItem> items,
            RegistryHive hive,
            RegistryView view,
            string subKey,
            string sourceLabel)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey key = baseKey.OpenSubKey(subKey, false))
                {
                    if (key == null)
                    {
                        return;
                    }
                    foreach (string valueName in key.GetValueNames())
                    {
                        try
                        {
                            object value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                            RegistryValueKind kind = key.GetValueKind(valueName);
                            string command = value == null
                                ? String.Empty
                                : Environment.ExpandEnvironmentVariables(Convert.ToString(value));
                            StartupItem item = new StartupItem();
                            item.Id = String.Format(
                                "registry|{0}|{1}|{2}|{3}",
                                hive,
                                view,
                                subKey,
                                valueName);
                            item.Name = valueName;
                            item.Command = command;
                            item.Location = hive + "\\" + subKey;
                            item.SourceType = StartupSourceType.Registry;
                            item.SourceLabel = sourceLabel;
                            item.RegistryHive = hive;
                            item.RegistryView = view;
                            item.RegistrySubKey = subKey;
                            item.RegistryValueName = valueName;
                            item.RegistryValueKind = kind;
                            item.RegistryValue = Convert.ToString(value);
                            ApplyRisk(item);
                            items.Add(item);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void ScanStartupFolder(
            List<StartupItem> items,
            string directory,
            string sourceLabel)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }
            try
            {
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (Path.GetFileName(file).Equals(
                        "desktop.ini",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    StartupItem item = new StartupItem();
                    item.Id = "folder|" + file.ToLowerInvariant();
                    item.Name = Path.GetFileNameWithoutExtension(file);
                    item.StartupFilePath = file;
                    item.Command = ResolveShortcut(file);
                    item.Location = file;
                    item.SourceType = StartupSourceType.StartupFolder;
                    item.SourceLabel = sourceLabel;
                    ApplyRisk(item);
                    items.Add(item);
                }
            }
            catch
            {
            }
        }

        private static void ScanServices(List<StartupItem> items)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, DisplayName, StartMode, StartName, PathName, State " +
                    "FROM Win32_Service WHERE StartMode='Auto'"))
                using (ManagementObjectCollection services = searcher.Get())
                {
                    foreach (ManagementObject service in services)
                    {
                        try
                        {
                            string name = Convert.ToString(service["Name"]);
                            string display = Convert.ToString(service["DisplayName"]);
                            string path = Convert.ToString(service["PathName"]);
                            string startMode = Convert.ToString(service["StartMode"]);
                            if (String.IsNullOrWhiteSpace(path))
                            {
                                continue;
                            }
                            string windows = Environment.GetFolderPath(
                                Environment.SpecialFolder.Windows);
                            if (PathLooksInside(path, windows))
                            {
                                continue;
                            }
                            if (name.Equals(
                                "PcManagerLiteService",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            StartupItem item = new StartupItem();
                            item.Id = "service|" + name.ToLowerInvariant();
                            item.Name = String.IsNullOrWhiteSpace(display) ? name : display;
                            item.Command = path;
                            item.Location = "服务 · " + name;
                            item.SourceType = StartupSourceType.Service;
                            item.SourceLabel = "自动服务";
                            item.ServiceName = name;
                            item.ServiceStartMode = startMode;
                            ApplyRisk(item);
                            if (item.CanDisable)
                            {
                                item.RiskLabel = "高影响";
                                item.Note = "开机自动服务；禁用启动不会结束当前进程";
                            }
                            items.Add(item);
                        }
                        finally
                        {
                            service.Dispose();
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void ApplyRisk(StartupItem item)
        {
            string text = ((item.Name ?? String.Empty) + " " +
                (item.Command ?? String.Empty)).ToLowerInvariant();
            string currentExecutable =
                ShortcutManager.ApplicationExecutablePath().ToLowerInvariant();
            if (!String.IsNullOrWhiteSpace(currentExecutable) &&
                text.Contains(currentExecutable))
            {
                item.CanDisable = false;
                item.RiskLabel = "本工具";
                item.Note = "请在设置页管理“随 Windows 启动”";
                return;
            }
            bool protectedItem = false;
            foreach (string term in ProtectedTerms)
            {
                if (text.Contains(term))
                {
                    protectedItem = true;
                    break;
                }
            }
            item.CanDisable = !protectedItem;
            if (protectedItem)
            {
                item.RiskLabel = "受保护";
                item.Note = "系统安全、输入法或硬件组件，默认不允许禁用";
            }
            else if (item.SourceType == StartupSourceType.Service)
            {
                item.RiskLabel = "高影响";
                item.Note = "第三方开机服务；禁用启动不会结束当前进程";
            }
            else if (item.SourceType == StartupSourceType.StartupFolder)
            {
                item.RiskLabel = "按需";
                item.Note = "启动文件夹程序；禁用后文件会移入本工具隔离区";
            }
            else
            {
                item.RiskLabel = "常用";
                item.Note = "登录后会启动；不影响卸载或程序本体";
            }
        }

        private static StartupBackupRecord BuildBackup(StartupItem item)
        {
            StartupBackupRecord backup = new StartupBackupRecord();
            backup.Id = item.Id;
            backup.SourceType = item.SourceType;
            backup.Name = item.Name;
            backup.Command = item.Command;
            if (item.SourceType == StartupSourceType.Registry)
            {
                backup.RegistryHive = item.RegistryHive.ToString();
                backup.RegistryView = item.RegistryView.ToString();
                backup.RegistrySubKey = item.RegistrySubKey;
                backup.RegistryValueName = item.RegistryValueName;
                backup.RegistryKind = item.RegistryValueKind.ToString();
                backup.RegistryValue = Convert.ToString(item.RegistryValue);
            }
            else if (item.SourceType == StartupSourceType.StartupFolder)
            {
                backup.OriginalFilePath = item.StartupFilePath;
            }
            else
            {
                backup.ServiceName = item.ServiceName;
                backup.ServiceStartMode = item.ServiceStartMode;
            }
            return backup;
        }

        private static List<StartupBackupRecord> LoadBackups()
        {
            List<StartupBackupRecord> records = new List<StartupBackupRecord>();
            try
            {
                if (!File.Exists(BackupFile))
                {
                    return records;
                }
                foreach (string line in File.ReadAllLines(BackupFile, Encoding.UTF8))
                {
                    if (String.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    try
                    {
                        records.Add(StartupBackupRecord.Deserialize(line.Trim()));
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return records;
        }

        private static void SaveBackups(List<StartupBackupRecord> records)
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataDirectory))
                {
                    Directory.CreateDirectory(AppPaths.DataDirectory);
                }
                List<string> lines = new List<string>();
                foreach (StartupBackupRecord record in records)
                {
                    lines.Add(record.Serialize());
                }
                File.WriteAllLines(BackupFile, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                FileLogger.Write("保存启动项备份失败: " + ex.Message);
            }
        }

        private static string ResolveShortcut(string path)
        {
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return path;
                }
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { path });
                object target = shortcut.GetType().InvokeMember(
                    "TargetPath",
                    BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null);
                return target == null ? path : Convert.ToString(target);
            }
            catch
            {
                return path;
            }
        }

        private static bool PathLooksInside(string path, string directory)
        {
            if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(directory))
            {
                return false;
            }
            string normalizedDirectory =
                directory.TrimEnd('\\') + "\\";
            return path.Trim('"').StartsWith(
                normalizedDirectory,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void RunCommand(
            string fileName,
            string arguments,
            out string output,
            out int exitCode)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = fileName;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.StandardOutputEncoding = Encoding.Default;
            startInfo.StandardErrorEncoding = Encoding.Default;
            using (Process process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(6000);
                output = stdout + (String.IsNullOrWhiteSpace(stderr) ? "" : "\r\n" + stderr);
                exitCode = process.HasExited ? process.ExitCode : -1;
            }
        }
    }

    internal sealed class InstalledProgram
    {
        public string Name;
        public string Publisher;
        public string Version;
        public string InstallLocation;
        public string UninstallString;
        public string QuietUninstallString;
        public string InstallDate;
        public long SizeBytes;
        public bool CanUninstall;
        public bool UninstallerMissing;
        public RegistryHive Hive;
        public RegistryView View;
        public string KeyName;
    }

    internal sealed class UninstallLaunchResult
    {
        public bool Started;
        public int ProcessId;
        public string Error;
        public string ExecutablePath;
    }

    internal static class InstalledProgramService
    {
        public static List<InstalledProgram> Scan()
        {
            Dictionary<string, InstalledProgram> found =
                new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Collect(found, RegistryHive.LocalMachine, RegistryView.Registry64);
                Collect(found, RegistryHive.LocalMachine, RegistryView.Registry32);
                Collect(found, RegistryHive.CurrentUser, RegistryView.Default);
            }
            catch (Exception ex)
            {
                FileLogger.Write("扫描已安装程序失败: " + ex.Message);
            }
            List<InstalledProgram> items = new List<InstalledProgram>(found.Values);
            items.Sort(delegate(InstalledProgram left, InstalledProgram right)
            {
                return String.Compare(
                    left.Name,
                    right.Name,
                    StringComparison.CurrentCultureIgnoreCase);
            });
            return items;
        }

        public static UninstallLaunchResult LaunchUninstall(InstalledProgram program)
        {
            UninstallLaunchResult result = new UninstallLaunchResult();
            if (program == null || String.IsNullOrWhiteSpace(program.UninstallString))
            {
                result.Error = "该程序没有提供卸载命令，可尝试强制移除登记信息。";
                return result;
            }
            try
            {
                string command = program.UninstallString.Trim();
                command = Regex.Replace(
                    command,
                    @"(msiexec(?:\.exe)?\s+)/I",
                    "$1/X",
                    RegexOptions.IgnoreCase);

                string fileName;
                string arguments;
                bool direct = TrySplitCommand(command, out fileName, out arguments);
                if (direct && IsScriptFile(fileName))
                {
                    arguments = "/c \"" + command + "\"";
                    fileName = "cmd.exe";
                    direct = true;
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                if (direct)
                {
                    startInfo.FileName = fileName;
                    startInfo.Arguments = arguments;
                    result.ExecutablePath = fileName;
                }
                else
                {
                    startInfo.FileName = "cmd.exe";
                    startInfo.Arguments = "/c " + command;
                    result.ExecutablePath = "cmd.exe";
                }
                startInfo.UseShellExecute = false;
                startInfo.WorkingDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.System);
                if (direct && Path.IsPathRooted(fileName))
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(fileName);
                        if (!String.IsNullOrWhiteSpace(directory) &&
                            Directory.Exists(directory))
                        {
                            startInfo.WorkingDirectory = directory;
                        }
                    }
                    catch
                    {
                    }
                }
                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result.Error = "卸载程序没有成功启动。";
                        return result;
                    }
                    result.Started = true;
                    result.ProcessId = process.Id;
                }
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("启动卸载程序失败: " + program.Name + " / " + ex);
                return result;
            }
        }

        public static bool ForceRemove(
            InstalledProgram program,
            bool deleteInstallFolder,
            out string message)
        {
            message = String.Empty;
            if (program == null || String.IsNullOrWhiteSpace(program.KeyName))
            {
                message = "没有可删除的注册表卸载项。";
                return false;
            }
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(program.Hive, program.View))
                using (RegistryKey uninstall = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    true))
                {
                    if (uninstall == null)
                    {
                        message = "无法打开卸载登记表。";
                        return false;
                    }
                    uninstall.DeleteSubKeyTree(program.KeyName, false);
                }
            }
            catch (Exception ex)
            {
                message = "删除注册表卸载项失败：" + ex.Message;
                FileLogger.Write("强制移除失败: " + program.Name + " / " + ex);
                return false;
            }

            string folderMessage = String.Empty;
            if (deleteInstallFolder)
            {
                SafeDeleteInstallFolder(program, out folderMessage);
            }
            message = String.Format(
                "已移除「{0}」的卸载登记{1}。",
                program.Name,
                String.IsNullOrWhiteSpace(folderMessage) ? "" : "；" + folderMessage);
            FileLogger.Write("强制移除程序: " + program.Name + " / " + message);
            return true;
        }

        private static void SafeDeleteInstallFolder(
            InstalledProgram program,
            out string message)
        {
            message = String.Empty;
            string raw = program.InstallLocation;
            if (String.IsNullOrWhiteSpace(raw))
            {
                message = "安装目录未登记，未删除文件";
                return;
            }
            try
            {
                string full = Path.GetFullPath(raw.Trim().Trim('"')).TrimEnd('\\');
                string root = Path.GetPathRoot(full);
                if (String.IsNullOrWhiteSpace(root) ||
                    String.Equals(
                        full.TrimEnd('\\'),
                        root.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(full))
                {
                    message = "安装目录不可用，未删除文件";
                    return;
                }
                string[] protectedPaths =
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads"
                };
                foreach (string protectedPath in protectedPaths)
                {
                    if (!String.IsNullOrWhiteSpace(protectedPath) &&
                        String.Equals(
                            full.TrimEnd('\\'),
                            protectedPath.TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        message = "安装目录属于系统目录，未删除文件";
                        return;
                    }
                }
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                {
                    message = "安装目录是符号链接，未删除文件";
                    return;
                }
                Directory.Delete(full, true);
                message = "已删除安装目录";
            }
            catch (Exception ex)
            {
                message = "删除安装目录失败：" + ex.Message;
            }
        }

        public static bool OpenInstallLocation(InstalledProgram program, out string error)
        {
            error = String.Empty;
            try
            {
                string path = program == null ? null : program.InstallLocation;
                if (String.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    error = "没有可用的安装目录。";
                    return false;
                }
                Process.Start("explorer.exe", "\"" + path.Trim('"') + "\"");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void Collect(
            Dictionary<string, InstalledProgram> found,
            RegistryHive hive,
            RegistryView view)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey uninstall = baseKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                false))
            {
                if (uninstall == null)
                {
                    return;
                }
                foreach (string subKeyName in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey item = uninstall.OpenSubKey(subKeyName, false))
                        {
                            if (item == null)
                            {
                                continue;
                            }
                            string name = ReadString(item, "DisplayName");
                            if (String.IsNullOrWhiteSpace(name))
                            {
                                continue;
                            }
                            if (ReadInt(item, "SystemComponent") == 1 ||
                                ReadInt(item, "NoDisplay") == 1)
                            {
                                continue;
                            }
                            if (!String.IsNullOrWhiteSpace(ReadString(item, "ParentKeyName")))
                            {
                                continue;
                            }

                            string version = ReadString(item, "DisplayVersion");
                            string key = name.Trim() + "|" + version.Trim();
                            if (found.ContainsKey(key))
                            {
                                continue;
                            }

                            InstalledProgram program = new InstalledProgram();
                            program.Name = name.Trim();
                            program.Version = version;
                            program.Publisher = ReadString(item, "Publisher");
                            program.InstallLocation = ReadString(item, "InstallLocation");
                            program.UninstallString = ReadString(item, "UninstallString");
                            program.QuietUninstallString =
                                ReadString(item, "QuietUninstallString");
                            program.InstallDate = FormatInstallDate(
                                ReadString(item, "InstallDate"));
                            long sizeKb = ReadInt64(item, "EstimatedSize");
                            program.SizeBytes = sizeKb > 0 ? sizeKb * 1024L : 0L;
                            program.CanUninstall =
                                !String.IsNullOrWhiteSpace(program.UninstallString);
                            program.Hive = hive;
                            program.View = view;
                            program.KeyName = subKeyName;
                            program.UninstallerMissing =
                                IsUninstallerMissing(program.UninstallString);
                            found[key] = program;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string ReadString(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);
                return value == null ? String.Empty : Convert.ToString(value).Trim();
            }
            catch
            {
                return String.Empty;
            }
        }

        private static int ReadInt(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static long ReadInt64(RegistryKey key, string name)
        {
            try
            {
                object value = key.GetValue(name);
                return value == null ? 0L : Convert.ToInt64(value);
            }
            catch
            {
                return 0L;
            }
        }

        private static bool TrySplitCommand(
            string command,
            out string fileName,
            out string arguments)
        {
            fileName = String.Empty;
            arguments = String.Empty;
            if (String.IsNullOrWhiteSpace(command))
            {
                return false;
            }
            command = command.Trim();
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = command.IndexOf('"', 1);
                if (end > 0)
                {
                    fileName = command.Substring(1, end - 1).Trim();
                    arguments = command.Substring(end + 1).Trim();
                    return fileName.Length > 0;
                }
            }
            int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0)
            {
                int cut = exe + 4;
                fileName = command.Substring(0, cut).Trim().Trim('"');
                arguments = command.Substring(cut).Trim();
                if (fileName.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "msiexec.exe");
                }
                return true;
            }
            return false;
        }

        private static bool IsScriptFile(string fileName)
        {
            if (String.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }
            string extension = Path.GetExtension(fileName);
            return extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUninstallerMissing(string command)
        {
            if (String.IsNullOrWhiteSpace(command))
            {
                return false;
            }
            string fileName;
            string arguments;
            if (!TrySplitCommand(command, out fileName, out arguments))
            {
                return false;
            }
            if (fileName.IndexOf(':') < 0 && fileName.IndexOf('\\') < 0)
            {
                return false;
            }
            return !File.Exists(fileName);
        }

        private static string FormatInstallDate(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw) || raw.Length != 8)
            {
                return String.Empty;
            }
            return raw.Substring(0, 4) + "-" + raw.Substring(4, 2) + "-" + raw.Substring(6, 2);
        }
    }

    internal sealed class DriveUsage
    {
        public string Name;
        public string Label;
        public string Format;
        public long TotalBytes;
        public long FreeBytes;
        public long UsedBytes;

        public double UsedPercent
        {
            get
            {
                return TotalBytes <= 0 ? 0 : UsedBytes * 100.0 / TotalBytes;
            }
        }
    }

    internal sealed class LargeFileInfo
    {
        public string Path;
        public long SizeBytes;
        public DateTime Modified;
        public string Directory
        {
            get
            {
                try
                {
                    return System.IO.Path.GetDirectoryName(Path);
                }
                catch
                {
                    return String.Empty;
                }
            }
        }
    }

    internal sealed class LargeFileScanResult
    {
        public readonly List<LargeFileInfo> Files = new List<LargeFileInfo>();
        public long ScannedBytes;
        public int ScannedFiles;
        public bool Cancelled;
        public string Error;
    }

    internal static class DiskSpaceService
    {
        public static List<DriveUsage> ScanDrives()
        {
            List<DriveUsage> drives = new List<DriveUsage>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady)
                        {
                            continue;
                        }
                        if (drive.DriveType != DriveType.Fixed &&
                            drive.DriveType != DriveType.Removable)
                        {
                            continue;
                        }
                        DriveUsage usage = new DriveUsage();
                        usage.Name = drive.Name.TrimEnd('\\');
                        usage.Label = String.IsNullOrWhiteSpace(drive.VolumeLabel)
                            ? "本地磁盘"
                            : drive.VolumeLabel.Trim();
                        usage.Format = drive.DriveFormat;
                        usage.TotalBytes = drive.TotalSize;
                        usage.FreeBytes = drive.TotalFreeSpace;
                        usage.UsedBytes = usage.TotalBytes - usage.FreeBytes;
                        drives.Add(usage);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Write("扫描磁盘失败: " + ex.Message);
            }
            return drives;
        }

        public static LargeFileScanResult ScanLargeFiles(
            string root,
            long minimumBytes,
            int maxResults,
            Action<string> progress,
            Func<bool> cancelled)
        {
            LargeFileScanResult result = new LargeFileScanResult();
            if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                result.Error = "目录不存在。";
                return result;
            }
            if (maxResults < 1)
            {
                maxResults = 100;
            }

            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            try
            {
                while (pending.Count > 0)
                {
                    if (cancelled != null && cancelled())
                    {
                        result.Cancelled = true;
                        break;
                    }
                    string directory = pending.Pop();
                    DirectoryInfo info;
                    try
                    {
                        info = new DirectoryInfo(directory);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    if (!String.Equals(directory, root, StringComparison.OrdinalIgnoreCase) &&
                        IsExcludedDirectory(info.Name))
                    {
                        continue;
                    }

                    try
                    {
                        foreach (FileInfo file in info.GetFiles())
                        {
                            result.ScannedFiles++;
                            result.ScannedBytes += file.Length;
                            if (file.Length < minimumBytes)
                            {
                                continue;
                            }
                            LargeFileInfo item = new LargeFileInfo();
                            item.Path = file.FullName;
                            item.SizeBytes = file.Length;
                            item.Modified = file.LastWriteTime;
                            result.Files.Add(item);
                            if (result.Files.Count > maxResults)
                            {
                                result.Files.Sort(CompareBySize);
                                result.Files.RemoveRange(
                                    maxResults,
                                    result.Files.Count - maxResults);
                            }
                        }
                    }
                    catch
                    {
                    }

                    try
                    {
                        foreach (DirectoryInfo child in info.GetDirectories())
                        {
                            if ((child.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }
                            if (IsExcludedDirectory(child.Name))
                            {
                                continue;
                            }
                            pending.Push(child.FullName);
                        }
                    }
                    catch
                    {
                    }

                    if (progress != null && result.ScannedFiles > 0 &&
                        result.ScannedFiles % 1500 == 0)
                    {
                        progress("已扫描 " + result.ScannedFiles + " 个文件，正在继续…");
                    }
                }
                result.Files.Sort(CompareBySize);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("大文件扫描失败: " + ex.Message);
            }
            return result;
        }

        private static int CompareBySize(LargeFileInfo left, LargeFileInfo right)
        {
            return right.SizeBytes.CompareTo(left.SizeBytes);
        }

        private static bool IsExcludedDirectory(string name)
        {
            return name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Recovery", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Config.Msi", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class MemoryModuleInfo
    {
        public long CapacityBytes;
        public string Speed;
        public string Manufacturer;
        public string Slot;
    }

    internal sealed class DiskDeviceInfo
    {
        public string Model;
        public long SizeBytes;
        public string Interface;
        public string MediaType;
        public string Status;
    }

    internal sealed class SystemInfoSnapshot
    {
        public string ComputerName;
        public string UserName;
        public string Manufacturer;
        public string Model;
        public string OsCaption;
        public string OsVersion;
        public string OsBuild;
        public string Architecture;
        public DateTime? InstallDate;
        public DateTime? LastBoot;
        public string CpuName;
        public int CpuCores;
        public int CpuThreads;
        public int CpuClockMhz;
        public string GpuName;
        public long GpuMemoryBytes;
        public long TotalMemoryBytes;
        public readonly List<MemoryModuleInfo> MemoryModules =
            new List<MemoryModuleInfo>();
        public readonly List<DiskDeviceInfo> Disks = new List<DiskDeviceInfo>();

        public TimeSpan Uptime
        {
            get
            {
                return LastBoot.HasValue ? DateTime.Now - LastBoot.Value : TimeSpan.Zero;
            }
        }
    }

    internal static class SystemInfoService
    {
        public static SystemInfoSnapshot Read()
        {
            SystemInfoSnapshot snapshot = new SystemInfoSnapshot();
            snapshot.ComputerName = Environment.MachineName;
            snapshot.UserName = Environment.UserName;
            ReadComputer(snapshot);
            ReadProcessor(snapshot);
            ReadGraphics(snapshot);
            ReadMemory(snapshot);
            ReadDisks(snapshot);
            ReadOperatingSystem(snapshot);
            return snapshot;
        }

        private static void ReadComputer(SystemInfoSnapshot snapshot)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Manufacturer, Model, Name, UserName FROM Win32_ComputerSystem"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        snapshot.Manufacturer = Value(item, "Manufacturer");
                        snapshot.Model = Value(item, "Model");
                        string name = Value(item, "Name");
                        if (!String.IsNullOrWhiteSpace(name))
                        {
                            snapshot.ComputerName = name;
                        }
                        string user = Value(item, "UserName");
                        if (!String.IsNullOrWhiteSpace(user))
                        {
                            snapshot.UserName = user;
                        }
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private static void ReadProcessor(SystemInfoSnapshot snapshot)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed " +
                    "FROM Win32_Processor"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        snapshot.CpuName = Value(item, "Name");
                        snapshot.CpuCores = (int)Number(item, "NumberOfCores");
                        snapshot.CpuThreads = (int)Number(item, "NumberOfLogicalProcessors");
                        snapshot.CpuClockMhz = (int)Number(item, "MaxClockSpeed");
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private static void ReadGraphics(SystemInfoSnapshot snapshot)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        string name = Value(item, "Name");
                        if (String.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }
                        if (String.IsNullOrWhiteSpace(snapshot.GpuName))
                        {
                            snapshot.GpuName = name;
                            snapshot.GpuMemoryBytes = Number(item, "AdapterRAM");
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void ReadMemory(SystemInfoSnapshot snapshot)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Capacity, Speed, Manufacturer, DeviceLocator " +
                    "FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        MemoryModuleInfo module = new MemoryModuleInfo();
                        module.CapacityBytes = Number(item, "Capacity");
                        module.Speed = Value(item, "Speed");
                        module.Manufacturer = Value(item, "Manufacturer");
                        module.Slot = Value(item, "DeviceLocator");
                        snapshot.MemoryModules.Add(module);
                        snapshot.TotalMemoryBytes += module.CapacityBytes;
                    }
                }
            }
            catch
            {
            }
            if (snapshot.TotalMemoryBytes <= 0)
            {
                snapshot.TotalMemoryBytes = NativeSystem.GetMemory().TotalPhysical;
            }
        }

        private static void ReadDisks(SystemInfoSnapshot snapshot)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Model, Size, InterfaceType, MediaType, Status FROM Win32_DiskDrive"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        DiskDeviceInfo disk = new DiskDeviceInfo();
                        disk.Model = Value(item, "Model");
                        disk.SizeBytes = Number(item, "Size");
                        disk.Interface = Value(item, "InterfaceType");
                        disk.MediaType = Value(item, "MediaType");
                        disk.Status = Value(item, "Status");
                        if (!String.IsNullOrWhiteSpace(disk.Model) || disk.SizeBytes > 0)
                        {
                            snapshot.Disks.Add(disk);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void ReadOperatingSystem(SystemInfoSnapshot snapshot)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT Caption, Version, BuildNumber, OSArchitecture, " +
                    "InstallDate, LastBootUpTime FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        snapshot.OsCaption = Value(item, "Caption");
                        snapshot.OsVersion = Value(item, "Version");
                        snapshot.OsBuild = Value(item, "BuildNumber");
                        snapshot.Architecture = Value(item, "OSArchitecture");
                        snapshot.InstallDate = Date(item, "InstallDate");
                        snapshot.LastBoot = Date(item, "LastBootUpTime");
                        break;
                    }
                }
            }
            catch
            {
            }
            if (String.IsNullOrWhiteSpace(snapshot.OsCaption))
            {
                snapshot.OsCaption = Environment.OSVersion.VersionString;
            }
        }

        private static string Value(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value == null ? String.Empty : Convert.ToString(value).Trim();
            }
            catch
            {
                return String.Empty;
            }
        }

        private static long Number(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                if (value == null)
                {
                    return 0L;
                }
                if (value is ulong)
                {
                    ulong unsigned = (ulong)value;
                    return unsigned > Int64.MaxValue ? Int64.MaxValue : (long)unsigned;
                }
                return Convert.ToInt64(value);
            }
            catch
            {
                return 0L;
            }
        }

        private static DateTime? Date(ManagementBaseObject item, string name)
        {
            try
            {
                string raw = Value(item, name);
                if (String.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }
                return ManagementDateTimeConverter.ToDateTime(raw);
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class ScheduledCleanupService
    {
        public const string TaskName = "PcManagerLite AutoCleanup";

        public static bool IsEnabled()
        {
            return Run("/Query /TN \"" + TaskName + "\"", 8000) == 0;
        }

        public static bool Enable(out string message)
        {
            message = String.Empty;
            try
            {
                string executable = ShortcutManager.ApplicationExecutablePath();
                if (String.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
                {
                    message = "找不到当前程序路径，无法创建计划任务。";
                    return false;
                }
                string action = "\"" + executable + "\" --auto-clean";
                string arguments = String.Format(
                    "/Create /F /SC WEEKLY /D SUN /ST 12:00 /IT /RL HIGHEST " +
                    "/TN \"{0}\" /TR \"{1}\"",
                    TaskName,
                    action);
                int code = Run(arguments, 20000);
                if (code != 0)
                {
                    message = "创建计划任务失败，退出代码 " + code + "。";
                    return false;
                }
                message = "已启用：每周日 12:00 自动清理临时文件。";
                FileLogger.Write("已创建自动清理计划任务: " + TaskName);
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static bool Disable(out string message)
        {
            message = String.Empty;
            try
            {
                int code = Run("/Delete /F /TN \"" + TaskName + "\"", 12000);
                if (code != 0)
                {
                    message = "删除计划任务失败，退出代码 " + code + "。";
                    return false;
                }
                message = "已关闭每周自动清理。";
                FileLogger.Write("已删除自动清理计划任务: " + TaskName);
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static int Run(string arguments, int timeoutMs)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "schtasks.exe";
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                using (Process process = Process.Start(startInfo))
                {
                    process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                        return -1;
                    }
                    return process.ExitCode;
                }
            }
            catch
            {
                return -1;
            }
        }
    }

    internal sealed class MemorySnapshot
    {
        public long TotalBytes;
        public long AvailableBytes;
        public long SystemCacheBytes;
        public long CommitBytes;
    }

    internal sealed class MemoryCleanResult
    {
        public bool Success;
        public long BeforeAvailable;
        public long AfterAvailable;
        public long FreedBytes;
        public int TrimmedProcesses;
        public bool WorkingSetsCleared;
        public bool ModifiedListFlushed;
        public bool StandbyListPurged;
        public string Error;
    }

    internal static class MemoryOptimizer
    {
        private const int SystemMemoryListInformation = 0x50;
        private const int MemoryEmptyWorkingSets = 2;
        private const int MemoryFlushModifiedList = 3;
        private const int MemoryPurgeStandbyList = 4;

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;

        public static MemorySnapshot GetSnapshot()
        {
            MemorySnapshot snapshot = new MemorySnapshot();
            try
            {
                MEMORYSTATUSEX status = new MEMORYSTATUSEX();
                status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref status))
                {
                    snapshot.TotalBytes = (long)status.ullTotalPhys;
                    snapshot.AvailableBytes = (long)status.ullAvailPhys;
                }
                PERFORMANCE_INFORMATION info = new PERFORMANCE_INFORMATION();
                info.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
                if (GetPerformanceInfo(ref info, info.cb))
                {
                    snapshot.SystemCacheBytes = (long)info.SystemCache.ToUInt64();
                    snapshot.CommitBytes = (long)info.Committed.ToUInt64() *
                        (long)info.PageSize.ToUInt64();
                }
            }
            catch
            {
            }
            return snapshot;
        }

        public static MemoryCleanResult Clean(Action<string> progress)
        {
            MemoryCleanResult result = new MemoryCleanResult();
            try
            {
                MemorySnapshot before = GetSnapshot();
                result.BeforeAvailable = before.AvailableBytes;

                EnablePrivilege("SeProfileSingleProcessPrivilege");
                EnablePrivilege("SeIncreaseQuotaPrivilege");
                EnablePrivilege("SeDebugPrivilege");

                if (progress != null)
                {
                    progress("正在清空进程工作集…");
                }
                int command = MemoryEmptyWorkingSets;
                result.WorkingSetsCleared =
                    NtSetSystemInformation(
                        SystemMemoryListInformation,
                        ref command,
                        sizeof(int)) == 0;
                if (!result.WorkingSetsCleared)
                {
                    result.TrimmedProcesses = TrimProcesses();
                    result.WorkingSetsCleared = result.TrimmedProcesses > 0;
                }

                if (progress != null)
                {
                    progress("正在刷新已修改页面…");
                }
                command = MemoryFlushModifiedList;
                result.ModifiedListFlushed =
                    NtSetSystemInformation(
                        SystemMemoryListInformation,
                        ref command,
                        sizeof(int)) == 0;

                if (progress != null)
                {
                    progress("正在清空待机列表…");
                }
                command = MemoryPurgeStandbyList;
                result.StandbyListPurged =
                    NtSetSystemInformation(
                        SystemMemoryListInformation,
                        ref command,
                        sizeof(int)) == 0;

                NativeSystem.TrimCurrentProcessWorkingSet();
                MemorySnapshot after = GetSnapshot();
                result.AfterAvailable = after.AvailableBytes;
                result.FreedBytes = Math.Max(
                    0,
                    result.AfterAvailable - result.BeforeAvailable);
                result.Success = result.WorkingSetsCleared ||
                    result.ModifiedListFlushed ||
                    result.StandbyListPurged;

                if (progress != null)
                {
                    progress("正在重新读取内存状态…");
                }
                FileLogger.Write(String.Format(
                    "内存加速: 释放 {0}, 工作集={1}, 修改页={2}, 待机={3}, 进程={4}",
                    Format.Bytes(result.FreedBytes),
                    result.WorkingSetsCleared,
                    result.ModifiedListFlushed,
                    result.StandbyListPurged,
                    result.TrimmedProcesses));
                if (!result.Success)
                {
                    result.Error = "系统拒绝了内存整理请求，请以管理员权限运行。";
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("内存加速失败: " + ex);
            }
            return result;
        }

        private static int TrimProcesses()
        {
            int count = 0;
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id <= 4)
                        {
                            continue;
                        }
                        IntPtr handle = OpenProcess(
                            PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA,
                            false,
                            process.Id);
                        if (handle != IntPtr.Zero)
                        {
                            if (EmptyWorkingSet(handle))
                            {
                                count++;
                            }
                            CloseHandle(handle);
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
            }
            return count;
        }

        private static bool EnablePrivilege(string name)
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(
                    GetCurrentProcess(),
                    TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                    out token))
                {
                    return false;
                }
                LUID luid;
                if (!LookupPrivilegeValue(null, name, out luid))
                {
                    return false;
                }
                TOKEN_PRIVILEGES privileges = new TOKEN_PRIVILEGES();
                privileges.PrivilegeCount = 1;
                privileges.Luid = luid;
                privileges.Attributes = (int)SE_PRIVILEGE_ENABLED;
                bool ok = AdjustTokenPrivileges(
                    token,
                    false,
                    ref privileges,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return ok && Marshal.GetLastWin32Error() == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public UIntPtr CommitTotal;
            public UIntPtr CommitLimit;
            public UIntPtr Committed;
            public UIntPtr PhysicalTotal;
            public UIntPtr PhysicalAvailable;
            public UIntPtr SystemCache;
            public UIntPtr KernelTotal;
            public UIntPtr KernelPaged;
            public UIntPtr KernelNonpaged;
            public UIntPtr PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID Luid;
            public int Attributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetPerformanceInfo(
            ref PERFORMANCE_INFORMATION info,
            uint size);

        [DllImport("ntdll.dll")]
        private static extern int NtSetSystemInformation(
            int infoClass,
            ref int info,
            int length);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint access,
            bool inherit,
            int processId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(
            IntPtr process,
            uint access,
            out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(
            string system,
            string name,
            out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr token,
            bool disableAll,
            ref TOKEN_PRIVILEGES newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);
    }

    internal sealed class DefenderStatus
    {
        public bool Available;
        public bool AntivirusEnabled;
        public bool RealTimeProtectionEnabled;
        public bool AmServiceEnabled;
        public string ProductName;
        public string SignatureVersion;
        public DateTime? SignatureUpdated;
        public DateTime? LastQuickScan;
        public DateTime? LastFullScan;
        public string Error;
    }

    internal sealed class DefenderThreat
    {
        public long ThreatId;
        public string Name;
        public string Severity;
        public DateTime? DetectedAt;
        public string Status;
        public string Resources;
    }

    internal sealed class DefenderScanResult
    {
        public bool Success;
        public int ExitCode;
        public string Error;
    }

    internal static class DefenderService
    {
        private static readonly object ScanSync = new object();
        private static Process _scanProcess;

        public static DefenderStatus ReadStatus()
        {
            DefenderStatus status = new DefenderStatus();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"\\.\root\Microsoft\Windows\Defender",
                    "SELECT * FROM MSFT_MpComputerStatus"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        status.Available = true;
                        status.AntivirusEnabled = Bool(item, "AntivirusEnabled");
                        status.RealTimeProtectionEnabled =
                            Bool(item, "RealTimeProtectionEnabled");
                        status.AmServiceEnabled = Bool(item, "AMServiceEnabled");
                        status.ProductName = Text(item, "AMProductVersion");
                        status.SignatureVersion =
                            Text(item, "AntivirusSignatureVersion");
                        status.SignatureUpdated =
                            Date(item, "AntivirusSignatureLastUpdated");
                        status.LastQuickScan = Date(item, "QuickScanEndTime");
                        status.LastFullScan = Date(item, "FullScanEndTime");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                status.Error = ex.Message;
            }
            return status;
        }

        public static List<DefenderThreat> ReadThreats()
        {
            List<DefenderThreat> threats = new List<DefenderThreat>();
            Dictionary<long, string> names = new Dictionary<long, string>();
            Dictionary<long, string> severities = new Dictionary<long, string>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"\\.\root\Microsoft\Windows\Defender",
                    "SELECT ThreatID, ThreatName, SeverityID FROM MSFT_MpThreat"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        long id = (long)Number(item, "ThreatID");
                        names[id] = Text(item, "ThreatName");
                        severities[id] = SeverityName(Number(item, "SeverityID"));
                    }
                }
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"\\.\root\Microsoft\Windows\Defender",
                    "SELECT ThreatID, InitialDetectionTime, ThreatStatusID, Resources " +
                    "FROM MSFT_MpThreatDetection"))
                {
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        DefenderThreat threat = new DefenderThreat();
                        threat.ThreatId = (long)Number(item, "ThreatID");
                        threat.DetectedAt = Date(item, "InitialDetectionTime");
                        threat.Status = StatusName(Number(item, "ThreatStatusID"));
                        string name;
                        if (names.TryGetValue(threat.ThreatId, out name))
                        {
                            threat.Name = name;
                        }
                        else
                        {
                            threat.Name = "Threat " + threat.ThreatId;
                        }
                        string severity;
                        if (severities.TryGetValue(threat.ThreatId, out severity))
                        {
                            threat.Severity = severity;
                        }
                        threat.Resources = FirstResource(item);
                        threats.Add(threat);
                    }
                }
            }
            catch (Exception ex)
            {
                FileLogger.Write("读取 Defender 威胁记录失败: " + ex.Message);
            }
            threats.Sort(delegate(DefenderThreat left, DefenderThreat right)
            {
                DateTime a = left.DetectedAt ?? DateTime.MinValue;
                DateTime b = right.DetectedAt ?? DateTime.MinValue;
                return b.CompareTo(a);
            });
            return threats;
        }

        public static string FindMpCmdRun()
        {
            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            string legacy = Path.Combine(programFiles, @"Windows Defender\MpCmdRun.exe");
            if (File.Exists(legacy))
            {
                return legacy;
            }
            try
            {
                string platform = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows Defender\Platform");
                if (Directory.Exists(platform))
                {
                    string best = null;
                    Version bestVersion = null;
                    foreach (string directory in Directory.GetDirectories(platform))
                    {
                        string candidate = Path.Combine(directory, "MpCmdRun.exe");
                        if (!File.Exists(candidate))
                        {
                            continue;
                        }
                        Version version = null;
                        try
                        {
                            version = new Version(Path.GetFileName(directory));
                        }
                        catch
                        {
                        }
                        if (best == null ||
                            (version != null &&
                             (bestVersion == null || version > bestVersion)))
                        {
                            best = candidate;
                            bestVersion = version;
                        }
                    }
                    return best;
                }
            }
            catch
            {
            }
            return null;
        }

        public static DefenderScanResult Scan(
            bool quick,
            string path,
            Action<string> progress)
        {
            DefenderScanResult result = new DefenderScanResult();
            string executable = FindMpCmdRun();
            if (String.IsNullOrWhiteSpace(executable))
            {
                result.Error = "未找到 Windows Defender 扫描组件（MpCmdRun.exe）。";
                return result;
            }
            string arguments = quick
                ? "-Scan -ScanType 1"
                : "-Scan -ScanType 3 -File \"" + path + "\"";
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.Default;
                startInfo.StandardErrorEncoding = Encoding.Default;

                Process process = new Process();
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!String.IsNullOrWhiteSpace(e.Data) && progress != null)
                    {
                        progress(e.Data.Trim());
                    }
                };
                process.Start();
                lock (ScanSync)
                {
                    _scanProcess = process;
                }
                process.BeginOutputReadLine();
                process.WaitForExit();
                result.ExitCode = process.ExitCode;
                result.Success = process.ExitCode == 0;
                process.Close();
                FileLogger.Write(String.Format(
                    "Defender 扫描完成: quick={0}, path={1}, exit={2}",
                    quick,
                    String.IsNullOrWhiteSpace(path) ? "-" : path,
                    result.ExitCode));
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                FileLogger.Write("Defender 扫描失败: " + ex.Message);
                return result;
            }
            finally
            {
                lock (ScanSync)
                {
                    _scanProcess = null;
                }
            }
        }

        public static bool UpdateSignatures(out string message)
        {
            message = String.Empty;
            string executable = FindMpCmdRun();
            if (String.IsNullOrWhiteSpace(executable))
            {
                message = "未找到 Windows Defender 组件。";
                return false;
            }
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.Arguments = "-SignatureUpdate";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = Encoding.Default;
                startInfo.StandardErrorEncoding = Encoding.Default;
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(180000);
                    bool ok = process.HasExited && process.ExitCode == 0;
                    message = ok
                        ? "病毒库已更新。"
                        : "病毒库更新未完成：" +
                          (String.IsNullOrWhiteSpace(output) ? "未知原因" : output.Trim());
                    return ok;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static bool CancelScan()
        {
            lock (ScanSync)
            {
                if (_scanProcess == null)
                {
                    return false;
                }
                try
                {
                    if (!_scanProcess.HasExited)
                    {
                        _scanProcess.Kill();
                        return true;
                    }
                }
                catch
                {
                }
                return false;
            }
        }

        public static void OpenWindowsSecurity()
        {
            try
            {
                Process.Start("windowsdefender://threat");
            }
            catch
            {
                try
                {
                    Process.Start("ms-settings:windowsdefender");
                }
                catch
                {
                }
            }
        }

        private static string FirstResource(ManagementBaseObject item)
        {
            try
            {
                object value = item["Resources"];
                string[] resources = value as string[];
                if (resources != null && resources.Length > 0)
                {
                    return resources[0];
                }
                return Convert.ToString(value);
            }
            catch
            {
                return String.Empty;
            }
        }

        private static string SeverityName(long severity)
        {
            switch ((int)severity)
            {
                case 1:
                    return "低";
                case 2:
                    return "中";
                case 4:
                    return "高";
                case 5:
                    return "严重";
                default:
                    return "未知";
            }
        }

        private static string StatusName(long status)
        {
            switch ((int)status)
            {
                case 1:
                    return "已检测";
                case 2:
                    return "已清理";
                case 3:
                    return "已隔离";
                case 4:
                    return "已移除";
                case 5:
                    return "已允许";
                case 6:
                    return "已阻止";
                default:
                    return "未知";
            }
        }

        private static string Text(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value == null ? String.Empty : Convert.ToString(value).Trim();
            }
            catch
            {
                return String.Empty;
            }
        }

        private static bool Bool(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value != null && Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        private static long Number(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                return value == null ? 0L : Convert.ToInt64(value);
            }
            catch
            {
                return 0L;
            }
        }

        private static DateTime? Date(ManagementBaseObject item, string name)
        {
            try
            {
                object value = item[name];
                if (value == null)
                {
                    return null;
                }
                DateTime parsed;
                if (value is DateTime)
                {
                    parsed = (DateTime)value;
                }
                else if (!DateTime.TryParse(Convert.ToString(value), out parsed))
                {
                    return null;
                }
                return parsed.Year <= 1900 ? (DateTime?)null : parsed;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class Format
    {
        public static string Bytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }
            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024.0).ToString("0") + " KB";
            }
            if (bytes < 1024L * 1024L * 1024L)
            {
                return (bytes / 1048576.0).ToString("0.0") + " MB";
            }
            return (bytes / 1073741824.0).ToString("0.00") + " GB";
        }

        public static string Milliseconds(double value)
        {
            return value <= 0 ? "-" : value.ToString("0") + " ms";
        }
    }
}
