using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("电脑管家 Lite")]
[assembly: System.Reflection.AssemblyDescription("轻量清理、软件管理、安全防护与游戏加速工具")]
[assembly: System.Reflection.AssemblyProduct("电脑管家 Lite")]
[assembly: System.Reflection.AssemblyCompany("Joker X")]
[assembly: System.Reflection.AssemblyCopyright("Copyright © 2026 Joker X")]
[assembly: System.Reflection.AssemblyTrademark("jokerx.top")]
[assembly: System.Reflection.AssemblyVersion("2.6.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.6.0.0")]

namespace PcManagerLite
{
    internal static class PcManagerProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ManagerSettings startupSettings = ManagerSettings.Load();
            Palette.ApplyMode(startupSettings.Theme);
            Palette.ApplyAccent(startupSettings.Accent);
            string forcedTheme = GetArgumentValue(args, "--theme");
            if (!String.IsNullOrWhiteSpace(forcedTheme))
            {
                Palette.ApplyMode(forcedTheme);
            }

            if (HasArgument(args, "--self-test"))
            {
                string report = GetArgumentValue(args, "--report");
                Environment.ExitCode = SelfTest.Run(report) ? 0 : 1;
                return;
            }

            if (HasArgument(args, "--update-self-test"))
            {
                Environment.ExitCode = UpdateSelfTest(
                    GetArgumentValue(args, "--report"));
                return;
            }

            string disableStartupMatch = GetArgumentValue(args, "--disable-startup");
            if (disableStartupMatch != null)
            {
                Environment.ExitCode = DisableStartupFromCommandLine(
                    disableStartupMatch,
                    GetArgumentValue(args, "--report"));
                return;
            }

            if (HasArgument(args, "--restore-startup"))
            {
                Environment.ExitCode = RestoreStartupFromCommandLine(
                    GetArgumentValue(args, "--report"));
                return;
            }

            if (HasArgument(args, "--auto-clean"))
            {
                Environment.ExitCode = RunAutoClean();
                return;
            }

            if (HasArgument(args, "--memory-clean"))
            {
                Environment.ExitCode = RunMemoryClean(
                    GetArgumentValue(args, "--report"));
                return;
            }

            string screenshot = GetArgumentValue(args, "--screenshot");
            if (screenshot != null)
            {
                string page = GetArgumentValue(args, "--page");
                Environment.ExitCode = SelfTest.SaveScreenshot(
                    screenshot,
                    page,
                    forcedTheme) ? 0 : 1;
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\PcManagerLite.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "电脑管家 Lite 已经在运行。",
                        "电脑管家 Lite",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.Run(new MainForm(
                    startupSettings,
                    HasArgument(args, "--minimized")));
            }
        }

        private static bool HasArgument(string[] args, string name)
        {
            return GetArgumentValue(args, name) != null;
        }

        private static string GetArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (String.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return args[i + 1];
                    }
                    return String.Empty;
                }
            }
            return null;
        }

        private static int DisableStartupFromCommandLine(string match, string reportPath)
        {
            try
            {
                List<StartupItem> selected = new List<StartupItem>();
                foreach (StartupItem item in StartupManagerService.Scan())
                {
                    if (item.CanDisable &&
                        item.Name.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        selected.Add(item);
                    }
                }
                StartupActionResult result = StartupManagerService.Disable(selected);
                StringBuilder report = new StringBuilder();
                report.AppendLine("Matched startup items: " + selected.Count);
                foreach (string message in result.Messages)
                {
                    report.AppendLine(message);
                }
                report.AppendLine("Changed: " + result.Changed);
                report.AppendLine("Failed: " + result.Failed);
                if (String.IsNullOrWhiteSpace(reportPath))
                {
                    reportPath = Path.Combine(
                        Path.GetTempPath(),
                        "PcManagerLite-startup-action.txt");
                }
                string directory = Path.GetDirectoryName(reportPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return result.Failed == 0 && result.Changed > 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                if (!String.IsNullOrWhiteSpace(reportPath))
                {
                    File.WriteAllText(reportPath, ex.ToString(), Encoding.UTF8);
                }
                return 1;
            }
        }

        private static int RestoreStartupFromCommandLine(string reportPath)
        {
            try
            {
                StartupActionResult result = StartupManagerService.RestoreAll();
                StringBuilder report = new StringBuilder();
                foreach (string message in result.Messages)
                {
                    report.AppendLine(message);
                }
                report.AppendLine("Changed: " + result.Changed);
                report.AppendLine("Failed: " + result.Failed);
                if (String.IsNullOrWhiteSpace(reportPath))
                {
                    reportPath = Path.Combine(
                        Path.GetTempPath(),
                        "PcManagerLite-startup-restore.txt");
                }
                string directory = Path.GetDirectoryName(reportPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return result.Failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                if (!String.IsNullOrWhiteSpace(reportPath))
                {
                    File.WriteAllText(reportPath, ex.ToString(), Encoding.UTF8);
                }
                return 1;
            }
        }

        private static int RunAutoClean()
        {
            try
            {
                CleanupScanResult scan = CleanupScanner.ScanAll(null);
                List<CleanupCategory> selected = new List<CleanupCategory>();
                foreach (CleanupCategory category in scan.Categories)
                {
                    if (category.DefaultSelected)
                    {
                        selected.Add(category);
                    }
                }
                CleanupResult result = CleanupScanner.CleanSelected(selected, null);
                FileLogger.Write(String.Format(
                    "计划任务自动清理: 文件 {0}, 大小 {1}, 失败 {2}, 错误 {3}",
                    result.DeletedFiles,
                    result.DeletedBytes,
                    result.FailedItems,
                    result.Error ?? "none"));
                return result.Error == null ? 0 : 1;
            }
            catch (Exception ex)
            {
                FileLogger.Write("计划任务自动清理失败: " + ex);
                return 1;
            }
        }

        private static int RunMemoryClean(string reportPath)
        {
            try
            {
                MemoryCleanResult result = MemoryOptimizer.Clean(null);
                StringBuilder report = new StringBuilder();
                report.AppendLine("Success=" + result.Success);
                report.AppendLine("BeforeAvailable=" + result.BeforeAvailable);
                report.AppendLine("AfterAvailable=" + result.AfterAvailable);
                report.AppendLine("FreedBytes=" + result.FreedBytes);
                report.AppendLine("TrimmedProcesses=" + result.TrimmedProcesses);
                report.AppendLine("WorkingSetsCleared=" + result.WorkingSetsCleared);
                report.AppendLine("ModifiedListFlushed=" + result.ModifiedListFlushed);
                report.AppendLine("StandbyListPurged=" + result.StandbyListPurged);
                report.AppendLine("Error=" + (result.Error ?? "none"));
                if (String.IsNullOrWhiteSpace(reportPath))
                {
                    reportPath = Path.Combine(
                        Path.GetTempPath(),
                        "PcManagerLite-memory-clean.txt");
                }
                string directory = Path.GetDirectoryName(reportPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                FileLogger.Write("内存加速测试失败: " + ex);
                return 1;
            }
        }

        private static int UpdateSelfTest(string reportPath)
        {
            try
            {
                StringBuilder report = new StringBuilder();
                UpdateCheckResult check = UpdateService.Check();
                report.AppendLine("Current=" + check.CurrentVersion);
                report.AppendLine("Latest=" + check.LatestVersion);
                report.AppendLine("Available=" + check.Available);
                report.AppendLine("UpToDate=" + check.UpToDate);
                report.AppendLine("Error=" + (check.Error ?? "none"));
                bool ok = String.IsNullOrWhiteSpace(check.Error) && check.Info != null;
                if (ok)
                {
                    string downloaded;
                    string error;
                    ok = UpdateService.DownloadVerified(
                        check.Info,
                        null,
                        out downloaded,
                        out error);
                    report.AppendLine("DownloadVerified=" + ok);
                    report.AppendLine("DownloadError=" + (error ?? "none"));
                    report.AppendLine("DownloadedPath=" + downloaded);
                    if (ok && File.Exists(downloaded))
                    {
                        string downloadedHash;
                        using (SHA256 sha = SHA256.Create())
                        using (FileStream stream = File.OpenRead(downloaded))
                        {
                            downloadedHash = BitConverter.ToString(sha.ComputeHash(stream))
                                .Replace("-", "")
                                .ToLowerInvariant();
                        }
                        report.AppendLine("DownloadedSHA256=" + downloadedHash);
                        File.Delete(downloaded);
                    }
                }
                if (String.IsNullOrWhiteSpace(reportPath))
                {
                    reportPath = Path.Combine(
                        Path.GetTempPath(),
                        "PcManagerLite-update-test.txt");
                }
                string directory = Path.GetDirectoryName(reportPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                if (!String.IsNullOrWhiteSpace(reportPath))
                {
                    File.WriteAllText(reportPath, ex.ToString(), Encoding.UTF8);
                }
                return 1;
            }
        }
    }

    internal static class Palette
    {
        private static bool _dark;
        private static string _mode = "system";
        private static string _accent = "blue";

        public static readonly string[] AccentNames =
        {
            "blue",
            "teal",
            "purple",
            "orange",
            "red"
        };

        public static bool IsDark
        {
            get { return _dark; }
        }

        public static string AccentName
        {
            get { return _accent; }
        }

        public static void ApplyAccent(string accent)
        {
            _accent = NormalizeAccent(accent);
        }

        public static string NormalizeAccent(string accent)
        {
            string value = String.IsNullOrWhiteSpace(accent)
                ? "blue"
                : accent.Trim().ToLowerInvariant();
            foreach (string name in AccentNames)
            {
                if (String.Equals(name, value, StringComparison.Ordinal))
                {
                    return name;
                }
            }
            return "blue";
        }

        public static string AccentDisplayName(string accent)
        {
            switch (NormalizeAccent(accent))
            {
                case "teal":
                    return "青绿";
                case "purple":
                    return "紫罗兰";
                case "orange":
                    return "琥珀";
                case "red":
                    return "珊瑚";
                default:
                    return "经典蓝";
            }
        }

        public static string Mode
        {
            get { return _mode; }
        }

        public static Color Canvas
        {
            get { return _dark ? Color.FromArgb(24, 32, 41) : Color.FromArgb(243, 246, 249); }
        }

        public static Color Card
        {
            get { return _dark ? Color.FromArgb(33, 42, 51) : Color.White; }
        }

        public static Color Sidebar
        {
            get { return _dark ? Color.FromArgb(17, 24, 32) : Color.FromArgb(14, 36, 58); }
        }

        public static Color SidebarHover
        {
            get { return _dark ? Color.FromArgb(38, 52, 65) : Color.FromArgb(25, 56, 86); }
        }

        public static Color SidebarMuted
        {
            get { return _dark ? Color.FromArgb(154, 170, 184) : Color.FromArgb(169, 192, 214); }
        }

        public static Color Accent
        {
            get { return AccentVariant(0); }
        }

        public static Color AccentDark
        {
            get { return AccentVariant(1); }
        }

        public static Color AccentHover
        {
            get { return AccentVariant(2); }
        }

        public static Color AccentPreview(string accent)
        {
            string previous = _accent;
            bool dark = _dark;
            _accent = NormalizeAccent(accent);
            _dark = false;
            Color color = AccentVariant(0);
            _accent = previous;
            _dark = dark;
            return color;
        }

        private static Color AccentVariant(int variant)
        {
            switch (_accent)
            {
                case "teal":
                    if (_dark)
                    {
                        return variant == 2
                            ? Color.FromArgb(56, 168, 160)
                            : variant == 1
                                ? Color.FromArgb(126, 222, 214)
                                : Color.FromArgb(69, 191, 184);
                    }
                    return variant == 1
                        ? Color.FromArgb(12, 102, 96)
                        : variant == 2
                            ? Color.FromArgb(18, 124, 116)
                            : Color.FromArgb(24, 143, 134);
                case "purple":
                    if (_dark)
                    {
                        return variant == 2
                            ? Color.FromArgb(130, 112, 226)
                            : variant == 1
                                ? Color.FromArgb(186, 174, 255)
                                : Color.FromArgb(150, 135, 245);
                    }
                    return variant == 1
                        ? Color.FromArgb(84, 66, 180)
                        : variant == 2
                            ? Color.FromArgb(92, 76, 196)
                            : Color.FromArgb(108, 92, 214);
                case "orange":
                    if (_dark)
                    {
                        return variant == 2
                            ? Color.FromArgb(222, 142, 52)
                            : variant == 1
                                ? Color.FromArgb(248, 184, 110)
                                : Color.FromArgb(240, 160, 70);
                    }
                    return variant == 1
                        ? Color.FromArgb(168, 86, 10)
                        : variant == 2
                            ? Color.FromArgb(190, 100, 14)
                            : Color.FromArgb(214, 116, 22);
                case "red":
                    if (_dark)
                    {
                        return variant == 2
                            ? Color.FromArgb(224, 100, 132)
                            : variant == 1
                                ? Color.FromArgb(250, 150, 166)
                                : Color.FromArgb(240, 120, 150);
                    }
                    return variant == 1
                        ? Color.FromArgb(158, 36, 64)
                        : variant == 2
                            ? Color.FromArgb(176, 48, 78)
                            : Color.FromArgb(200, 60, 90);
                default:
                    if (_dark)
                    {
                        return variant == 2
                            ? Color.FromArgb(62, 132, 208)
                            : variant == 1
                                ? Color.FromArgb(131, 192, 255)
                                : Color.FromArgb(75, 152, 232);
                    }
                    return variant == 1
                        ? Color.FromArgb(10, 92, 185)
                        : variant == 2
                            ? Color.FromArgb(10, 104, 207)
                            : Color.FromArgb(20, 119, 232);
            }
        }

        public static Color Aqua
        {
            get { return _dark ? Color.FromArgb(69, 191, 184) : Color.FromArgb(24, 143, 134); }
        }

        public static Color Ink
        {
            get { return _dark ? Color.FromArgb(238, 243, 247) : Color.FromArgb(25, 34, 45); }
        }

        public static Color Muted
        {
            get { return _dark ? Color.FromArgb(153, 168, 182) : Color.FromArgb(104, 118, 133); }
        }

        public static Color Border
        {
            get { return _dark ? Color.FromArgb(52, 65, 77) : Color.FromArgb(218, 226, 234); }
        }

        public static Color SoftBlue
        {
            get { return _dark ? Color.FromArgb(36, 53, 69) : Color.FromArgb(235, 244, 253); }
        }

        public static Color Warning
        {
            get { return _dark ? Color.FromArgb(231, 172, 74) : Color.FromArgb(205, 127, 17); }
        }

        public static Color Danger
        {
            get { return _dark ? Color.FromArgb(240, 122, 130) : Color.FromArgb(210, 66, 79); }
        }

        public static Color Success
        {
            get { return _dark ? Color.FromArgb(70, 200, 146) : Color.FromArgb(20, 139, 91); }
        }

        public static readonly Font Body = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8.25F);
        public static readonly Font PageTitle = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold);
        public static readonly Font SectionTitle = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        public static readonly Font Metric = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
        public static readonly Font SidebarBrand = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);

        public static void ApplyMode(string mode)
        {
            _mode = String.IsNullOrWhiteSpace(mode)
                ? "system"
                : mode.Trim().ToLowerInvariant();
            if (_mode == "dark")
            {
                _dark = true;
            }
            else if (_mode == "light")
            {
                _dark = false;
            }
            else
            {
                _dark = IsSystemDark();
                _mode = "system";
            }
        }

        private static bool IsSystemDark()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);
                return Convert.ToInt32(value) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    internal enum ManagerView
    {
        Dashboard,
        Cleanup,
        Programs,
        Security,
        Game,
        Space,
        SystemInfo,
        Network,
        Startup,
        Recovery,
        Settings
    }

    internal enum NavIconKind
    {
        Home,
        Cleanup,
        Programs,
        Shield,
        Game,
        Disk,
        Info,
        Network,
        Startup,
        Recovery,
        Settings
    }

    internal sealed class MainForm : Form
    {
        private readonly Panel _contentHost;
        private readonly Dictionary<ManagerView, Control> _pages =
            new Dictionary<ManagerView, Control>();
        private readonly Dictionary<ManagerView, SidebarButton> _navButtons =
            new Dictionary<ManagerView, SidebarButton>();
        private DashboardPage _dashboardPage;
        private CleanupPage _cleanupPage;
        private ProgramsPage _programsPage;
        private SecurityPage _securityPage;
        private SpacePage _spacePage;
        private SystemInfoPage _systemInfoPage;
        private GamePage _gamePage;
        private NetworkPage _networkPage;
        private StartupPage _startupPage;
        private RecoveryPage _recoveryPage;
        private SettingsPage _settingsPage;
        private Panel _sidebarPanel;
        private readonly ManagerSettings _settings;
        private bool _quickChecking;
        private ManagerView _activeView = ManagerView.Dashboard;

        public MainForm()
            : this(ManagerSettings.Load(), false)
        {
        }

        public MainForm(ManagerSettings settings, bool startMinimized)
        {
            _settings = settings ?? ManagerSettings.Load();
            Text = "电脑管家 Lite";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1200, 790);
            MinimumSize = new Size(1040, 690);
            BackColor = Palette.Canvas;
            ForeColor = Palette.Ink;
            Font = Palette.Body;
            DoubleBuffered = true;

            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = Palette.Canvas;
            _contentHost.Padding = new Padding(22, 18, 22, 18);
            Controls.Add(_contentHost);
            BuildWorkspace();
            HandleCreated += delegate
            {
                NativeSystem.ApplyWindowTheme(Handle, Palette.IsDark);
            };

            Load += delegate
            {
                FitToWorkingArea();
                Navigate(ManagerView.Dashboard);
                UpdateKnownSystemState();
                if (startMinimized)
                {
                    WindowState = FormWindowState.Minimized;
                }
                if (_settings.CheckUpdatesOnStart)
                {
                    _settingsPage.AutoCheck();
                }
            };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    NativeSystem.TrimCurrentProcessWorkingSet();
                }
            };

            // 必须在所有子控件创建完成后设置，否则 WinForms 只会缩放窗体本身，
            // 导致字体按系统 DPI 放大而控件尺寸保持 96 DPI，界面出现挤压和错位。
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        private void BuildWorkspace()
        {
            _navButtons.Clear();
            _sidebarPanel = BuildSidebar();
            Controls.Add(_sidebarPanel);

            _pages.Clear();
            _contentHost.Controls.Clear();
            _dashboardPage = new DashboardPage();
            _cleanupPage = new CleanupPage();
            _programsPage = new ProgramsPage();
            _securityPage = new SecurityPage();
            _spacePage = new SpacePage();
            _systemInfoPage = new SystemInfoPage();
            _gamePage = new GamePage(this);
            _networkPage = new NetworkPage();
            _startupPage = new StartupPage();
            _recoveryPage = new RecoveryPage(this);
            _settingsPage = new SettingsPage(this, _settings);

            _pages[ManagerView.Dashboard] = _dashboardPage;
            _pages[ManagerView.Cleanup] = _cleanupPage;
            _pages[ManagerView.Programs] = _programsPage;
            _pages[ManagerView.Security] = _securityPage;
            _pages[ManagerView.Space] = _spacePage;
            _pages[ManagerView.SystemInfo] = _systemInfoPage;
            _pages[ManagerView.Game] = _gamePage;
            _pages[ManagerView.Network] = _networkPage;
            _pages[ManagerView.Startup] = _startupPage;
            _pages[ManagerView.Recovery] = _recoveryPage;
            _pages[ManagerView.Settings] = _settingsPage;

            foreach (Control page in _pages.Values)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                _contentHost.Controls.Add(page);
            }

            _dashboardPage.NavigateRequested += Navigate;
            _dashboardPage.QuickCheckRequested += StartQuickCheck;
        }

        public void SwitchTheme(string theme)
        {
            _settings.Theme = String.IsNullOrWhiteSpace(theme)
                ? "system"
                : theme.ToLowerInvariant();
            _settings.Save();
            Palette.ApplyMode(_settings.Theme);
            RebuildWorkspace();
        }

        public void SwitchAccent(string accent)
        {
            _settings.Accent = Palette.NormalizeAccent(accent);
            _settings.Save();
            Palette.ApplyAccent(_settings.Accent);
            RebuildWorkspace();
        }

        private void RebuildWorkspace()
        {
            ManagerView previous = _activeView;
            NativeSystem.ApplyWindowTheme(Handle, Palette.IsDark);

            // 主题/强调色切换会重建所有控件，新控件按 96 DPI 创建，
            // 需要以 96 DPI 为基准重新执行一次自动缩放，否则高 DPI 下会错位。
            Size savedClientSize = ClientSize;
            Size savedMinimumSize = MinimumSize;
            SuspendLayout();
            _contentHost.SuspendLayout();
            try
            {
                foreach (Control page in new List<Control>(_pages.Values))
                {
                    _contentHost.Controls.Remove(page);
                    page.Dispose();
                }
                _pages.Clear();
                if (_sidebarPanel != null)
                {
                    Controls.Remove(_sidebarPanel);
                    _sidebarPanel.Dispose();
                    _sidebarPanel = null;
                }

                BackColor = Palette.Canvas;
                ForeColor = Palette.Ink;
                Font = Palette.Body;
                _contentHost.BackColor = Palette.Canvas;
                _contentHost.Padding = new Padding(22, 18, 22, 18);
                AutoScaleDimensions = new SizeF(96F, 96F);
                BuildWorkspace();
            }
            finally
            {
                _contentHost.ResumeLayout(false);
                ResumeLayout(false);
            }

            PerformAutoScale();
            ClientSize = savedClientSize;
            MinimumSize = savedMinimumSize;
            Navigate(previous);
            UpdateKnownSystemState();
            PerformLayout();
        }

        public void Navigate(ManagerView view)
        {
            _activeView = view;
            foreach (KeyValuePair<ManagerView, Control> pair in _pages)
            {
                pair.Value.Visible = pair.Key == view;
            }
            foreach (KeyValuePair<ManagerView, SidebarButton> pair in _navButtons)
            {
                pair.Value.SetActive(pair.Key == view);
            }
            if (view == ManagerView.Programs)
            {
                _programsPage.EnsureLoaded();
            }
            if (view == ManagerView.Security)
            {
                _securityPage.RefreshState();
            }
            if (view == ManagerView.Space)
            {
                _spacePage.RefreshDrives();
            }
            if (view == ManagerView.SystemInfo)
            {
                _systemInfoPage.RefreshInfo();
            }
            if (view == ManagerView.Game)
            {
                _gamePage.RefreshState();
            }
            if (view == ManagerView.Network)
            {
                _networkPage.RefreshAdapter();
            }
            if (view == ManagerView.Startup)
            {
                _startupPage.Scan();
            }
            if (view == ManagerView.Recovery)
            {
                _recoveryPage.RefreshState();
            }
            if (view == ManagerView.Settings)
            {
                _settingsPage.RefreshState();
            }
        }

        public void NavigateForTest(string page)
        {
            if (String.IsNullOrWhiteSpace(page))
            {
                Navigate(ManagerView.Dashboard);
                return;
            }
            if (page.Equals("cleanup", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Cleanup);
                _cleanupPage.Scan();
            }
            else if (page.Equals("programs", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Programs);
            }
            else if (page.Equals("space", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Space);
            }
            else if (page.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.SystemInfo);
            }
            else if (page.Equals("security", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Security);
            }
            else if (page.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Game);
            }
            else if (page.Equals("network", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Network);
            }
            else if (page.Equals("startup", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Startup);
            }
            else if (page.Equals("recovery", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Recovery);
            }
            else if (page.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                Navigate(ManagerView.Settings);
            }
            else
            {
                Navigate(ManagerView.Dashboard);
            }
        }

        private void FitToWorkingArea()
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size chrome = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
            int maxWidth = Math.Max(640, area.Width - chrome.Width);
            int maxHeight = Math.Max(480, area.Height - chrome.Height);
            Size minimum = MinimumSize;
            if (minimum.Width > maxWidth || minimum.Height > maxHeight)
            {
                MinimumSize = new Size(
                    Math.Min(minimum.Width, maxWidth),
                    Math.Min(minimum.Height, maxHeight));
            }
            int width = Math.Min(ClientSize.Width, maxWidth);
            int height = Math.Min(ClientSize.Height, maxHeight);
            if (width != ClientSize.Width || height != ClientSize.Height)
            {
                ClientSize = new Size(width, height);
            }
        }

        public void MinimizeAfterOptimization(bool minimize)
        {
            if (minimize)
            {
                WindowState = FormWindowState.Minimized;
            }
        }

        public void UpdateKnownSystemState()
        {
            MemoryMetrics memory = NativeSystem.GetMemory();
            GameSnapshot games = GameDetector.Snapshot();
            string pageFile = SystemOptimizer.GetPageFileDisplay();
            NetworkReport adapter = NetworkDiagnostics.ReadAdapter();
            _dashboardPage.UpdateKnownState(memory, pageFile, games, adapter);
        }

        private Panel BuildSidebar()
        {
            Panel sidebar = new Panel();
            sidebar.Dock = DockStyle.Left;
            sidebar.Width = 202;
            sidebar.BackColor = Palette.Sidebar;

            Panel brand = new Panel();
            brand.Dock = DockStyle.Top;
            brand.Height = 96;
            brand.Padding = new Padding(22, 20, 10, 8);

            Label mark = new Label();
            mark.Text = "净";
            mark.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            mark.ForeColor = Color.White;
            mark.BackColor = Palette.Accent;
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.Size = new Size(38, 38);
            mark.Location = new Point(22, 23);
            brand.Controls.Add(mark);

            Label name = new Label();
            name.Text = "电脑管家";
            name.Font = Palette.SidebarBrand;
            name.ForeColor = Color.White;
            name.AutoSize = true;
            name.Location = new Point(69, 21);
            brand.Controls.Add(name);

            Label version = new Label();
            version.Text = "轻量版 " + UpdateService.CurrentVersion();
            version.Font = Palette.Small;
            version.ForeColor = Palette.SidebarMuted;
            version.AutoSize = true;
            version.Location = new Point(70, 46);
            brand.Controls.Add(version);
            sidebar.Controls.Add(brand);

            Panel nav = new Panel();
            nav.Dock = DockStyle.Top;
            nav.Height = 544;
            nav.Padding = new Padding(10, 6, 10, 0);

            AddSidebarButton(nav, ManagerView.Dashboard, "首页体检", NavIconKind.Home);
            AddSidebarButton(nav, ManagerView.Cleanup, "垃圾清理", NavIconKind.Cleanup);
            AddSidebarButton(nav, ManagerView.Programs, "软件管理", NavIconKind.Programs);
            AddSidebarButton(nav, ManagerView.Security, "安全防护", NavIconKind.Shield);
            AddSidebarButton(nav, ManagerView.Game, "游戏加速", NavIconKind.Game);
            AddSidebarButton(nav, ManagerView.Space, "空间分析", NavIconKind.Disk);
            AddSidebarButton(nav, ManagerView.SystemInfo, "系统信息", NavIconKind.Info);
            AddSidebarButton(nav, ManagerView.Network, "网络修复", NavIconKind.Network);
            AddSidebarButton(nav, ManagerView.Startup, "启动项管理", NavIconKind.Startup);
            AddSidebarButton(nav, ManagerView.Recovery, "恢复中心", NavIconKind.Recovery);
            AddSidebarButton(nav, ManagerView.Settings, "设置", NavIconKind.Settings);
            sidebar.Controls.Add(nav);
            nav.BringToFront();

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 122;
            footer.Padding = new Padding(20, 10, 20, 16);

            Label privacy = new Label();
            privacy.Dock = DockStyle.Top;
            privacy.Height = 38;
            privacy.Text = "本地运行 · 无数据上传\n点击功能按钮即开始执行";
            privacy.Font = Palette.Small;
            privacy.ForeColor = Palette.SidebarMuted;

            Button shortcut = new Button();
            shortcut.Dock = DockStyle.Bottom;
            shortcut.Height = 32;
            shortcut.Text = "创建桌面快捷方式";
            shortcut.FlatStyle = FlatStyle.Flat;
            shortcut.FlatAppearance.BorderColor = Color.FromArgb(55, 90, 125);
            shortcut.FlatAppearance.BorderSize = 1;
            shortcut.BackColor = Palette.Sidebar;
            shortcut.ForeColor = Color.White;
            shortcut.Font = Palette.Small;
            shortcut.Cursor = Cursors.Hand;
            shortcut.Click += delegate
            {
                try
                {
                    ShortcutManager.CreateDesktopShortcut();
                    MessageBox.Show(
                        this,
                        "桌面快捷方式已创建。",
                        "电脑管家 Lite",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "创建快捷方式失败：\n" + ex.Message,
                        "创建失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };
            footer.Controls.Add(shortcut);
            footer.Controls.Add(privacy);
            sidebar.Controls.Add(footer);
            footer.BringToFront();
            return sidebar;
        }

        private void AddSidebarButton(Panel parent, ManagerView view, string text, NavIconKind icon)
        {
            SidebarButton button = new SidebarButton();
            button.Dock = DockStyle.Top;
            button.Height = 48;
            button.Text = text;
            button.IconKind = icon;
            button.Margin = new Padding(0, 0, 0, 4);
            button.Click += delegate { Navigate(view); };
            parent.Controls.Add(button);
            button.BringToFront();
            _navButtons[view] = button;
        }

        private void StartQuickCheck()
        {
            if (_quickChecking)
            {
                return;
            }
            _quickChecking = true;
            _dashboardPage.BeginQuickCheck();

            ThreadPool.QueueUserWorkItem(delegate
            {
                CleanupScanResult cleanup = null;
                NetworkReport network = null;
                string error = null;
                try
                {
                    cleanup = CleanupScanner.ScanAll(delegate(string text)
                    {
                        SafeBeginInvoke(delegate
                        {
                            _dashboardPage.SetProgress(text);
                        });
                    });
                    network = NetworkDiagnostics.Test(delegate(string text)
                    {
                        SafeBeginInvoke(delegate
                        {
                            _dashboardPage.SetProgress(text);
                        });
                    });
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    FileLogger.Write("一键体检失败: " + ex);
                }

                SafeBeginInvoke(delegate
                {
                    _quickChecking = false;
                    _dashboardPage.CompleteQuickCheck(cleanup, network, error);
                    if (cleanup != null)
                    {
                        _cleanupPage.ApplyScanResult(cleanup);
                    }
                    if (network != null)
                    {
                        _networkPage.ApplyReport(network);
                    }
                    UpdateKnownSystemState();
                    NativeSystem.TrimCurrentProcessWorkingSet();
                });
            });
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class SidebarButton : Control
    {
        private bool _hover;
        private bool _active;
        private bool _keyboardFocus;
        private NavIconKind _iconKind;

        public NavIconKind IconKind
        {
            get { return _iconKind; }
            set
            {
                _iconKind = value;
                Invalidate();
            }
        }

        public SidebarButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable,
                true);
            Cursor = Cursors.Hand;
            Font = Palette.Body;
            ForeColor = Palette.SidebarMuted;
            TabStop = true;
            Height = 52;
        }

        public void SetActive(bool active)
        {
            _active = active;
            ForeColor = active ? Color.White : Palette.SidebarMuted;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _keyboardFocus = false;
            Focus();
            base.OnMouseDown(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            _keyboardFocus = true;
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color background = _active
                ? Palette.Accent
                : _hover ? Palette.SidebarHover : Palette.Sidebar;
            using (GraphicsPath path = Ui.RoundedPath(
                new Rectangle(
                    0,
                    Ui.Scale(e.Graphics, 2),
                    Width - 1,
                    Height - Ui.Scale(e.Graphics, 5)),
                Ui.Scale(e.Graphics, 9)))
            using (SolidBrush brush = new SolidBrush(background))
            {
                e.Graphics.FillPath(brush, path);
            }

            Color iconColor = _active ? Color.White : Palette.SidebarMuted;
            DrawIcon(
                e.Graphics,
                new Rectangle(
                    Ui.Scale(e.Graphics, 17),
                    Ui.Scale(e.Graphics, 17),
                    Ui.Scale(e.Graphics, 18),
                    Ui.Scale(e.Graphics, 18)),
                _iconKind,
                iconColor);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                _active ? Palette.BodyBold : Font,
                new Rectangle(
                    Ui.Scale(e.Graphics, 48),
                    0,
                    Width - Ui.Scale(e.Graphics, 55),
                    Height),
                _active ? Color.White : Palette.SidebarMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused && _keyboardFocus)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    new Rectangle(
                        Ui.Scale(e.Graphics, 4),
                        Ui.Scale(e.Graphics, 5),
                        Width - Ui.Scale(e.Graphics, 9),
                        Height - Ui.Scale(e.Graphics, 11)),
                    Color.White,
                    background);
            }
        }

        private static void DrawIcon(
            Graphics graphics,
            Rectangle box,
            NavIconKind kind,
            Color color)
        {
            float scale = box.Width / 18F;
            if (scale <= 0F)
            {
                scale = 1F;
            }
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(box.Left, box.Top);
            graphics.ScaleTransform(scale, scale);
            box = new Rectangle(0, 0, 18, 18);
            try
            {
                using (Pen pen = new Pen(color, 1.8F))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    if (kind == NavIconKind.Home)
                    {
                        graphics.DrawLines(pen, new Point[]
                        {
                            new Point(box.Left + 2, box.Top + 8),
                            new Point(box.Left + 9, box.Top + 2),
                            new Point(box.Right - 2, box.Top + 8)
                        });
                        graphics.DrawRectangle(
                            pen,
                            box.Left + 4,
                            box.Top + 8,
                            box.Width - 8,
                            box.Height - 9);
                    }
                    else if (kind == NavIconKind.Cleanup)
                    {
                        graphics.DrawRectangle(
                            pen,
                            box.Left + 4,
                            box.Top + 5,
                            box.Width - 8,
                            box.Height - 5);
                        graphics.DrawLine(
                            pen,
                            box.Left + 3,
                            box.Top + 5,
                            box.Right - 3,
                            box.Top + 5);
                        graphics.DrawLine(
                            pen,
                            box.Left + 7,
                            box.Top + 2,
                            box.Right - 7,
                            box.Top + 2);
                        graphics.DrawLine(
                            pen,
                            box.Left + 8,
                            box.Top + 9,
                            box.Left + 8,
                            box.Bottom - 3);
                    }
                    else if (kind == NavIconKind.Game)
                    {
                        graphics.DrawLines(pen, new Point[]
                        {
                            new Point(box.Left + 10, box.Top + 1),
                            new Point(box.Left + 4, box.Top + 10),
                            new Point(box.Left + 9, box.Top + 10),
                            new Point(box.Left + 7, box.Bottom - 1),
                            new Point(box.Right - 3, box.Top + 7),
                            new Point(box.Left + 12, box.Top + 7)
                        });
                    }
                    else if (kind == NavIconKind.Network)
                    {
                        graphics.DrawEllipse(pen, box.Left + 1, box.Top + 7, 5, 5);
                        graphics.DrawEllipse(pen, box.Right - 6, box.Top + 2, 5, 5);
                        graphics.DrawEllipse(pen, box.Right - 6, box.Bottom - 7, 5, 5);
                        graphics.DrawLine(pen, box.Left + 6, box.Top + 10, box.Right - 6, box.Top + 5);
                        graphics.DrawLine(pen, box.Left + 6, box.Top + 10, box.Right - 6, box.Bottom - 5);
                    }
                    else if (kind == NavIconKind.Programs)
                    {
                        graphics.DrawRectangle(pen, box.Left + 2, box.Top + 2, 6, 6);
                        graphics.DrawRectangle(pen, box.Left + 10, box.Top + 2, 6, 6);
                        graphics.DrawRectangle(pen, box.Left + 2, box.Top + 10, 6, 6);
                        graphics.DrawRectangle(pen, box.Left + 10, box.Top + 10, 6, 6);
                    }
                    else if (kind == NavIconKind.Disk)
                    {
                        graphics.DrawRectangle(pen, box.Left + 1, box.Top + 5, box.Width - 2, 9);
                        graphics.DrawLine(
                            pen,
                            box.Left + 1,
                            box.Top + 10,
                            box.Right - 1,
                            box.Top + 10);
                        graphics.DrawEllipse(pen, box.Right - 5, box.Top + 7, 2, 2);
                    }
                    else if (kind == NavIconKind.Info)
                    {
                        graphics.DrawEllipse(pen, box.Left + 2, box.Top + 2, 14, 14);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 5, box.Left + 9, box.Top + 6);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 8, box.Left + 9, box.Top + 13);
                    }
                    else if (kind == NavIconKind.Shield)
                    {
                        graphics.DrawPolygon(pen, new Point[]
                        {
                            new Point(box.Left + 9, box.Top + 1),
                            new Point(box.Right - 2, box.Top + 4),
                            new Point(box.Right - 2, box.Top + 9),
                            new Point(box.Left + 9, box.Bottom - 1),
                            new Point(box.Left + 2, box.Top + 9),
                            new Point(box.Left + 2, box.Top + 4)
                        });
                        graphics.DrawLines(pen, new Point[]
                        {
                            new Point(box.Left + 6, box.Top + 9),
                            new Point(box.Left + 8, box.Top + 12),
                            new Point(box.Left + 12, box.Top + 6)
                        });
                    }
                    else if (kind == NavIconKind.Startup)
                    {
                        graphics.DrawArc(pen, box.Left + 2, box.Top + 2, box.Width - 4, box.Height - 4, 40, 285);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 1, box.Left + 9, box.Top + 7);
                        graphics.DrawLine(pen, box.Left + 13, box.Top + 5, box.Right - 4, box.Top + 5);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 9, box.Left + 9, box.Top + 13);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 13, box.Left + 13, box.Top + 13);
                    }
                    else if (kind == NavIconKind.Recovery)
                    {
                        graphics.DrawArc(pen, box.Left + 3, box.Top + 1, box.Width - 6, box.Height - 2, 205, 310);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 3, box.Left + 9, box.Top + 9);
                        graphics.DrawLine(pen, box.Left + 9, box.Top + 3, box.Right - 4, box.Top + 3);
                        graphics.DrawArc(pen, box.Left + 6, box.Top + 7, 7, 7, 20, 270);
                    }
                    else
                    {
                        graphics.DrawEllipse(pen, box.Left + 4, box.Top + 4, box.Width - 8, box.Height - 8);
                        graphics.DrawEllipse(pen, box.Left + 7, box.Top + 7, box.Width - 14, box.Height - 14);
                        graphics.DrawLine(pen, box.Left + 9, box.Top, box.Left + 9, box.Top + 4);
                        graphics.DrawLine(pen, box.Right - 9, box.Top, box.Right - 9, box.Top + 4);
                        graphics.DrawLine(pen, box.Left + 9, box.Bottom - 4, box.Left + 9, box.Bottom);
                        graphics.DrawLine(pen, box.Right - 9, box.Bottom - 4, box.Right - 9, box.Bottom);
                    }
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public int Radius { get; set; }
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public int BorderWidth { get; set; }

        public RoundedPanel()
        {
            Radius = 12;
            FillColor = Palette.Card;
            BorderColor = Palette.Border;
            BorderWidth = 1;
            DoubleBuffered = true;
            BackColor = Palette.Canvas;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
            using (GraphicsPath path = Ui.RoundedPath(
                new Rectangle(0, 0, Width - 1, Height - 1),
                Ui.Scale(e.Graphics, Radius)))
            using (SolidBrush fill = new SolidBrush(FillColor))
            {
                e.Graphics.FillPath(fill, path);
                if (BorderWidth > 0)
                {
                    using (Pen pen = new Pen(
                        BorderColor,
                        Math.Max(1F, Ui.Scale(e.Graphics, (float)BorderWidth))))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }
        }
    }

    internal sealed class HeroPanel : Panel
    {
        public HeroPanel()
        {
            DoubleBuffered = true;
            BackColor = Palette.Canvas;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
            using (GraphicsPath path = Ui.RoundedPath(
                new Rectangle(0, 0, Width - 1, Height - 1),
                Ui.Scale(e.Graphics, 12)))
            using (SolidBrush brush = new SolidBrush(
                Palette.IsDark ? Color.FromArgb(29, 41, 52) : Color.FromArgb(233, 242, 252)))
            {
                e.Graphics.FillPath(brush, path);
                using (Pen pen = new Pen(Palette.Border, Math.Max(1F, Ui.Scale(e.Graphics, 1F))))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
        }
    }

    internal sealed class HealthRing : Control
    {
        private int _score = -1;
        private string _status = "待体检";
        private string _caption = "运行一次体检了解当前状态";

        public HealthRing()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
        }

        public void SetResult(int score, string status, string caption)
        {
            _score = score;
            _status = status;
            _caption = caption;
            Invalidate();
        }

        public void SetChecking(string text)
        {
            _score = -1;
            _status = "体检中";
            _caption = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Ui.ScaleFactor(e.Graphics);
            int size = Math.Min(Width, Height) - Ui.Scale(e.Graphics, 24);
            Rectangle ring = new Rectangle(
                (Width - size) / 2,
                Ui.Scale(e.Graphics, 10),
                size,
                size);

            Color trackColor = Palette.IsDark
                ? Color.FromArgb(55, 255, 255, 255)
                : Color.FromArgb(30, Palette.Accent);
            using (Pen background = new Pen(trackColor, 10F * scale))
            {
                background.StartCap = LineCap.Round;
                background.EndCap = LineCap.Round;
                e.Graphics.DrawArc(background, ring, -90, 360);
            }

            int value = _score < 0 ? 360 : (int)(360.0 * _score / 100.0);
            using (Pen foreground = new Pen(Palette.Accent, 10F * scale))
            {
                foreground.StartCap = LineCap.Round;
                foreground.EndCap = LineCap.Round;
                e.Graphics.DrawArc(foreground, ring, -90, value);
            }

            string number = _score < 0 ? "—" : _score.ToString();
            using (Font numberFont = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold))
            {
                Rectangle numberArea = new Rectangle(
                    0,
                    ring.Top + Ui.Scale(e.Graphics, 22),
                    Width,
                    Ui.Scale(e.Graphics, 48));
                TextRenderer.DrawText(
                    e.Graphics,
                    number,
                    numberFont,
                    numberArea,
                    Palette.Ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(
                e.Graphics,
                _status,
                Palette.BodyBold,
                new Rectangle(
                    0,
                    ring.Top + Ui.Scale(e.Graphics, 72),
                    Width,
                    Ui.Scale(e.Graphics, 24)),
                Palette.Ink,
                TextFormatFlags.HorizontalCenter);
        }
    }

    internal static class Ui
    {
        public static int Scale(Graphics graphics, int value)
        {
            return (int)Math.Round(value * ScaleFactor(graphics));
        }

        public static float Scale(Graphics graphics, float value)
        {
            return value * ScaleFactor(graphics);
        }

        public static float ScaleFactor(Graphics graphics)
        {
            return graphics == null ? 1F : graphics.DpiX / 96F;
        }

        public static float ScaleFactor(Control control)
        {
            if (control == null)
            {
                return 1F;
            }
            using (Graphics graphics = control.CreateGraphics())
            {
                return ScaleFactor(graphics);
            }
        }

        public static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(
                rectangle.Right - diameter,
                rectangle.Bottom - diameter,
                diameter,
                diameter,
                0,
                90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Button CreateButton(string text, bool primary)
        {
            Button button = new Button();
            button.Text = text;
            button.Height = 34;
            button.Width = 104;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Palette.Border;
            button.BackColor = primary ? Palette.Accent : Palette.Card;
            button.ForeColor = primary ? Color.White : Palette.Ink;
            button.Font = Palette.BodyBold;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(8, 0, 0, 0);
            button.EnabledChanged += delegate
            {
                if (primary)
                {
                    button.BackColor = button.Enabled
                        ? Palette.Accent
                        : Palette.IsDark
                            ? Color.FromArgb(49, 72, 97)
                            : Color.FromArgb(183, 207, 234);
                    button.ForeColor = button.Enabled
                        ? Color.White
                        : Palette.Muted;
                }
                else
                {
                    button.BackColor = button.Enabled
                        ? Palette.Card
                        : Palette.SoftBlue;
                    button.ForeColor = button.Enabled ? Palette.Ink : Palette.Muted;
                }
            };
            button.MouseEnter += delegate
            {
                if (!button.Enabled)
                {
                    return;
                }
                button.BackColor = primary ? Palette.AccentHover : Palette.SoftBlue;
            };
            button.MouseLeave += delegate
            {
                if (!button.Enabled)
                {
                    return;
                }
                button.BackColor = primary ? Palette.Accent : Palette.Card;
            };
            return button;
        }

        public static Label BuildStatCard(
            TableLayoutPanel parent,
            int column,
            string title,
            string value,
            Color accent)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(
                column == 0 ? 0 : 7,
                0,
                column == parent.ColumnCount - 1 ? 0 : 7,
                0);
            card.Padding = new Padding(16, 10, 16, 10);

            Label valueLabel = Label(
                value,
                new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                accent,
                ContentAlignment.MiddleLeft);
            valueLabel.Dock = DockStyle.Fill;
            card.Controls.Add(valueLabel);

            Label titleLabel = Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 22;
            card.Controls.Add(titleLabel);

            parent.Controls.Add(card, column, 0);
            return valueLabel;
        }

        public static Label Label(
            string text,
            Font font,
            Color color,
            ContentAlignment align)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.TextAlign = align;
            label.AutoEllipsis = true;
            return label;
        }

        public static Control BuildPageHeader(
            string title,
            string description,
            string actionText,
            EventHandler action)
        {
            TableLayoutPanel panel = new TableLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0);
            panel.ColumnCount = 2;
            panel.RowCount = 1;
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));

            Panel text = new Panel();
            text.Dock = DockStyle.Fill;
            text.Margin = new Padding(0);

            Label titleLabel = Label(title, Palette.PageTitle, Palette.Ink, ContentAlignment.MiddleLeft);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(0, 0);
            text.Controls.Add(titleLabel);

            Label descriptionLabel =
                Label(description, Palette.Small, Palette.Muted, ContentAlignment.MiddleLeft);
            descriptionLabel.AutoSize = true;
            descriptionLabel.Location = new Point(2, 36);
            text.Controls.Add(descriptionLabel);
            panel.Controls.Add(text, 0, 0);

            if (!String.IsNullOrWhiteSpace(actionText) && action != null)
            {
                Button button = CreateButton(actionText, true);
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(12, 7, 0, 7);
                button.Click += action;
                panel.Controls.Add(button, 1, 0);
            }
            return panel;
        }

        public static void StyleGrid(DataGridView grid)
        {
            Color header = Palette.IsDark
                ? Color.FromArgb(36, 47, 59)
                : Color.FromArgb(248, 251, 254);
            grid.BackgroundColor = Palette.Card;
            grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 38;
            grid.RowTemplate.Height = 42;
            grid.GridColor = Palette.Border;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.BackColor = Palette.Card;
            grid.DefaultCellStyle.ForeColor = Palette.Ink;
            grid.DefaultCellStyle.SelectionBackColor = Palette.SoftBlue;
            grid.DefaultCellStyle.SelectionForeColor = Palette.Ink;
            grid.DefaultCellStyle.Font = Palette.Body;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.ColumnHeadersDefaultCellStyle.BackColor = header;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = header;
            grid.ColumnHeadersDefaultCellStyle.Font = Palette.Small;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(grid.Handle, Palette.IsDark, false);
            };
            if (grid.IsHandleCreated)
            {
                NativeSystem.ApplyControlTheme(grid.Handle, Palette.IsDark, false);
            }
        }

        public static void CenterOnOwner(Form form, Control owner)
        {
            Form ownerForm = owner.FindForm();
            if (ownerForm != null)
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(
                    ownerForm.Left + (ownerForm.Width - form.Width) / 2,
                    ownerForm.Top + (ownerForm.Height - form.Height) / 2);
            }
        }
    }

    internal sealed class DashboardPage : UserControl
    {
        public event Action<ManagerView> NavigateRequested;
        public event Action QuickCheckRequested;

        private readonly HealthRing _ring;
        private readonly Label _checkButtonLabel;
        private readonly Button _checkButton;
        private readonly Button _memoryButton;
        private readonly Label _scoreHint;
        private bool _memoryBusy;
        private readonly Label _ramValue;
        private readonly Label _pageFileValue;
        private readonly Label _gameValue;
        private readonly Label _networkValue;
        private readonly Label _junkValue;
        private readonly Label _networkCardValue;
        private readonly Label _gameCardValue;

        public DashboardPage()
        {
            BackColor = Palette.Canvas;
            AutoScroll = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Top;
            root.AutoSize = true;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.Margin = new Padding(0);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 204F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 166F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 232F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "首页体检",
                    "查看系统状态，处理影响游戏流畅度的常见问题",
                    "刷新状态",
                    delegate { RefreshKnownFromParent(); }),
                0,
                0);

            HeroPanel hero = new HeroPanel();
            hero.Dock = DockStyle.Fill;
            hero.Margin = new Padding(0, 0, 0, 14);

            TableLayoutPanel heroLayout = new TableLayoutPanel();
            heroLayout.Dock = DockStyle.Fill;
            heroLayout.Padding = new Padding(26, 20, 18, 15);
            heroLayout.BackColor = Color.Transparent;
            heroLayout.ColumnCount = 2;
            heroLayout.RowCount = 1;
            heroLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

            Panel heroText = new Panel();
            heroText.Dock = DockStyle.Fill;
            heroText.BackColor = Color.Transparent;
            Label heroTitle = Ui.Label(
                "系统状态概览",
                new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            heroTitle.AutoSize = true;
            heroTitle.Location = new Point(0, 10);
            heroText.Controls.Add(heroTitle);

            Label heroDescription = Ui.Label(
                "检查垃圾、网络、游戏和虚拟内存，或一键释放内存；扫描不会自动修改系统。",
                Palette.Body,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            heroDescription.AutoSize = false;
            heroDescription.Size = new Size(470, 48);
            heroDescription.Location = new Point(2, 50);
            heroText.Controls.Add(heroDescription);

            _checkButton = new Button();
            _checkButton.Text = "开始一键体检";
            _checkButton.Width = 132;
            _checkButton.Height = 38;
            _checkButton.FlatStyle = FlatStyle.Flat;
            _checkButton.FlatAppearance.BorderSize = 0;
            _checkButton.BackColor = Palette.Accent;
            _checkButton.ForeColor = Color.White;
            _checkButton.Font = Palette.BodyBold;
            _checkButton.Cursor = Cursors.Hand;
            _checkButton.Location = new Point(0, 107);
            _checkButton.Click += delegate
            {
                if (QuickCheckRequested != null)
                {
                    QuickCheckRequested();
                }
            };
            heroText.Controls.Add(_checkButton);

            _memoryButton = Ui.CreateButton("释放内存", false);
            _memoryButton.Width = 112;
            _memoryButton.Height = 38;
            _memoryButton.Location = new Point(142, 107);
            _memoryButton.Click += delegate { BoostMemory(); };
            heroText.Controls.Add(_memoryButton);

            _checkButtonLabel = Ui.Label(
                "扫描过程不会自动删除任何文件",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _checkButtonLabel.AutoSize = true;
            _checkButtonLabel.Location = new Point(266, 116);
            heroText.Controls.Add(_checkButtonLabel);

            _ring = new HealthRing();
            _ring.Dock = DockStyle.Fill;

            heroLayout.Controls.Add(heroText, 0, 0);
            heroLayout.Controls.Add(_ring, 1, 0);
            hero.Controls.Add(heroLayout);
            root.Controls.Add(hero, 0, 1);

            TableLayoutPanel quickCards = new TableLayoutPanel();
            quickCards.Dock = DockStyle.Fill;
            quickCards.Margin = new Padding(0, 0, 0, 14);
            quickCards.ColumnCount = 3;
            quickCards.RowCount = 1;
            quickCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++)
            {
                quickCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }

            _junkValue = AddQuickCard(
                quickCards,
                0,
                "垃圾清理",
                "查看可清理的临时文件和缓存",
                "尚未扫描",
                Palette.Accent,
                "前往清理",
                delegate { Navigate(ManagerView.Cleanup); });

            _gameCardValue = AddQuickCard(
                quickCards,
                1,
                "游戏加速",
                "关闭后台占用并优化系统设置",
                "等待检测",
                Palette.Aqua,
                "立即加速",
                delegate { Navigate(ManagerView.Game); });

            _networkCardValue = AddQuickCard(
                quickCards,
                2,
                "网络修复",
                "检测延迟、丢包和域名解析",
                "网络已连接",
                Palette.Warning,
                "检测网络",
                delegate { Navigate(ManagerView.Network); });
            root.Controls.Add(quickCards, 0, 2);

            RoundedPanel overview = new RoundedPanel();
            overview.Dock = DockStyle.Fill;
            overview.Margin = new Padding(0, 0, 0, 16);
            overview.Padding = new Padding(18, 14, 18, 14);

            TableLayoutPanel overviewLayout = new TableLayoutPanel();
            overviewLayout.Dock = DockStyle.Fill;
            overviewLayout.ColumnCount = 2;
            overviewLayout.RowCount = 3;
            overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            Label title = Ui.Label(
                "当前状态",
                Palette.SectionTitle,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            title.Dock = DockStyle.Fill;
            overviewLayout.Controls.Add(title, 0, 0);
            overviewLayout.SetColumnSpan(title, 2);

            _ramValue = AddOverviewRow(overviewLayout, 0, 1, "物理内存", "正在读取");
            _pageFileValue = AddOverviewRow(overviewLayout, 1, 1, "虚拟内存", "正在读取");
            _gameValue = AddOverviewRow(overviewLayout, 0, 2, "游戏进程", "未检测到");
            _networkValue = AddOverviewRow(overviewLayout, 1, 2, "网络连接", "正在检测");

            _scoreHint = Ui.Label(
                "体检会检查垃圾文件、网络、游戏后台和系统设置",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _scoreHint.Dock = DockStyle.Bottom;
            overview.Controls.Add(_scoreHint);
            overview.Controls.Add(overviewLayout);
            root.Controls.Add(overview, 0, 3);
        }

        public void UpdateKnownState(
            MemoryMetrics memory,
            string pageFile,
            GameSnapshot games,
            NetworkReport network)
        {
            if (memory != null)
            {
                _ramValue.Text = String.Format(
                    "{0:0.0} GB 可用 / {1:0.0} GB",
                    memory.AvailablePhysical / 1073741824.0,
                    memory.TotalPhysical / 1073741824.0);
            }
            else
            {
                _ramValue.Text = "读取失败";
            }
            _pageFileValue.Text = pageFile;
            _gameValue.Text =
                games != null && games.RunningGames.Count > 0
                    ? games.Summary
                    : "未检测到游戏";
            _gameValue.ForeColor =
                games != null && games.RunningGames.Count > 0
                    ? Palette.Success
                    : Palette.Ink;
            _networkValue.Text = network == null ? "未检测到" : network.Summary;
            _networkValue.ForeColor =
                network != null && network.Available ? Palette.Success : Palette.Danger;
            if (network != null)
            {
                _networkCardValue.Text = network.Available
                    ? network.AdapterName + " · " + network.LinkSpeed
                    : "未连接";
            }
            if (games != null && games.RunningGames.Count > 0)
            {
                _gameCardValue.Text = games.Summary + " 正在运行";
            }
            else
            {
                _gameCardValue.Text =
                    games != null && games.InstalledSteamGames > 0
                        ? "已识别 " + games.InstalledSteamGames + " 个 Steam 游戏"
                        : "等待游戏启动";
            }
        }

        public void BeginQuickCheck()
        {
            _checkButton.Enabled = false;
            _checkButton.Text = "正在体检…";
            _checkButtonLabel.Text = "扫描中，不会自动删除文件";
            _ring.SetChecking("正在检查文件与网络");
            _scoreHint.Text = "正在扫描…";
        }

        public void SetProgress(string text)
        {
            _checkButtonLabel.Text = text;
        }

        public void CompleteQuickCheck(
            CleanupScanResult cleanup,
            NetworkReport network,
            string error)
        {
            _checkButton.Enabled = true;
            _checkButton.Text = "重新一键体检";
            _checkButtonLabel.Text = "扫描过程不会自动删除任何文件";

            if (!String.IsNullOrWhiteSpace(error))
            {
                _ring.SetResult(0, "体检失败", "请查看管家日志");
                _scoreHint.Text = error;
                _scoreHint.ForeColor = Palette.Danger;
                return;
            }

            long junkBytes = cleanup == null ? 0 : cleanup.TotalBytes;
            int score = 100;
            if (junkBytes > 0)
            {
                score -= Math.Min(20, (int)Math.Ceiling(junkBytes / 1073741824.0 * 4));
            }
            if (SystemOptimizer.PageFileNeedsMove())
            {
                score -= 4;
            }
            int gameMode = SystemOptimizer.GetDword(
                @"Software\Microsoft\GameBar",
                "AutoGameModeEnabled",
                0);
            if (gameMode == 0)
            {
                score -= 3;
            }
            if (network != null)
            {
                if (!network.InternetReachable)
                {
                    score -= 25;
                }
                else
                {
                    if (network.PacketLossPercent >= 10)
                    {
                        score -= 10;
                    }
                    if (network.InternetLatencyMs >= 150)
                    {
                        score -= 6;
                    }
                    if (!network.DnsResolved)
                    {
                        score -= 10;
                    }
                }
            }
            score = Math.Max(0, Math.Min(100, score));

            string status = score >= 90 ? "状态优秀" :
                score >= 75 ? "状态良好" :
                score >= 60 ? "建议优化" : "需要处理";
            _ring.SetResult(
                score,
                status,
                cleanup == null
                    ? "本次未完成垃圾扫描"
                    : "本次体检结果，不是硬件跑分");
            _scoreHint.ForeColor = Palette.Muted;
            _scoreHint.Text = String.Format(
                "扫描于 {0:HH:mm} · 发现 {1} 个可清理文件，共 {2}",
                DateTime.Now,
                cleanup == null ? 0 : cleanup.TotalFiles,
                Format.Bytes(junkBytes));

            _junkValue.Text = junkBytes > 0 ? Format.Bytes(junkBytes) + " 可清理" : "暂无可清理内容";
            if (network != null)
            {
                _networkCardValue.Text = network.Summary;
                _networkValue.Text = network.Summary;
                _networkValue.ForeColor =
                    network.InternetReachable && network.DnsResolved
                        ? Palette.Success
                        : Palette.Danger;
            }
        }

        private Label AddQuickCard(
            TableLayoutPanel parent,
            int column,
            string title,
            string description,
            string metric,
            Color accent,
            string actionText,
            EventHandler action)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(column == 0 ? 0 : 7, 0, column == 2 ? 0 : 7, 0);
            card.Padding = new Padding(16, 12, 14, 10);

            Panel stripe = new Panel();
            stripe.BackColor = accent;
            stripe.Size = new Size(4, 38);
            stripe.Location = new Point(16, 17);
            card.Controls.Add(stripe);

            Label titleLabel = Ui.Label(
                title,
                Palette.SectionTitle,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(29, 13);
            card.Controls.Add(titleLabel);

            Label descriptionLabel = Ui.Label(
                description,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            descriptionLabel.AutoSize = false;
            descriptionLabel.Size = new Size(270, 30);
            descriptionLabel.Location = new Point(29, 42);
            card.Controls.Add(descriptionLabel);

            Label metricLabel = Ui.Label(
                metric,
                Palette.BodyBold,
                accent,
                ContentAlignment.MiddleLeft);
            metricLabel.AutoSize = true;
            metricLabel.Location = new Point(16, 80);
            card.Controls.Add(metricLabel);

            Button button = Ui.CreateButton(actionText, false);
            button.Width = 94;
            button.Height = 30;
            button.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            button.Location = new Point(card.Width - 108, 78);
            button.Click += action;
            card.Controls.Add(button);
            card.Resize += delegate
            {
                button.Left = card.ClientSize.Width - button.Width - card.Padding.Right;
                button.Top = card.ClientSize.Height - button.Height - card.Padding.Bottom;
            };
            parent.Controls.Add(card, column, 0);
            return metricLabel;
        }

        private Label AddOverviewRow(
            TableLayoutPanel parent,
            int column,
            int row,
            string title,
            string value)
        {
            Panel rowPanel = new Panel();
            rowPanel.Dock = DockStyle.Fill;
            rowPanel.Margin = new Padding(column == 0 ? 0 : 8, 2, column == 0 ? 8 : 0, 2);

            Label titleLabel = Ui.Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(0, 2);
            rowPanel.Controls.Add(titleLabel);

            Label valueLabel = Ui.Label(
                value,
                Palette.BodyBold,
                Palette.Ink,
                ContentAlignment.BottomLeft);
            valueLabel.AutoSize = false;
            valueLabel.Dock = DockStyle.Bottom;
            valueLabel.Height = 24;
            rowPanel.Controls.Add(valueLabel);
            parent.Controls.Add(rowPanel, column, row);
            return valueLabel;
        }

        private void Navigate(ManagerView view)
        {
            if (NavigateRequested != null)
            {
                NavigateRequested(view);
            }
        }

        private void BoostMemory()
        {
            if (_memoryBusy)
            {
                return;
            }
            _memoryBusy = true;
            _memoryButton.Enabled = false;
            _checkButton.Enabled = false;
            _checkButtonLabel.Text = "正在整理内存…";
            _checkButtonLabel.ForeColor = Palette.Muted;
            _scoreHint.Text = "正在清空工作集、刷新修改页并整理待机列表…";
            _scoreHint.ForeColor = Palette.Muted;

            ThreadPool.QueueUserWorkItem(delegate
            {
                MemoryCleanResult result = MemoryOptimizer.Clean(
                    delegate(string text)
                    {
                        SafeBeginInvoke(delegate { _scoreHint.Text = text; });
                    });
                SafeBeginInvoke(delegate
                {
                    _memoryBusy = false;
                    _memoryButton.Enabled = true;
                    _checkButton.Enabled = true;
                    if (!String.IsNullOrWhiteSpace(result.Error))
                    {
                        _checkButtonLabel.Text = "内存加速失败：" + result.Error;
                        _checkButtonLabel.ForeColor = Palette.Danger;
                        _scoreHint.Text = "可尝试以管理员身份运行后重试。";
                    }
                    else
                    {
                        _checkButtonLabel.Text = String.Format(
                            "已释放约 {0}，当前可用 {1}",
                            Format.Bytes(result.FreedBytes),
                            Format.Bytes(result.AfterAvailable));
                        _checkButtonLabel.ForeColor = Palette.Success;
                        _scoreHint.Text =
                            "内存加速完成；待机缓存会按需重建，首次打开大型程序可能稍慢。";
                    }
                    _scoreHint.ForeColor = Palette.Muted;
                    RefreshKnownFromParent();
                });
            });
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }

        private void RefreshKnownFromParent()
        {
            MainForm form = FindForm() as MainForm;
            if (form != null)
            {
                form.UpdateKnownSystemState();
            }
        }
    }

    internal sealed class CleanupPage : UserControl
    {
        private readonly DataGridView _grid;
        private readonly Label _totalValue;
        private readonly Label _summary;
        private readonly Button _scanButton;
        private readonly Button _cleanButton;
        private readonly ProgressBar _progress;
        private List<CleanupCategory> _categories;
        private bool _busy;

        public CleanupPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "垃圾清理",
                    "仅处理可重建的临时文件和缓存，不扫描文档、图片或下载目录",
                    null,
                    null),
                0,
                0);

            RoundedPanel summary = new RoundedPanel();
            summary.Dock = DockStyle.Fill;
            summary.Margin = new Padding(0, 0, 0, 14);
            summary.Padding = new Padding(20, 14, 18, 12);

            TableLayoutPanel summaryLayout = new TableLayoutPanel();
            summaryLayout.Dock = DockStyle.Fill;
            summaryLayout.ColumnCount = 2;
            summaryLayout.RowCount = 2;
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430F));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));

            Panel totalPanel = new Panel();
            totalPanel.Dock = DockStyle.Fill;
            Label caption = Ui.Label(
                "本次扫描",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            caption.AutoSize = true;
            caption.Location = new Point(0, 0);
            totalPanel.Controls.Add(caption);

            _totalValue = Ui.Label(
                "尚未扫描",
                new Font("Microsoft YaHei UI", 22F, FontStyle.Bold),
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            _totalValue.AutoSize = true;
            _totalValue.Location = new Point(0, 22);
            totalPanel.Controls.Add(_totalValue);

            _summary = Ui.Label(
                "点击扫描后展示可清理内容，点击清理按钮即执行。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _summary.Dock = DockStyle.Fill;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 20, 0, 0);

            _cleanButton = Ui.CreateButton("立即清理选中", true);
            _cleanButton.Width = 122;
            _cleanButton.Enabled = false;
            _cleanButton.Click += delegate { CleanSelected(); };

            _scanButton = Ui.CreateButton("扫描垃圾", false);
            _scanButton.Width = 102;
            _scanButton.Click += delegate { Scan(); };

            Button selectAll = Ui.CreateButton("全选", false);
            selectAll.Width = 68;
            selectAll.Click += delegate { SelectAll(false); };
            Button recommended = Ui.CreateButton("推荐项", false);
            recommended.Width = 80;
            recommended.Click += delegate { SelectAll(true); };

            buttons.Controls.Add(_cleanButton);
            buttons.Controls.Add(_scanButton);
            buttons.Controls.Add(selectAll);
            buttons.Controls.Add(recommended);

            _progress = new ProgressBar();
            _progress.Dock = DockStyle.Fill;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;

            summaryLayout.Controls.Add(totalPanel, 0, 0);
            summaryLayout.Controls.Add(_summary, 0, 1);
            summaryLayout.Controls.Add(buttons, 1, 0);
            summaryLayout.SetRowSpan(buttons, 2);
            summaryLayout.Controls.Add(_progress, 1, 1);
            summary.Controls.Add(summaryLayout);
            root.Controls.Add(summary, 0, 1);

            RoundedPanel gridCard = new RoundedPanel();
            gridCard.Dock = DockStyle.Fill;
            gridCard.Margin = new Padding(0, 0, 0, 12);
            gridCard.Padding = new Padding(12);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            Ui.StyleGrid(_grid);

            DataGridViewCheckBoxColumn select = new DataGridViewCheckBoxColumn();
            select.Name = "Selected";
            select.HeaderText = "选择";
            select.FillWeight = 10F;
            select.MinimumWidth = 56;

            DataGridViewTextBoxColumn title = new DataGridViewTextBoxColumn();
            title.Name = "Title";
            title.HeaderText = "清理项目";
            title.ReadOnly = true;
            title.FillWeight = 25F;
            title.MinimumWidth = 150;

            DataGridViewTextBoxColumn size = new DataGridViewTextBoxColumn();
            size.Name = "Size";
            size.HeaderText = "大小";
            size.ReadOnly = true;
            size.FillWeight = 15F;
            size.MinimumWidth = 90;

            DataGridViewTextBoxColumn advice = new DataGridViewTextBoxColumn();
            advice.Name = "Advice";
            advice.HeaderText = "建议";
            advice.ReadOnly = true;
            advice.FillWeight = 13F;
            advice.MinimumWidth = 88;

            DataGridViewTextBoxColumn files = new DataGridViewTextBoxColumn();
            files.Name = "Files";
            files.HeaderText = "数量";
            files.ReadOnly = true;
            files.FillWeight = 9F;
            files.MinimumWidth = 64;

            DataGridViewTextBoxColumn note = new DataGridViewTextBoxColumn();
            note.Name = "Note";
            note.HeaderText = "说明";
            note.ReadOnly = true;
            note.FillWeight = 31F;
            note.MinimumWidth = 220;

            _grid.Columns.Add(select);
            _grid.Columns.Add(title);
            _grid.Columns.Add(size);
            _grid.Columns.Add(advice);
            _grid.Columns.Add(files);
            _grid.Columns.Add(note);
            _grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == _grid.Columns["Selected"].Index)
                {
                    UpdateSelectionSummary();
                }
            };
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            gridCard.Controls.Add(_grid);
            root.Controls.Add(gridCard, 0, 2);

            Label safety = Ui.Label(
                "24 小时内新文件自动跳过；不会清理下载目录、桌面、文档、注册表或浏览器密码、书签和历史记录。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            safety.Dock = DockStyle.Fill;
            root.Controls.Add(safety, 0, 3);

            ShowEmptyState();
        }

        public void ApplyScanResult(CleanupScanResult result)
        {
            if (result == null)
            {
                return;
            }
            _categories = new List<CleanupCategory>();
            _grid.Rows.Clear();
            foreach (CleanupCategory category in result.Categories)
            {
                _categories.Add(category);
                int index = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = category;
                row.Cells["Selected"].Value = category.DefaultSelected;
                row.Cells["Title"].Value = category.Title;
                row.Cells["Size"].Value = Format.Bytes(category.SizeBytes);
                row.Cells["Advice"].Value = category.Recommendation;
                row.Cells["Advice"].ToolTipText = category.RecommendationNote;
                row.Cells["Advice"].Style.ForeColor =
                    category.Recommendation == "不建议清理"
                        ? Palette.Danger
                        : category.Recommendation == "谨慎清理"
                            ? Palette.Warning
                            : Palette.Success;
                row.Cells["Files"].Value = category.FileCount;
                row.Cells["Note"].Value = category.Description;
                if (category.BrowserRunning)
                {
                    row.Cells["Note"].Style.ForeColor = Palette.Warning;
                }
            }
            _totalValue.Text = result.Error == null
                ? "可清理约 " + Format.Bytes(result.TotalBytes)
                : "扫描未完成";
            _summary.Text = result.Error == null
                ? String.Format(
                    "共 {0} 个文件，扫描于 {1:HH:mm:ss}。勾选后点击清理即执行。",
                    result.TotalFiles,
                    result.CompletedAt)
                : result.Error;
            _summary.ForeColor = result.Error == null ? Palette.Muted : Palette.Danger;
            _cleanButton.Enabled = result.TotalBytes > 0;
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            if (_categories == null || _busy)
            {
                return;
            }
            List<CleanupCategory> selected = GetSelectedCategories();
            long bytes = 0;
            foreach (CleanupCategory category in selected)
            {
                bytes += category.SizeBytes;
            }
            _summary.Text = String.Format(
                "已选 {0} 项，预计释放 {1}；绿色为建议清理，橙色需谨慎，红色不建议清理。",
                selected.Count,
                Format.Bytes(bytes));
            _summary.ForeColor = selected.Count > 0 ? Palette.Muted : Palette.Warning;
            _cleanButton.Enabled = selected.Count > 0;
        }

        public void Scan()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetBusy(true, "正在扫描临时文件和缓存…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                CleanupScanResult result = CleanupScanner.ScanAll(delegate(string text)
                {
                    SafeBeginInvoke(delegate { _summary.Text = text; });
                });
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false, null);
                    ApplyScanResult(result);
                });
            });
        }

        private void CleanSelected()
        {
            if (_busy)
            {
                return;
            }
            List<CleanupCategory> selected = GetSelectedCategories();
            if (selected.Count == 0)
            {
                _summary.Text = "请先勾选要清理的项目。";
                _summary.ForeColor = Palette.Warning;
                return;
            }

            long total = 0;
            foreach (CleanupCategory category in selected)
            {
                total += category.SizeBytes;
            }

            _busy = true;
            SetBusy(true, "正在清理选中项目，预计释放 " + Format.Bytes(total) + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                CleanupResult result = CleanupScanner.CleanSelected(
                    selected,
                    delegate(string text)
                    {
                        SafeBeginInvoke(delegate { _summary.Text = text; });
                    });
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false, null);
                    string text = result.Error == null
                        ? String.Format(
                            "已清理 {0} 个文件，释放约 {1}；跳过 {2} 个占用项目、{3} 个新文件、{4} 个特殊文件。",
                            result.DeletedFiles,
                            Format.Bytes(result.DeletedBytes),
                            result.FailedItems,
                            result.SkippedTooNew,
                            result.SkippedUnsafe)
                        : "清理失败：" + result.Error;
                    _summary.Text = text;
                    _summary.ForeColor = result.Error == null ? Palette.Success : Palette.Danger;
                    _totalValue.Text = result.Error == null
                        ? "本次释放 " + Format.Bytes(result.DeletedBytes)
                        : "清理未完成";
                    _categories = null;
                    _grid.Enabled = false;
                    _cleanButton.Enabled = false;
                    _scanButton.Enabled = true;
                    _progress.Visible = false;
                });
            });
        }

        private void SelectAll(bool recommended)
        {
            if (_busy || _categories == null)
            {
                return;
            }
            foreach (DataGridViewRow row in _grid.Rows)
            {
                CleanupCategory category = row.Tag as CleanupCategory;
                if (category == null)
                {
                    continue;
                }
                row.Cells["Selected"].Value = recommended
                    ? category.DefaultSelected
                    : true;
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _grid.Enabled = !busy;
            _scanButton.Enabled = !busy;
            _cleanButton.Enabled = !busy && _categories != null;
            _progress.Visible = busy;
            if (!String.IsNullOrWhiteSpace(text))
            {
                _summary.Text = text;
                _summary.ForeColor = Palette.AccentDark;
            }
        }

        private void ShowEmptyState()
        {
            _grid.Rows.Clear();
            _grid.Rows.Add();
            DataGridViewRow row = _grid.Rows[0];
            row.Cells["Selected"].Value = false;
            row.Cells["Selected"].ReadOnly = true;
            row.Cells["Title"].Value = "尚未扫描";
            row.Cells["Size"].Value = "—";
            row.Cells["Files"].Value = "—";
            row.Cells["Note"].Value = "点击“扫描垃圾”查看可清理内容";
        }

        private List<CleanupCategory> GetSelectedCategories()
        {
            List<CleanupCategory> result = new List<CleanupCategory>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                object selected = row.Cells["Selected"].Value;
                if (selected is bool && (bool)selected && row.Tag is CleanupCategory)
                {
                    result.Add((CleanupCategory)row.Tag);
                }
            }
            return result;
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class GamePage : UserControl
    {
        private readonly MainForm _owner;
        private readonly Label _gameValue;
        private readonly Label _backgroundValue;
        private readonly Label _pageFileValue;
        private readonly ComboBox _gameCombo;
        private readonly Button _frameButton;
        private readonly Label _frameResult;
        private readonly ProgressBar _frameProgress;
        private readonly DataGridView _grid;
        private readonly CheckBox _systemOptions;
        private readonly CheckBox _pageFileOption;
        private readonly CheckBox _minimizeOption;
        private readonly Button _optimizeButton;
        private readonly Button _scanButton;
        private readonly Button _cleanCacheButton;
        private readonly Button _restoreButton;
        private readonly Label _summary;
        private List<ProcessGroup> _groups;
        private List<GameProcessInfo> _detectedGames;
        private bool _busy;
        private bool _capturing;

        public GamePage(MainForm owner)
        {
            _owner = owner;
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "游戏加速",
                    "只关闭你勾选的后台软件，按钮点击后立即应用系统优化",
                    null,
                    null),
                0,
                0);

            TableLayoutPanel stats = new TableLayoutPanel();
            stats.Dock = DockStyle.Fill;
            stats.Margin = new Padding(0, 0, 0, 12);
            stats.ColumnCount = 3;
            stats.RowCount = 1;
            stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++)
            {
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            _gameValue = AddMetricCard(stats, 0, "游戏检测", "正在扫描", Palette.Accent);
            _backgroundValue = AddMetricCard(stats, 1, "后台候选", "等待扫描", Palette.Aqua);
            _pageFileValue = AddMetricCard(
                stats,
                2,
                "虚拟内存",
                SystemOptimizer.GetPageFileDisplay(),
                Palette.Warning);
            root.Controls.Add(stats, 0, 1);

            RoundedPanel frameCard = new RoundedPanel();
            frameCard.Dock = DockStyle.Fill;
            frameCard.Margin = new Padding(0, 0, 0, 12);
            frameCard.Padding = new Padding(16, 10, 16, 10);

            Label frameTitle = Ui.Label(
                "通用帧时间诊断",
                Palette.BodyBold,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            frameTitle.AutoSize = true;
            frameTitle.Location = new Point(16, 10);
            frameCard.Controls.Add(frameTitle);

            Label frameHint = Ui.Label(
                "Steam 自动识别，其他游戏全屏后刷新；采样使用 Intel PresentMon",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            frameHint.AutoSize = true;
            frameHint.Location = new Point(142, 11);
            frameCard.Controls.Add(frameHint);

            _gameCombo = new ComboBox();
            _gameCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _gameCombo.FlatStyle = FlatStyle.Flat;
            _gameCombo.DrawMode = DrawMode.OwnerDrawFixed;
            _gameCombo.Font = Palette.Body;
            _gameCombo.BackColor = Palette.Card;
            _gameCombo.ForeColor = Palette.Ink;
            _gameCombo.Location = new Point(16, 43);
            _gameCombo.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _gameCombo.Width = 360;
            _gameCombo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0)
                {
                    return;
                }
                bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using (SolidBrush background = new SolidBrush(
                    isSelected ? Palette.SoftBlue : Palette.Card))
                {
                    e.Graphics.FillRectangle(background, e.Bounds);
                }
                TextRenderer.DrawText(
                    e.Graphics,
                    Convert.ToString(_gameCombo.Items[e.Index]),
                    Palette.Body,
                    e.Bounds,
                    Palette.Ink,
                    TextFormatFlags.Left |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
            };
            _gameCombo.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(_gameCombo.Handle, Palette.IsDark, true);
            };
            if (_gameCombo.IsHandleCreated)
            {
                NativeSystem.ApplyControlTheme(_gameCombo.Handle, Palette.IsDark, true);
            }
            frameCard.Controls.Add(_gameCombo);

            _frameButton = Ui.CreateButton("采样 30 秒", true);
            _frameButton.Width = 108;
            _frameButton.Height = 30;
            _frameButton.Location = new Point(386, 40);
            _frameButton.Click += delegate { StartFrameCapture(); };
            frameCard.Controls.Add(_frameButton);

            _frameProgress = new ProgressBar();
            _frameProgress.Style = ProgressBarStyle.Marquee;
            _frameProgress.Size = new Size(110, 5);
            _frameProgress.Location = new Point(386, 74);
            _frameProgress.Visible = false;
            frameCard.Controls.Add(_frameProgress);

            _frameResult = Ui.Label(
                "启动游戏后刷新列表，再开始采样。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _frameResult.AutoSize = false;
            _frameResult.Location = new Point(508, 36);
            _frameResult.Height = 60;
            _frameResult.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            frameCard.Controls.Add(_frameResult);
            frameCard.Resize += delegate
            {
                LayoutFrameCard(frameCard, _gameCombo, _frameResult);
            };
            frameCard.HandleCreated += delegate
            {
                LayoutFrameCard(frameCard, _gameCombo, _frameResult);
            };
            root.Controls.Add(frameCard, 0, 2);

            RoundedPanel optionsPanel = new RoundedPanel();
            optionsPanel.Dock = DockStyle.Fill;
            optionsPanel.Margin = new Padding(0, 0, 0, 12);
            optionsPanel.Padding = new Padding(16, 10, 16, 8);

            FlowLayoutPanel options = new FlowLayoutPanel();
            options.Dock = DockStyle.Fill;
            options.FlowDirection = FlowDirection.TopDown;
            options.WrapContents = false;

            _systemOptions = CreateOption(
                "开启游戏模式、关闭 Game DVR 后台录制、切换高性能电源",
                true);
            _pageFileOption = CreateOption(
                "将虚拟内存迁移到 D: NVMe（需要重启后生效）",
                SystemOptimizer.CanMovePageFileToD());
            _minimizeOption = CreateOption(
                "优化完成后最小化管家，减少游戏期间的资源占用",
                true);
            options.Controls.Add(_systemOptions);
            options.Controls.Add(_pageFileOption);
            options.Controls.Add(_minimizeOption);
            optionsPanel.Controls.Add(options);
            root.Controls.Add(optionsPanel, 0, 3);

            RoundedPanel gridPanel = new RoundedPanel();
            gridPanel.Dock = DockStyle.Fill;
            gridPanel.Margin = new Padding(0, 0, 0, 12);
            gridPanel.Padding = new Padding(12);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            Ui.StyleGrid(_grid);

            DataGridViewCheckBoxColumn selected = new DataGridViewCheckBoxColumn();
            selected.Name = "Selected";
            selected.HeaderText = "选择";
            selected.FillWeight = 10F;
            selected.MinimumWidth = 56;

            DataGridViewTextBoxColumn name = new DataGridViewTextBoxColumn();
            name.Name = "Name";
            name.HeaderText = "后台程序";
            name.ReadOnly = true;
            name.FillWeight = 23F;
            name.MinimumWidth = 145;

            DataGridViewTextBoxColumn count = new DataGridViewTextBoxColumn();
            count.Name = "Count";
            count.HeaderText = "进程";
            count.ReadOnly = true;
            count.FillWeight = 10F;
            count.MinimumWidth = 62;

            DataGridViewTextBoxColumn memory = new DataGridViewTextBoxColumn();
            memory.Name = "Memory";
            memory.HeaderText = "内存";
            memory.ReadOnly = true;
            memory.FillWeight = 14F;
            memory.MinimumWidth = 82;

            DataGridViewTextBoxColumn advice = new DataGridViewTextBoxColumn();
            advice.Name = "Advice";
            advice.HeaderText = "建议";
            advice.ReadOnly = true;
            advice.FillWeight = 14F;
            advice.MinimumWidth = 84;

            DataGridViewTextBoxColumn note = new DataGridViewTextBoxColumn();
            note.Name = "Note";
            note.HeaderText = "说明";
            note.ReadOnly = true;
            note.FillWeight = 39F;
            note.MinimumWidth = 260;
            _grid.Columns.Add(selected);
            _grid.Columns.Add(name);
            _grid.Columns.Add(count);
            _grid.Columns.Add(memory);
            _grid.Columns.Add(advice);
            _grid.Columns.Add(note);
            gridPanel.Controls.Add(_grid);
            root.Controls.Add(gridPanel, 0, 4);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            root.Controls.Add(actions, 0, 5);

            _summary = Ui.Label(
                "尚未扫描后台程序",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _summary.AutoSize = true;
            _summary.Location = new Point(2, 14);
            actions.Controls.Add(_summary);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 580;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _optimizeButton = Ui.CreateButton("一键游戏加速", true);
            _optimizeButton.Width = 124;
            _optimizeButton.Click += delegate { Optimize(); };

            _scanButton = Ui.CreateButton("扫描后台", false);
            _scanButton.Width = 96;
            _scanButton.Click += delegate { Scan(); };

            _cleanCacheButton = Ui.CreateButton("清理游戏缓存", false);
            _cleanCacheButton.Width = 118;
            _cleanCacheButton.Click += delegate { CleanGameCaches(); };

            _restoreButton = Ui.CreateButton("恢复系统设置", false);
            _restoreButton.Width = 112;
            _restoreButton.Click += delegate { RestoreSettings(); };

            buttons.Controls.Add(_optimizeButton);
            buttons.Controls.Add(_scanButton);
            buttons.Controls.Add(_cleanCacheButton);
            buttons.Controls.Add(_restoreButton);
            actions.Controls.Add(buttons);
            actions.Resize += delegate
            {
                buttons.Left = actions.ClientSize.Width - buttons.Width;
            };

            Scan();
        }

        private static void LayoutFrameCard(
            Control frameCard,
            Control gameCombo,
            Control frameResult)
        {
            float scale = Ui.ScaleFactor(frameCard);
            gameCombo.Width = Math.Max(
                (int)Math.Round(180 * scale),
                Math.Min(
                    (int)Math.Round(360 * scale),
                    frameCard.ClientSize.Width - (int)Math.Round(510 * scale)));
            frameResult.Width = Math.Max(
                (int)Math.Round(180 * scale),
                frameCard.ClientSize.Width - frameResult.Left - (int)Math.Round(16 * scale));
        }

        public void RefreshState()
        {
            RefreshGames();
            _pageFileValue.Text = SystemOptimizer.GetPageFileDisplay();
            _pageFileOption.Enabled = SystemOptimizer.CanMovePageFileToD();
            if (!_pageFileOption.Enabled)
            {
                _pageFileOption.Checked = false;
            }
        }

        private void RefreshGames()
        {
            GameSnapshot snapshot = GameDetector.Snapshot();
            _detectedGames = snapshot.RunningGames;
            _gameCombo.Items.Clear();
            foreach (GameProcessInfo game in _detectedGames)
            {
                _gameCombo.Items.Add(game);
            }
            if (_detectedGames.Count > 0)
            {
                _gameCombo.Enabled = true;
                int selected = 0;
                for (int i = 0; i < _detectedGames.Count; i++)
                {
                    if (_detectedGames[i].IsForeground)
                    {
                        selected = i;
                        break;
                    }
                }
                _gameCombo.SelectedIndex = selected;
                _gameValue.Text = snapshot.Summary;
                _gameValue.ForeColor = Palette.Success;
            }
            else
            {
                _gameCombo.Items.Add("未检测到正在运行的游戏");
                _gameCombo.SelectedIndex = 0;
                _gameCombo.Enabled = false;
                _gameValue.Text = snapshot.InstalledSteamGames > 0
                    ? "已识别 " + snapshot.InstalledSteamGames + " 个游戏"
                    : "未检测到";
                _gameValue.ForeColor = Palette.Accent;
            }

            string presentMon = FrameCaptureService.FindPresentMon();
            if (String.IsNullOrWhiteSpace(presentMon))
            {
                _frameResult.Text = "未找到 PresentMon，帧时间诊断暂不可用。";
                _frameResult.ForeColor = Palette.Warning;
            }
            else if (_detectedGames.Count == 0)
            {
                _frameResult.Text = "启动游戏后点击“扫描后台”刷新列表。";
                _frameResult.ForeColor = Palette.Muted;
            }
            else
            {
                _frameResult.Text = "选择游戏后采样；采样时会暂时最小化管家。";
                _frameResult.ForeColor = Palette.Muted;
            }
            _frameButton.Enabled =
                !String.IsNullOrWhiteSpace(presentMon) &&
                _detectedGames.Count > 0 &&
                !_busy &&
                !_capturing;
        }

        private void StartFrameCapture()
        {
            if (_busy || _capturing)
            {
                return;
            }
            GameProcessInfo game = _gameCombo.SelectedItem as GameProcessInfo;
            if (game == null)
            {
                _frameResult.Text = "没有可采样的游戏进程，请先启动游戏并刷新。";
                _frameResult.ForeColor = Palette.Warning;
                return;
            }
            if (String.IsNullOrWhiteSpace(FrameCaptureService.FindPresentMon()))
            {
                _frameResult.Text = "未找到 PresentMon，无法开始采样。";
                _frameResult.ForeColor = Palette.Warning;
                return;
            }

            _capturing = true;
            SetBusy(true);
            _frameButton.Enabled = false;
            _frameProgress.Visible = true;
            _frameResult.ForeColor = Palette.AccentDark;
            _frameResult.Text = "2 秒后开始采样，请保持游戏运行…";
            NativeSystem.ActivateWindow(game.WindowHandle);
            _owner.WindowState = FormWindowState.Minimized;

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(2000);
                FrameCaptureResult result = FrameCaptureService.Capture(
                    game.ProcessId,
                    30,
                    delegate(string text)
                    {
                        SafeBeginInvoke(delegate
                        {
                            _frameResult.Text = text;
                        });
                    });
                SafeBeginInvoke(delegate
                {
                    _owner.WindowState = FormWindowState.Normal;
                    _owner.Activate();
                    _capturing = false;
                    _frameProgress.Visible = false;
                    SetBusy(false);
                    RefreshGames();
                    if (!result.Success)
                    {
                        _frameResult.Text = result.Error;
                        _frameResult.ForeColor = Palette.Danger;
                        return;
                    }

                    string assessment =
                        result.OnePercentLowFps < result.AverageFps * 0.55 ||
                        result.StutterCount >= 10
                            ? "帧时间波动明显，建议先关闭后台占用并检查驱动。"
                            : "帧时间较稳定。";
                    _frameResult.Text = String.Format(
                        "平均 {0:0} FPS · 1% Low {1:0} FPS · 卡顿 {2} 次 · 平均帧时 {3:0.0}ms\r\n{4}",
                        result.AverageFps,
                        result.OnePercentLowFps,
                        result.StutterCount,
                        result.AverageFrameTimeMs,
                        assessment);
                    _frameResult.ForeColor =
                        result.OnePercentLowFps < result.AverageFps * 0.55
                            ? Palette.Warning
                            : Palette.Success;
                    FileLogger.Write(String.Format(
                        "帧时间诊断 {0}: avg={1:0.0}, low1={2:0.0}, stutters={3}, csv={4}",
                        game.DisplayName,
                        result.AverageFps,
                        result.OnePercentLowFps,
                        result.StutterCount,
                        result.CsvPath));
                });
            });
        }

        private void Scan()
        {
            if (_busy)
            {
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                _groups = ProcessScanner.ScanGroups();
                _grid.Rows.Clear();
                long total = 0;
                foreach (ProcessGroup group in _groups)
                {
                    int index = _grid.Rows.Add();
                    DataGridViewRow row = _grid.Rows[index];
                    row.Tag = group;
                    row.Cells["Selected"].Value = group.DefaultSelected;
                    row.Cells["Name"].Value = group.DisplayName;
                    row.Cells["Count"].Value = group.ProcessIds.Count;
                    row.Cells["Memory"].Value = Format.Bytes(group.WorkingSetBytes);
                    row.Cells["Advice"].Value = group.Recommendation;
                    row.Cells["Note"].Value = group.Note;
                    row.Cells["Advice"].Style.ForeColor =
                        group.Risk == RiskLevel.Low ? Palette.Success :
                        group.Risk == RiskLevel.Medium ? Palette.Warning : Palette.Danger;
                    total += group.WorkingSetBytes;
                }
                if (_groups.Count == 0)
                {
                    int index = _grid.Rows.Add();
                    _grid.Rows[index].Cells["Name"].Value = "没有发现候选后台";
                    _grid.Rows[index].Cells["Count"].Value = "—";
                    _grid.Rows[index].Cells["Memory"].Value = "—";
                    _grid.Rows[index].Cells["Advice"].Value = "—";
                    _grid.Rows[index].Cells["Note"].Value = "可以直接进行系统设置优化";
                    _summary.Text = "没有可关闭的候选后台程序";
                    _backgroundValue.Text = "0 个候选";
                }
                else
                {
                    _summary.Text = String.Format(
                        "{0} 组候选，内存占用合计 {1}",
                        _groups.Count,
                        Format.Bytes(total));
                    _backgroundValue.Text = _groups.Count + " 组 · " + Format.Bytes(total);
                }
                RefreshState();
            }
            catch (Exception ex)
            {
                FileLogger.Write("扫描后台程序失败: " + ex);
                MessageBox.Show(
                    this,
                    "扫描后台程序失败：\n" + ex.Message,
                    "扫描失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void Optimize()
        {
            if (_busy)
            {
                return;
            }
            List<ProcessGroup> selected = new List<ProcessGroup>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                object value = row.Cells["Selected"].Value;
                if (value is bool && (bool)value && row.Tag is ProcessGroup)
                {
                    selected.Add((ProcessGroup)row.Tag);
                }
            }
            bool system = _systemOptions.Checked;
            bool pageFile = _pageFileOption.Checked && _pageFileOption.Enabled;
            if (selected.Count == 0 && !system && !pageFile)
            {
                _summary.Text = "请先勾选后台程序或系统优化项。";
                _summary.ForeColor = Palette.Warning;
                return;
            }

            _busy = true;
            SetBusy(true);
            _summary.Text = String.Format(
                "正在执行：关闭 {0} 组后台程序{1}{2}…",
                selected.Count,
                system ? "，优化游戏设置" : "",
                pageFile ? "，迁移虚拟内存" : "");
            _summary.ForeColor = Palette.AccentDark;
            ThreadPool.QueueUserWorkItem(delegate
            {
                StringBuilder message = new StringBuilder();
                int errors = 0;
                bool reboot = false;
                try
                {
                    BackupStore backup = BackupStore.Load();
                    TerminationResult termination = ProcessScanner.TerminateGroups(selected);
                    errors += termination.Errors.Count;
                    message.AppendFormat(
                        "已结束 {0} 个进程，工作集约 {1}。\r\n",
                        termination.ClosedProcesses,
                        Format.Bytes(termination.WorkingSetBytes));

                    if (system)
                    {
                        try
                        {
                            SystemOptimizer.ApplyGameSettings(backup);
                            SystemOptimizer.ApplyHighPerformancePower(backup);
                            message.AppendLine("游戏模式、Game DVR 与电源计划已优化。");
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            message.AppendLine("系统优化失败：" + ex.Message);
                        }
                    }
                    if (pageFile)
                    {
                        try
                        {
                            SystemOptimizer.ApplyPageFileToD(backup);
                            message.AppendLine("虚拟内存将在重启后迁移到 D:。");
                            reboot = true;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            message.AppendLine("虚拟内存迁移失败：" + ex.Message);
                        }
                    }
                    backup.Save();
                    FileLogger.Write(message.ToString());
                }
                catch (Exception ex)
                {
                    errors++;
                    message.AppendLine("优化失败：" + ex.Message);
                    FileLogger.Write("游戏加速失败: " + ex);
                }

                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false);
                    if (reboot)
                    {
                        message.AppendLine();
                        message.AppendLine("虚拟内存迁移需要重启电脑后生效。");
                    }
                    Scan();
                    _summary.Text = message.ToString()
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();
                    _summary.ForeColor = errors == 0 ? Palette.Success : Palette.Warning;
                    _owner.UpdateKnownSystemState();
                    _owner.MinimizeAfterOptimization(_minimizeOption.Checked && errors == 0);
                });
            });
        }

        private void CleanGameCaches()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetBusy(true);
            _summary.Text = "正在清理 DirectX 与显卡着色器缓存…";
            _summary.ForeColor = Palette.AccentDark;
            ThreadPool.QueueUserWorkItem(delegate
            {
                CleanupResult result = CleanupScanner.CleanCategories(
                    new string[] { "directx-shader-cache", "gpu-shader-caches" },
                    delegate(string text)
                    {
                        SafeBeginInvoke(delegate { _summary.Text = text; });
                    });
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false);
                    _summary.Text = result.Error == null
                        ? String.Format(
                            "游戏缓存清理完成：{0} 个文件，释放约 {1}。",
                            result.DeletedFiles,
                            Format.Bytes(result.DeletedBytes))
                        : "清理游戏缓存失败：" + result.Error;
                    _summary.ForeColor = result.Error == null
                        ? Palette.Success
                        : Palette.Danger;
                    _owner.UpdateKnownSystemState();
                });
            });
        }

        private void RestoreSettings()
        {
            BackupStore backup = BackupStore.Load();
            if (!backup.HasSettings)
            {
                _summary.Text = "还没有可恢复的系统设置备份。";
                _summary.ForeColor = Palette.Warning;
                return;
            }
            _summary.Text = "正在恢复本工具修改过的系统设置…";
            _summary.ForeColor = Palette.AccentDark;
            try
            {
                bool reboot;
                List<string> lines = SystemOptimizer.RestoreAll(backup, out reboot);
                StringBuilder message = new StringBuilder();
                foreach (string line in lines)
                {
                    message.AppendLine("• " + line);
                }
                if (reboot)
                {
                    message.AppendLine();
                    message.AppendLine("虚拟内存恢复需要重启后生效。");
                }
                _summary.Text = message.ToString()
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();
                _summary.ForeColor = Palette.Success;
                RefreshState();
                _owner.UpdateKnownSystemState();
            }
            catch (Exception ex)
            {
                _summary.Text = "恢复设置失败：" + ex.Message;
                _summary.ForeColor = Palette.Danger;
            }
        }

        private void SetBusy(bool busy)
        {
            _grid.Enabled = !busy;
            _optimizeButton.Enabled = !busy;
            _scanButton.Enabled = !busy;
            _cleanCacheButton.Enabled = !busy;
            _restoreButton.Enabled = !busy;
            _systemOptions.Enabled = !busy;
            _pageFileOption.Enabled = !busy && SystemOptimizer.CanMovePageFileToD();
            _minimizeOption.Enabled = !busy;
            _frameButton.Enabled =
                !busy &&
                !_capturing &&
                _detectedGames != null &&
                _detectedGames.Count > 0 &&
                !String.IsNullOrWhiteSpace(FrameCaptureService.FindPresentMon());
        }

        private Label AddMetricCard(
            TableLayoutPanel parent,
            int column,
            string title,
            string value,
            Color accent)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(column == 0 ? 0 : 7, 0, column == 2 ? 0 : 7, 0);
            card.Padding = new Padding(16, 10, 16, 10);

            Label valueLabel = Ui.Label(
                value,
                new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                accent,
                ContentAlignment.MiddleLeft);
            valueLabel.Dock = DockStyle.Fill;
            card.Controls.Add(valueLabel);

            Label titleLabel = Ui.Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 22;
            card.Controls.Add(titleLabel);
            parent.Controls.Add(card, column, 0);
            return valueLabel;
        }

        private static CheckBox CreateOption(string text, bool isChecked)
        {
            CheckBox check = new CheckBox();
            check.AutoSize = true;
            check.Text = text;
            check.Checked = isChecked;
            check.Font = Palette.Small;
            check.ForeColor = Palette.Ink;
            check.Margin = new Padding(0, 2, 0, 3);
            check.Cursor = Cursors.Hand;
            return check;
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class NetworkPage : UserControl
    {
        private readonly Label _adapterValue;
        private readonly Label _ipValue;
        private readonly Label _gatewayValue;
        private readonly Label _dnsValue;
        private readonly Label _latencyValue;
        private readonly Label _lossValue;
        private readonly Label _jitterValue;
        private readonly Label _gatewayLatencyValue;
        private readonly Label _linkValue;
        private readonly Label _summary;
        private readonly Button _testButton;
        private readonly Button _quickRepairButton;
        private readonly Button _advancedRepairButton;
        private readonly ProgressBar _progress;
        private readonly RichTextBox _repairLog;
        private NetworkReport _report;
        private bool _busy;

        public NetworkPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "网络修复",
                    "检测网关、互联网延迟、丢包和域名解析；修复按钮点击后立即执行",
                    null,
                    null),
                0,
                0);

            RoundedPanel adapterCard = new RoundedPanel();
            adapterCard.Dock = DockStyle.Fill;
            adapterCard.Margin = new Padding(0, 0, 0, 12);
            adapterCard.Padding = new Padding(18, 12, 18, 10);

            TableLayoutPanel adapterLayout = new TableLayoutPanel();
            adapterLayout.Dock = DockStyle.Fill;
            adapterLayout.ColumnCount = 2;
            adapterLayout.RowCount = 2;
            adapterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            adapterLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            adapterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            adapterLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));

            _adapterValue = Ui.Label(
                "正在读取网络…",
                new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            _adapterValue.Dock = DockStyle.Fill;

            _summary = Ui.Label(
                "尚未检测",
                Palette.BodyBold,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _summary.Dock = DockStyle.Fill;

            _ipValue = Ui.Label(
                "本机地址：—",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _ipValue.Dock = DockStyle.Fill;

            _gatewayValue = Ui.Label(
                "网关：—",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _gatewayValue.Dock = DockStyle.Fill;

            adapterLayout.Controls.Add(_adapterValue, 0, 0);
            adapterLayout.Controls.Add(_summary, 1, 0);
            adapterLayout.Controls.Add(_ipValue, 0, 1);
            adapterLayout.Controls.Add(_gatewayValue, 1, 1);
            adapterCard.Controls.Add(adapterLayout);
            root.Controls.Add(adapterCard, 0, 1);

            RoundedPanel metrics = new RoundedPanel();
            metrics.Dock = DockStyle.Fill;
            metrics.Margin = new Padding(0, 0, 0, 12);
            metrics.Padding = new Padding(16, 10, 16, 10);
            TableLayoutPanel metricsLayout = new TableLayoutPanel();
            metricsLayout.Dock = DockStyle.Fill;
            metricsLayout.ColumnCount = 6;
            metricsLayout.RowCount = 1;
            metricsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++)
            {
                metricsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666F));
            }
            _latencyValue = AddMetric(metricsLayout, 0, 0, "互联网延迟", "—");
            _lossValue = AddMetric(metricsLayout, 1, 0, "丢包率", "—");
            _jitterValue = AddMetric(metricsLayout, 2, 0, "抖动", "—");
            _dnsValue = AddMetric(metricsLayout, 3, 0, "域名解析", "—");
            _gatewayLatencyValue = AddMetric(metricsLayout, 4, 0, "网关延迟", "—");
            _linkValue = AddMetric(metricsLayout, 5, 0, "链路速度", "—");
            metrics.Controls.Add(metricsLayout);
            root.Controls.Add(metrics, 0, 2);

            RoundedPanel logCard = new RoundedPanel();
            logCard.Dock = DockStyle.Fill;
            logCard.Margin = new Padding(0, 0, 0, 12);
            logCard.Padding = new Padding(14, 10, 14, 10);

            _repairLog = new RichTextBox();
            _repairLog.Dock = DockStyle.Fill;
            _repairLog.ReadOnly = true;
            _repairLog.BorderStyle = BorderStyle.None;
            _repairLog.BackColor = Palette.Card;
            _repairLog.ForeColor = Palette.Ink;
            _repairLog.Font = Palette.Body;
            _repairLog.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(_repairLog.Handle, Palette.IsDark, false);
            };
            _repairLog.Text =
                "网络修复记录\r\n\r\n" +
                "快速修复：清空 DNS 缓存、刷新 ARP 并重新注册 DNS。\r\n" +
                "高级修复：重置 Winsock 和 TCP/IP，需要重启电脑。\r\n" +
                "按钮点击即执行，修复记录会保存在本机。";
            logCard.Controls.Add(_repairLog);
            root.Controls.Add(logCard, 0, 3);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            root.Controls.Add(actions, 0, 4);

            _progress = new ProgressBar();
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Width = 150;
            _progress.Height = 6;
            _progress.Location = new Point(0, 20);
            _progress.Visible = false;
            actions.Controls.Add(_progress);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 430;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _advancedRepairButton = Ui.CreateButton("高级网络重置", false);
            _advancedRepairButton.Width = 112;
            _advancedRepairButton.Click += delegate { AdvancedRepair(); };

            _quickRepairButton = Ui.CreateButton("快速修复", false);
            _quickRepairButton.Width = 96;
            _quickRepairButton.Click += delegate { QuickRepair(); };

            _testButton = Ui.CreateButton("开始网络检测", true);
            _testButton.Width = 126;
            _testButton.Click += delegate { Test(); };

            buttons.Controls.Add(_advancedRepairButton);
            buttons.Controls.Add(_quickRepairButton);
            buttons.Controls.Add(_testButton);
            actions.Controls.Add(buttons);
            actions.Resize += delegate
            {
                buttons.Left = actions.ClientSize.Width - buttons.Width;
            };

            Load += delegate { RefreshAdapter(); };
        }

        public void RefreshAdapter()
        {
            _report = NetworkDiagnostics.ReadAdapter();
            ApplyReport(_report);
        }

        public void ApplyReport(NetworkReport report)
        {
            if (report == null)
            {
                return;
            }
            _report = report;
            _adapterValue.Text = report.Available
                ? report.AdapterName + " · " + report.LinkSpeed
                : "未检测到可用网络";
            _adapterValue.ForeColor = report.Available ? Palette.Ink : Palette.Danger;
            _ipValue.Text = "本机地址：" + report.LocalAddress;
            _gatewayValue.Text = "网关：" + report.Gateway;
            _summary.Text = report.Summary;
            _linkValue.Text = report.LinkSpeed;
            if (!report.Tested)
            {
                _summary.ForeColor = report.Available ? Palette.AccentDark : Palette.Danger;
                _latencyValue.Text = "待检测";
                _lossValue.Text = "待检测";
                _jitterValue.Text = "待检测";
                _dnsValue.Text = "待检测";
                _gatewayLatencyValue.Text = "待检测";
                _dnsValue.ForeColor = Palette.Muted;
                return;
            }
            _summary.ForeColor =
                report.Summary.IndexOf("正常", StringComparison.Ordinal) >= 0
                    ? Palette.Success
                    : report.Available ? Palette.Warning : Palette.Danger;
            _latencyValue.Text = Format.Milliseconds(report.InternetLatencyMs);
            _lossValue.Text = report.PacketLossPercent.ToString("0.#") + " %";
            _dnsValue.Text = report.DnsResolved ? "正常" : "异常";
            _dnsValue.ForeColor = report.DnsResolved ? Palette.Success : Palette.Danger;
            _jitterValue.Text = Format.Milliseconds(report.JitterMs);
            _gatewayLatencyValue.Text = Format.Milliseconds(report.GatewayLatencyMs);
        }

        private void Test()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetBusy(true, "正在检测网络…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                NetworkReport report = NetworkDiagnostics.Test(delegate(string text)
                {
                    SafeBeginInvoke(delegate
                    {
                        _summary.Text = text;
                    });
                });
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false, null);
                    ApplyReport(report);
                    _repairLog.AppendText(
                        String.Format(
                            "\r\n\r\n[{0:HH:mm:ss}] 检测完成：{1}，延迟 {2}，丢包 {3:0.#}%。",
                            DateTime.Now,
                            report.Summary,
                            Format.Milliseconds(report.InternetLatencyMs),
                            report.PacketLossPercent));
                });
            });
        }

        private void QuickRepair()
        {
            if (_busy)
            {
                return;
            }
            RunRepair(false);
        }

        private void AdvancedRepair()
        {
            if (_busy)
            {
                return;
            }
            RunRepair(true);
        }

        private void RunRepair(bool advanced)
        {
            _busy = true;
            SetBusy(true, advanced ? "正在重置网络协议栈…" : "正在刷新网络缓存…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                RepairResult result = advanced
                    ? NetworkDiagnostics.AdvancedRepair()
                    : NetworkDiagnostics.QuickRepair();
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false, null);
                    _summary.Text = advanced ? "高级修复完成，需重启" : "快速修复完成";
                    _summary.ForeColor = Palette.Success;
                    StringBuilder text = new StringBuilder();
                    text.AppendFormat(
                        "\r\n\r\n[{0:HH:mm:ss}] {1}\r\n",
                        DateTime.Now,
                        advanced ? "高级网络修复" : "快速网络修复");
                    foreach (string line in result.Lines)
                    {
                        text.AppendLine(line);
                    }
                    if (result.RestartRequired)
                    {
                        text.AppendLine("请重启电脑后再次检测网络。");
                    }
                    _repairLog.AppendText(text.ToString());
                    _summary.Text = advanced
                        ? "网络协议栈已重置，请重启后重新检测。"
                        : "DNS 与局域网缓存已刷新，建议重新检测。";
                    _summary.ForeColor = Palette.Success;
                });
            });
        }

        private void SetBusy(bool busy, string text)
        {
            _testButton.Enabled = !busy;
            _quickRepairButton.Enabled = !busy;
            _advancedRepairButton.Enabled = !busy;
            _progress.Visible = busy;
            if (!String.IsNullOrWhiteSpace(text))
            {
                _summary.Text = text;
                _summary.ForeColor = Palette.AccentDark;
            }
        }

        private Label AddMetric(
            TableLayoutPanel parent,
            int column,
            int row,
            string title,
            string value)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(4);

            Label caption = Ui.Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            caption.AutoSize = true;
            caption.Location = new Point(0, 0);
            panel.Controls.Add(caption);

            Label valueLabel = Ui.Label(
                value,
                Palette.BodyBold,
                Palette.Ink,
                ContentAlignment.BottomLeft);
            valueLabel.Dock = DockStyle.Bottom;
            valueLabel.Height = 24;
            panel.Controls.Add(valueLabel);
            parent.Controls.Add(panel, column, row);
            return valueLabel;
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class SettingsPage : UserControl
    {
        private readonly MainForm _owner;
        private readonly ManagerSettings _settings;
        private readonly Label _currentVersion;
        private readonly Label _latestVersion;
        private readonly Label _updateStatus;
        private readonly RichTextBox _releaseNotes;
        private readonly Button _checkButton;
        private readonly Button _installButton;
        private readonly ProgressBar _progress;
        private readonly CheckBox _checkOnStart;
        private readonly CheckBox _startWithWindows;
        private readonly Button _systemThemeButton;
        private readonly Button _lightThemeButton;
        private readonly Button _darkThemeButton;
        private readonly Label _preferenceStatus;
        private readonly CheckBox _scheduledCleanup;
        private readonly ToolTip _tips = new ToolTip();
        private bool _busy;
        private bool _loading;
        private UpdateCheckResult _lastCheck;

        public SettingsPage(MainForm owner, ManagerSettings settings)
        {
            _owner = owner;
            _settings = settings;
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "设置",
                    "软件更新、启动行为和界面主题",
                    null,
                    null),
                0,
                0);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = new Padding(0, 0, 0, 12);
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.Controls.Add(body, 0, 1);

            RoundedPanel updateCard = new RoundedPanel();
            updateCard.Dock = DockStyle.Fill;
            updateCard.Margin = new Padding(0, 0, 6, 0);
            updateCard.Padding = new Padding(18, 14, 18, 12);

            TableLayoutPanel updateLayout = new TableLayoutPanel();
            updateLayout.Dock = DockStyle.Fill;
            updateLayout.ColumnCount = 2;
            updateLayout.RowCount = 4;
            updateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            updateLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224F));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            updateLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label updateTitle = Ui.Label(
                "软件更新",
                Palette.SectionTitle,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            updateTitle.Dock = DockStyle.Fill;
            updateLayout.Controls.Add(updateTitle, 0, 0);

            FlowLayoutPanel updateButtons = new FlowLayoutPanel();
            updateButtons.Dock = DockStyle.Fill;
            updateButtons.FlowDirection = FlowDirection.RightToLeft;
            updateButtons.WrapContents = false;
            _installButton = Ui.CreateButton("下载并安装", true);
            _installButton.Width = 112;
            _installButton.Enabled = false;
            _installButton.Click += delegate { InstallUpdate(); };
            _checkButton = Ui.CreateButton("检查更新", false);
            _checkButton.Width = 96;
            _checkButton.Click += delegate { CheckUpdates(true); };
            updateButtons.Controls.Add(_installButton);
            updateButtons.Controls.Add(_checkButton);
            updateLayout.Controls.Add(updateButtons, 1, 0);

            FlowLayoutPanel versionRow = new FlowLayoutPanel();
            versionRow.Dock = DockStyle.Fill;
            versionRow.WrapContents = false;
            versionRow.Margin = new Padding(0);
            _currentVersion = Ui.Label(
                "当前版本 " + UpdateService.CurrentVersion(),
                Palette.BodyBold,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            _currentVersion.AutoSize = true;
            _currentVersion.Margin = new Padding(0, 8, 18, 0);
            _latestVersion = Ui.Label(
                "更新源：jokerx.top",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _latestVersion.AutoSize = true;
            _latestVersion.Margin = new Padding(0, 10, 0, 0);
            versionRow.Controls.Add(_currentVersion);
            versionRow.Controls.Add(_latestVersion);
            updateLayout.Controls.Add(versionRow, 0, 1);
            updateLayout.SetColumnSpan(versionRow, 2);

            _updateStatus = Ui.Label(
                "点击“检查更新”连接中转站。更新文件会经过 SHA-256 校验。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            _updateStatus.Dock = DockStyle.Fill;
            _updateStatus.Margin = new Padding(0, 4, 0, 4);
            updateLayout.Controls.Add(_updateStatus, 0, 2);
            updateLayout.SetColumnSpan(_updateStatus, 2);

            Panel notesPanel = new Panel();
            notesPanel.Dock = DockStyle.Fill;
            notesPanel.Margin = new Padding(0);
            _releaseNotes = new RichTextBox();
            _releaseNotes.Dock = DockStyle.Fill;
            _releaseNotes.ReadOnly = true;
            _releaseNotes.BorderStyle = BorderStyle.None;
            _releaseNotes.ScrollBars = RichTextBoxScrollBars.Vertical;
            _releaseNotes.BackColor = Palette.Card;
            _releaseNotes.ForeColor = Palette.Muted;
            _releaseNotes.Font = Palette.Small;
            _releaseNotes.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(
                    _releaseNotes.Handle,
                    Palette.IsDark,
                    false);
            };
            _releaseNotes.Text = "尚未检查更新。";
            _progress = new ProgressBar();
            _progress.Dock = DockStyle.Bottom;
            _progress.Height = 4;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;
            notesPanel.Controls.Add(_releaseNotes);
            notesPanel.Controls.Add(_progress);
            updateLayout.Controls.Add(notesPanel, 0, 3);
            updateLayout.SetColumnSpan(notesPanel, 2);

            updateCard.Controls.Add(updateLayout);
            body.Controls.Add(updateCard, 0, 0);

            RoundedPanel preferences = new RoundedPanel();
            preferences.Dock = DockStyle.Fill;
            preferences.Margin = new Padding(6, 0, 0, 0);
            preferences.Padding = new Padding(18, 12, 18, 12);

            TableLayoutPanel preferenceLayout = new TableLayoutPanel();
            preferenceLayout.Dock = DockStyle.Fill;
            preferenceLayout.ColumnCount = 2;
            preferenceLayout.RowCount = 7;
            preferenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            preferenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 350F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            preferenceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label preferenceTitle = Ui.Label(
                "使用偏好",
                Palette.SectionTitle,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            preferenceTitle.Dock = DockStyle.Fill;
            preferenceLayout.Controls.Add(preferenceTitle, 0, 0);
            preferenceLayout.SetColumnSpan(preferenceTitle, 2);

            _checkOnStart = CreateSettingCheck(
                "启动时自动检查更新（只访问 jokerx.top）",
                _settings.CheckUpdatesOnStart);
            _checkOnStart.CheckedChanged += delegate
            {
                if (_loading)
                {
                    return;
                }
                _settings.CheckUpdatesOnStart = _checkOnStart.Checked;
                _settings.Save();
                _preferenceStatus.Visible = true;
                _preferenceStatus.Text = _checkOnStart.Checked
                    ? "已开启启动时检查更新"
                    : "已关闭启动时检查更新";
                _preferenceStatus.ForeColor = Palette.Success;
            };
            preferenceLayout.Controls.Add(_checkOnStart, 0, 1);
            preferenceLayout.SetColumnSpan(_checkOnStart, 2);

            _startWithWindows = CreateSettingCheck(
                "随 Windows 登录启动（启动时最小化到任务栏）",
                false);
            _startWithWindows.CheckedChanged += delegate
            {
                if (_loading)
                {
                    return;
                }
                try
                {
                    AppStartupService.SetEnabled(_startWithWindows.Checked);
                    _preferenceStatus.Visible = true;
                    _preferenceStatus.Text = _startWithWindows.Checked
                        ? "已设置开机自启动"
                        : "已关闭开机自启动";
                    _preferenceStatus.ForeColor = Palette.Success;
                }
                catch (Exception ex)
                {
                    _loading = true;
                    _startWithWindows.Checked = AppStartupService.IsEnabled();
                    _loading = false;
                    _preferenceStatus.Visible = true;
                    _preferenceStatus.Text = "设置失败：" + ex.Message;
                    _preferenceStatus.ForeColor = Palette.Danger;
                }
            };
            preferenceLayout.Controls.Add(_startWithWindows, 0, 2);
            preferenceLayout.SetColumnSpan(_startWithWindows, 2);

            TableLayoutPanel themeRow = new TableLayoutPanel();
            themeRow.Dock = DockStyle.Fill;
            themeRow.ColumnCount = 2;
            themeRow.RowCount = 1;
            themeRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            themeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            themeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));

            Label themeLabel = Ui.Label(
                "界面主题（点击立即切换）",
                Palette.Body,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            themeLabel.Dock = DockStyle.Fill;

            FlowLayoutPanel themeButtons = new FlowLayoutPanel();
            themeButtons.Dock = DockStyle.Fill;
            themeButtons.FlowDirection = FlowDirection.LeftToRight;
            themeButtons.WrapContents = false;
            themeButtons.Margin = new Padding(0);

            _systemThemeButton = Ui.CreateButton("跟随系统", false);
            _systemThemeButton.Width = 86;
            _systemThemeButton.Height = 29;
            _systemThemeButton.Margin = new Padding(0, 4, 6, 4);
            _systemThemeButton.Tag = "system";
            _systemThemeButton.Click += delegate(object sender, EventArgs e)
            {
                RequestTheme((Button)sender);
            };
            _lightThemeButton = Ui.CreateButton("浅色", false);
            _lightThemeButton.Width = 68;
            _lightThemeButton.Height = 29;
            _lightThemeButton.Margin = new Padding(0, 4, 6, 4);
            _lightThemeButton.Tag = "light";
            _lightThemeButton.Click += delegate(object sender, EventArgs e)
            {
                RequestTheme((Button)sender);
            };
            _darkThemeButton = Ui.CreateButton("深色", false);
            _darkThemeButton.Width = 68;
            _darkThemeButton.Height = 29;
            _darkThemeButton.Margin = new Padding(0, 4, 0, 4);
            _darkThemeButton.Tag = "dark";
            _darkThemeButton.Click += delegate(object sender, EventArgs e)
            {
                RequestTheme((Button)sender);
            };

            themeButtons.Controls.Add(_systemThemeButton);
            themeButtons.Controls.Add(_lightThemeButton);
            themeButtons.Controls.Add(_darkThemeButton);
            themeRow.Controls.Add(themeLabel, 0, 0);
            themeRow.Controls.Add(themeButtons, 1, 0);
            preferenceLayout.Controls.Add(themeRow, 0, 3);
            preferenceLayout.SetColumnSpan(themeRow, 2);

            TableLayoutPanel accentRow = new TableLayoutPanel();
            accentRow.Dock = DockStyle.Fill;
            accentRow.ColumnCount = 2;
            accentRow.RowCount = 1;
            accentRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            accentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            accentRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));

            Label accentLabel = Ui.Label(
                "强调色（按钮与高亮颜色）",
                Palette.Body,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            accentLabel.Dock = DockStyle.Fill;

            FlowLayoutPanel accentButtons = new FlowLayoutPanel();
            accentButtons.Dock = DockStyle.Fill;
            accentButtons.FlowDirection = FlowDirection.LeftToRight;
            accentButtons.WrapContents = false;
            accentButtons.Margin = new Padding(0);

            foreach (string accentName in Palette.AccentNames)
            {
                bool selected = String.Equals(
                    accentName,
                    Palette.AccentName,
                    StringComparison.Ordinal);
                Button accentButton = new Button();
                accentButton.Width = 34;
                accentButton.Height = 26;
                accentButton.Margin = new Padding(0, 5, 8, 5);
                accentButton.FlatStyle = FlatStyle.Flat;
                accentButton.FlatAppearance.BorderSize = selected ? 2 : 1;
                accentButton.FlatAppearance.BorderColor = selected
                    ? Palette.Ink
                    : Palette.Border;
                accentButton.BackColor = Palette.AccentPreview(accentName);
                accentButton.Cursor = Cursors.Hand;
                accentButton.Tag = accentName;
                accentButton.Click += delegate(object sender, EventArgs e)
                {
                    Button button = sender as Button;
                    if (button == null)
                    {
                        return;
                    }
                    _owner.SwitchAccent(Convert.ToString(button.Tag));
                };
                _tips.SetToolTip(
                    accentButton,
                    Palette.AccentDisplayName(accentName));
                accentButtons.Controls.Add(accentButton);
            }
            accentRow.Controls.Add(accentLabel, 0, 0);
            accentRow.Controls.Add(accentButtons, 1, 0);
            preferenceLayout.Controls.Add(accentRow, 0, 4);
            preferenceLayout.SetColumnSpan(accentRow, 2);

            _scheduledCleanup = CreateSettingCheck(
                "每周日 12:00 自动清理临时文件（登录后运行）",
                false);
            _scheduledCleanup.CheckedChanged += delegate
            {
                if (_loading)
                {
                    return;
                }
                string message;
                bool success;
                if (_scheduledCleanup.Checked)
                {
                    success = ScheduledCleanupService.Enable(out message);
                }
                else
                {
                    success = ScheduledCleanupService.Disable(out message);
                }
                if (!success)
                {
                    _loading = true;
                    _scheduledCleanup.Checked = ScheduledCleanupService.IsEnabled();
                    _loading = false;
                }
                _preferenceStatus.Text = message;
                _preferenceStatus.ForeColor = success ? Palette.Success : Palette.Danger;
            };
            preferenceLayout.Controls.Add(_scheduledCleanup, 0, 5);
            preferenceLayout.SetColumnSpan(_scheduledCleanup, 2);

            _preferenceStatus = Ui.Label(
                "",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            _preferenceStatus.Dock = DockStyle.Fill;
            _preferenceStatus.Margin = new Padding(0, 14, 0, 0);
            preferenceLayout.Controls.Add(_preferenceStatus, 0, 6);
            preferenceLayout.SetColumnSpan(_preferenceStatus, 2);
            preferences.Controls.Add(preferenceLayout);
            body.Controls.Add(preferences, 1, 0);

            RoundedPanel about = new RoundedPanel();
            about.Dock = DockStyle.Fill;
            about.Padding = new Padding(18, 12, 18, 10);
            TableLayoutPanel aboutLayout = new TableLayoutPanel();
            aboutLayout.Dock = DockStyle.Fill;
            aboutLayout.ColumnCount = 1;
            aboutLayout.RowCount = 3;
            aboutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            aboutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            Label aboutTitle = Ui.Label(
                "更新与发布",
                Palette.BodyBold,
                Palette.Ink,
                ContentAlignment.TopLeft);
            aboutTitle.Dock = DockStyle.Fill;
            Label aboutText = Ui.Label(
                "软件更新只从 jokerx.top 获取，不依赖 GitHub 代理。程序未签名，如 SmartScreen 提示风险，可核对发布页的 SHA-256 或提交误报。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            aboutText.Dock = DockStyle.Fill;
            LinkLabel links = new LinkLabel();
            links.Dock = DockStyle.Fill;
            links.Text = "jokerx.top 软件管理    GitHub 源码    Microsoft 误报提交";
            links.Links.Add(
                links.Text.IndexOf("jokerx.top"),
                "jokerx.top".Length,
                "https://jokerx.top/projects/software/index.html");
            links.Links.Add(
                links.Text.IndexOf("GitHub 源码"),
                "GitHub 源码".Length,
                "https://github.com/jokerXYH/pc-manager-lite");
            links.Links.Add(
                links.Text.IndexOf("Microsoft 误报提交"),
                "Microsoft 误报提交".Length,
                "https://www.microsoft.com/en-us/wdsi/filesubmission");
            links.LinkColor = Palette.Accent;
            links.ActiveLinkColor = Palette.AccentHover;
            links.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                try
                {
                    Process.Start(Convert.ToString(e.Link.LinkData));
                }
                catch
                {
                }
            };
            aboutLayout.Controls.Add(aboutTitle, 0, 0);
            aboutLayout.Controls.Add(aboutText, 0, 1);
            aboutLayout.Controls.Add(links, 0, 2);
            about.Controls.Add(aboutLayout);
            root.Controls.Add(about, 0, 2);

            Load += delegate { RefreshState(); };
        }

        public void AutoCheck()
        {
            if (_settings.CheckUpdatesOnStart)
            {
                CheckUpdates(false);
            }
        }

        public void RefreshState()
        {
            _loading = true;
            _checkOnStart.Checked = _settings.CheckUpdatesOnStart;
            _startWithWindows.Checked = AppStartupService.IsEnabled();
            try
            {
                _scheduledCleanup.Checked = ScheduledCleanupService.IsEnabled();
            }
            catch
            {
                _scheduledCleanup.Checked = false;
            }
            _loading = false;
            SetThemeButtonState(_systemThemeButton, _settings.Theme == "system");
            SetThemeButtonState(_lightThemeButton, _settings.Theme == "light");
            SetThemeButtonState(_darkThemeButton, _settings.Theme == "dark");
            _preferenceStatus.Text = AppStartupService.IsEnabled()
                ? "当前已启用开机自启动"
                : "当前未启用开机自启动";
            _preferenceStatus.ForeColor = Palette.Muted;
        }

        private void CheckUpdates(bool manual)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetUpdateBusy(true, "正在连接 jokerx.top 检查更新…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateCheckResult result = UpdateService.Check();
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    _lastCheck = result;
                    SetUpdateBusy(false, null);
                    ShowCheckResult(result);
                    if (manual && !String.IsNullOrWhiteSpace(result.Error))
                    {
                        FileLogger.Write("手动检查更新失败: " + result.Error);
                    }
                });
            });
        }

        private void ShowCheckResult(UpdateCheckResult result)
        {
            _currentVersion.Text = "当前版本 " + result.CurrentVersion;
            if (!String.IsNullOrWhiteSpace(result.Error))
            {
                _latestVersion.Text = "检查失败";
                _updateStatus.Text = result.Error;
                _updateStatus.ForeColor = Palette.Danger;
                _releaseNotes.Text = "更新服务器暂时不可用，可稍后重试。";
                _installButton.Text = "暂不可用";
                _installButton.Enabled = false;
                return;
            }
            _latestVersion.Text = "服务器版本 " + result.LatestVersion;
            _updateStatus.ForeColor = Palette.Success;
            if (result.Available)
            {
                _updateStatus.Text = "发现新版本，可直接下载并安装。";
                _releaseNotes.Text = result.Info.Notes.Count == 0
                    ? "暂无更新说明。"
                    : "• " + String.Join("\r\n• ", result.Info.Notes.ToArray());
                _installButton.Text = "下载并安装";
                _installButton.Enabled = true;
            }
            else
            {
                _updateStatus.Text = "当前已是最新版本。";
                _releaseNotes.Text = "无需更新。";
                _installButton.Text = "已是最新";
                _installButton.Enabled = false;
            }
        }

        private void InstallUpdate()
        {
            if (_busy || _lastCheck == null || !_lastCheck.Available)
            {
                return;
            }
            UpdateInfo info = _lastCheck.Info;
            _busy = true;
            SetUpdateBusy(true, "正在下载并校验更新…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string downloaded;
                string error;
                bool ok = UpdateService.DownloadVerified(
                    info,
                    delegate(string text)
                    {
                        SafeBeginInvoke(delegate
                        {
                            _updateStatus.Text = text;
                        });
                    },
                    out downloaded,
                    out error);
                SafeBeginInvoke(delegate
                {
                    if (!ok)
                    {
                        _busy = false;
                        SetUpdateBusy(false, null);
                        _updateStatus.Text = "更新失败：" + error;
                        _updateStatus.ForeColor = Palette.Danger;
                        return;
                    }
                    _updateStatus.Text = "更新校验通过，正在重启安装…";
                    _updateStatus.ForeColor = Palette.Success;
                    if (UpdateService.StartUpdaterAndExit(downloaded))
                    {
                        Application.Exit();
                    }
                    else
                    {
                        _busy = false;
                        SetUpdateBusy(false, null);
                        _updateStatus.Text = "无法启动更新程序，请检查文件权限。";
                        _updateStatus.ForeColor = Palette.Danger;
                    }
                });
            });
        }

        private void RequestTheme(Button button)
        {
            string theme = Convert.ToString(button.Tag);
            if (String.Equals(theme, _settings.Theme, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _preferenceStatus.Visible = true;
            _preferenceStatus.Text = "正在切换主题…";
            _preferenceStatus.ForeColor = Palette.AccentDark;
            BeginInvoke(new Action(delegate
            {
                _owner.SwitchTheme(theme);
            }));
        }

        private static void SetThemeButtonState(Button button, bool active)
        {
            button.BackColor = active ? Palette.Accent : Palette.Card;
            button.ForeColor = active ? Color.White : Palette.Ink;
            button.FlatAppearance.BorderSize = active ? 0 : 1;
            button.FlatAppearance.BorderColor = Palette.Border;
        }

        private void SetUpdateBusy(bool busy, string text)
        {
            _checkButton.Enabled = !busy;
            _installButton.Enabled = !busy && _lastCheck != null && _lastCheck.Available;
            _progress.Visible = busy;
            _checkButton.Text = busy ? "检查中…" : "检查更新";
            if (!String.IsNullOrWhiteSpace(text))
            {
                _updateStatus.Text = text;
                _updateStatus.ForeColor = Palette.AccentDark;
            }
        }

        private static CheckBox CreateSettingCheck(string text, bool value)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.Checked = value;
            check.AutoSize = true;
            check.Font = Palette.Body;
            check.ForeColor = Palette.Ink;
            check.Margin = new Padding(0, 7, 0, 4);
            check.Cursor = Cursors.Hand;
            return check;
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class StartupPage : UserControl
    {
        private readonly DataGridView _grid;
        private readonly Label _totalValue;
        private readonly Label _disableableValue;
        private readonly Label _selfValue;
        private readonly Label _status;
        private readonly Button _scanButton;
        private readonly Button _disableButton;
        private readonly Button _restoreButton;
        private readonly TextBox _search;
        private List<StartupItem> _items;
        private bool _busy;

        public StartupPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "启动项管理",
                    "检测注册表 Run、启动文件夹和自动服务；只禁用开机启动，不结束当前程序",
                    null,
                    null),
                0,
                0);

            TableLayoutPanel stats = new TableLayoutPanel();
            stats.Dock = DockStyle.Fill;
            stats.Margin = new Padding(0, 0, 0, 12);
            stats.ColumnCount = 3;
            stats.RowCount = 1;
            stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++)
            {
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            _totalValue = AddStatCard(stats, 0, "检测到的启动项", "正在扫描", Palette.Accent);
            _disableableValue = AddStatCard(stats, 1, "可安全配置", "正在扫描", Palette.Aqua);
            _selfValue = AddStatCard(stats, 2, "本工具自启动", "未注册", Palette.Success);
            root.Controls.Add(stats, 0, 1);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.Margin = new Padding(0, 0, 0, 10);
            Label searchLabel = Ui.Label(
                "搜索",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            searchLabel.AutoSize = true;
            searchLabel.Location = new Point(0, 15);
            toolbar.Controls.Add(searchLabel);

            _search = new TextBox();
            _search.AutoSize = false;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = Palette.Body;
            _search.BackColor = Palette.Card;
            _search.ForeColor = Palette.Ink;
            _search.Location = new Point(42, 11);
            _search.Height = 30;
            _search.Width = 330;
            _search.TextChanged += delegate { ApplySearchFilter(true); };
            _search.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(_search.Handle, Palette.IsDark, false);
                NativeSystem.SetCueBanner(_search.Handle, "输入名称、来源或命令关键字…");
            };
            toolbar.Controls.Add(_search);

            Label filterHint = Ui.Label(
                "按名称、来源或启动命令过滤，勾选状态会保留",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            filterHint.AutoSize = true;
            filterHint.Location = new Point(390, 15);
            toolbar.Controls.Add(filterHint);
            root.Controls.Add(toolbar, 0, 2);

            RoundedPanel gridPanel = new RoundedPanel();
            gridPanel.Dock = DockStyle.Fill;
            gridPanel.Margin = new Padding(0, 0, 0, 12);
            gridPanel.Padding = new Padding(12);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            Ui.StyleGrid(_grid);

            DataGridViewCheckBoxColumn selected = new DataGridViewCheckBoxColumn();
            selected.Name = "Selected";
            selected.HeaderText = "选择";
            selected.FillWeight = 9F;
            selected.MinimumWidth = 52;

            DataGridViewTextBoxColumn name = new DataGridViewTextBoxColumn();
            name.Name = "Name";
            name.HeaderText = "启动项";
            name.ReadOnly = true;
            name.FillWeight = 19F;
            name.MinimumWidth = 130;

            DataGridViewTextBoxColumn source = new DataGridViewTextBoxColumn();
            source.Name = "Source";
            source.HeaderText = "来源";
            source.ReadOnly = true;
            source.FillWeight = 17F;
            source.MinimumWidth = 125;

            DataGridViewTextBoxColumn risk = new DataGridViewTextBoxColumn();
            risk.Name = "Risk";
            risk.HeaderText = "影响";
            risk.ReadOnly = true;
            risk.FillWeight = 11F;
            risk.MinimumWidth = 80;

            DataGridViewTextBoxColumn command = new DataGridViewTextBoxColumn();
            command.Name = "Command";
            command.HeaderText = "启动命令";
            command.ReadOnly = true;
            command.FillWeight = 27F;
            command.MinimumWidth = 220;

            DataGridViewTextBoxColumn note = new DataGridViewTextBoxColumn();
            note.Name = "Note";
            note.HeaderText = "说明";
            note.ReadOnly = true;
            note.FillWeight = 24F;
            note.MinimumWidth = 180;

            _grid.Columns.Add(selected);
            _grid.Columns.Add(name);
            _grid.Columns.Add(source);
            _grid.Columns.Add(risk);
            _grid.Columns.Add(command);
            _grid.Columns.Add(note);
            gridPanel.Controls.Add(_grid);
            root.Controls.Add(gridPanel, 0, 3);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            root.Controls.Add(actions, 0, 4);

            _status = Ui.Label(
                "尚未扫描启动项",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _status.AutoSize = true;
            _status.Location = new Point(2, 20);
            actions.Controls.Add(_status);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 510;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(0, 9, 0, 0);

            _disableButton = Ui.CreateButton("禁用选中启动", true);
            _disableButton.Width = 124;
            _disableButton.Click += delegate { DisableSelected(); };

            _scanButton = Ui.CreateButton("扫描启动项", false);
            _scanButton.Width = 104;
            _scanButton.Click += delegate { Scan(); };

            _restoreButton = Ui.CreateButton("恢复已禁用", false);
            _restoreButton.Width = 112;
            _restoreButton.Click += delegate { RestoreDisabled(); };

            buttons.Controls.Add(_disableButton);
            buttons.Controls.Add(_scanButton);
            buttons.Controls.Add(_restoreButton);
            actions.Controls.Add(buttons);
            actions.Resize += delegate
            {
                buttons.Left = actions.ClientSize.Width - buttons.Width;
            };

            Load += delegate { Scan(); };
        }

        public void Scan()
        {
            if (_busy)
            {
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                _items = StartupManagerService.Scan();
                _grid.Rows.Clear();
                int canDisable = 0;
                bool selfStartup = false;
                string currentExe = Path.GetFileName(
                    Process.GetCurrentProcess().MainModule.FileName);
                foreach (StartupItem item in _items)
                {
                    int index = _grid.Rows.Add();
                    DataGridViewRow row = _grid.Rows[index];
                    row.Tag = item;
                    row.Cells["Selected"].Value = false;
                    row.Cells["Name"].Value = item.Name;
                    row.Cells["Source"].Value = item.SourceLabel;
                    row.Cells["Risk"].Value = item.RiskLabel;
                    row.Cells["Command"].Value = item.Command;
                    row.Cells["Note"].Value = item.Note;
                    if (!item.CanDisable)
                    {
                        row.Cells["Selected"].ReadOnly = true;
                        row.Cells["Risk"].Style.ForeColor = Palette.Danger;
                        row.DefaultCellStyle.ForeColor = Palette.Muted;
                    }
                    else
                    {
                        canDisable++;
                        row.Cells["Risk"].Style.ForeColor =
                            item.SourceType == StartupSourceType.Service
                                ? Palette.Warning
                                : Palette.Success;
                    }
                    if (!String.IsNullOrWhiteSpace(item.Command) &&
                        item.Command.IndexOf(
                            currentExe,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        selfStartup = true;
                    }
                }
                _totalValue.Text = _items.Count + " 项";
                _disableableValue.Text = canDisable + " 项";
                _selfValue.Text = selfStartup ? "已注册" : "未注册";
                _selfValue.ForeColor = selfStartup ? Palette.Warning : Palette.Success;
                int disabled = StartupManagerService.DisabledBackupCount();
                _status.Text = disabled > 0
                    ? String.Format(
                        "扫描完成；当前有 {0} 个启动项保持启用，{1} 个已由本工具禁用。",
                        _items.Count,
                        disabled)
                    : "扫描完成；勾选后点击“禁用选中启动”会立即执行。";
                _status.ForeColor = Palette.Muted;
                _restoreButton.Enabled = disabled > 0;
                ApplySearchFilter(false);
            }
            catch (Exception ex)
            {
                _status.Text = "扫描失败：" + ex.Message;
                _status.ForeColor = Palette.Danger;
                FileLogger.Write("扫描启动项失败: " + ex);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ApplySearchFilter(bool updateStatus)
        {
            string keyword = _search == null ? String.Empty : _search.Text.Trim();
            int shown = 0;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                StartupItem item = row.Tag as StartupItem;
                if (item == null)
                {
                    continue;
                }
                bool visible = keyword.Length == 0 ||
                    ContainsText(item.Name, keyword) ||
                    ContainsText(item.SourceLabel, keyword) ||
                    ContainsText(item.Command, keyword);
                row.Visible = visible;
                if (visible)
                {
                    shown++;
                }
            }
            if (updateStatus && _items != null && !_busy)
            {
                _status.Text = keyword.Length == 0
                    ? String.Format(
                        "共 {0} 个启动项；勾选后点击“禁用选中启动”会立即执行。",
                        _items.Count)
                    : String.Format(
                        "共 {0} 个启动项，匹配“{1}”的 {2} 个。",
                        _items.Count,
                        keyword,
                        shown);
                _status.ForeColor = Palette.Muted;
            }
        }

        private static bool ContainsText(string text, string keyword)
        {
            return !String.IsNullOrWhiteSpace(text) &&
                text.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private void DisableSelected()
        {
            if (_busy)
            {
                return;
            }
            List<StartupItem> selected = new List<StartupItem>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                object value = row.Cells["Selected"].Value;
                if (value is bool && (bool)value && row.Tag is StartupItem)
                {
                    selected.Add((StartupItem)row.Tag);
                }
            }
            if (selected.Count == 0)
            {
                _status.Text = "请先勾选要禁用的启动项。";
                _status.ForeColor = Palette.Warning;
                return;
            }

            _busy = true;
            SetBusy(true, "正在禁用 " + selected.Count + " 个启动项…");
            try
            {
                StartupActionResult result = StartupManagerService.Disable(selected);
                _busy = false;
                SetBusy(false, null);
                Scan();
                _status.Text = String.Format(
                    "已禁用 {0} 项，失败 {1} 项。当前运行中的程序不会被结束。",
                    result.Changed,
                    result.Failed);
                _status.ForeColor = result.Failed == 0 ? Palette.Success : Palette.Warning;
            }
            catch (Exception ex)
            {
                _status.Text = "禁用失败：" + ex.Message;
                _status.ForeColor = Palette.Danger;
            }
            finally
            {
                if (_busy)
                {
                    _busy = false;
                    SetBusy(false, null);
                }
            }
        }

        private void RestoreDisabled()
        {
            if (_busy || StartupManagerService.DisabledBackupCount() == 0)
            {
                _status.Text = "没有已禁用的启动项备份。";
                _status.ForeColor = Palette.Muted;
                return;
            }
            _busy = true;
            SetBusy(true, "正在恢复启动项…");
            try
            {
                StartupActionResult result = StartupManagerService.RestoreAll();
                _busy = false;
                SetBusy(false, null);
                Scan();
                _status.Text = String.Format(
                    "已恢复 {0} 项，失败 {1} 项。",
                    result.Changed,
                    result.Failed);
                _status.ForeColor = result.Failed == 0 ? Palette.Success : Palette.Warning;
            }
            catch (Exception ex)
            {
                _status.Text = "恢复失败：" + ex.Message;
                _status.ForeColor = Palette.Danger;
            }
            finally
            {
                if (_busy)
                {
                    _busy = false;
                    SetBusy(false, null);
                }
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _grid.Enabled = !busy;
            _scanButton.Enabled = !busy;
            _disableButton.Enabled = !busy;
            _restoreButton.Enabled =
                !busy && StartupManagerService.DisabledBackupCount() > 0;
            if (!String.IsNullOrWhiteSpace(text))
            {
                _status.Text = text;
                _status.ForeColor = Palette.AccentDark;
            }
        }

        private Label AddStatCard(
            TableLayoutPanel parent,
            int column,
            string title,
            string value,
            Color accent)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(column == 0 ? 0 : 7, 0, column == 2 ? 0 : 7, 0);
            card.Padding = new Padding(16, 10, 16, 10);

            Label valueLabel = Ui.Label(
                value,
                new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                accent,
                ContentAlignment.MiddleLeft);
            valueLabel.Dock = DockStyle.Fill;
            card.Controls.Add(valueLabel);

            Label titleLabel = Ui.Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 22;
            card.Controls.Add(titleLabel);
            parent.Controls.Add(card, column, 0);
            return valueLabel;
        }
    }

    internal sealed class RecoveryPage : UserControl
    {
        private readonly MainForm _owner;
        private readonly Label _backupValue;
        private readonly Label _backupDetail;
        private readonly Label _status;
        private readonly RichTextBox _activity;
        private readonly Button _restoreButton;
        private readonly Button _checkpointButton;
        private bool _busy;

        public RecoveryPage(MainForm owner)
        {
            _owner = owner;
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "恢复中心",
                    "恢复本工具修改过的系统设置，或使用 Windows 自带还原点",
                    null,
                    null),
                0,
                0);

            TableLayoutPanel cards = new TableLayoutPanel();
            cards.Dock = DockStyle.Fill;
            cards.Margin = new Padding(0, 0, 0, 12);
            cards.ColumnCount = 2;
            cards.RowCount = 1;
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            RoundedPanel backupCard = new RoundedPanel();
            backupCard.Dock = DockStyle.Fill;
            backupCard.Margin = new Padding(0, 0, 7, 0);
            backupCard.Padding = new Padding(18, 12, 18, 10);

            Label backupTitle = Ui.Label(
                "本工具设置备份",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            backupTitle.Dock = DockStyle.Top;
            backupTitle.Height = 24;
            _backupValue = Ui.Label(
                "正在检查…",
                new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            _backupValue.Dock = DockStyle.Top;
            _backupValue.Height = 36;
            _backupDetail = Ui.Label(
                "电源、Game Mode、Game DVR 与虚拟内存位置",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _backupDetail.Dock = DockStyle.Fill;
            backupCard.Controls.Add(_backupDetail);
            backupCard.Controls.Add(_backupValue);
            backupCard.Controls.Add(backupTitle);
            cards.Controls.Add(backupCard, 0, 0);

            RoundedPanel checkpointCard = new RoundedPanel();
            checkpointCard.Dock = DockStyle.Fill;
            checkpointCard.Margin = new Padding(7, 0, 0, 0);
            checkpointCard.Padding = new Padding(18, 12, 18, 10);

            Label checkpointTitle = Ui.Label(
                "Windows 系统还原",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            checkpointTitle.Dock = DockStyle.Top;
            checkpointTitle.Height = 24;
            Label checkpointValue = Ui.Label(
                "由系统保护决定",
                new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                Palette.Accent,
                ContentAlignment.MiddleLeft);
            checkpointValue.Dock = DockStyle.Top;
            checkpointValue.Height = 36;
            Label checkpointDetail = Ui.Label(
                "可创建还原点、打开系统还原，不会自动启用系统保护",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            checkpointDetail.Dock = DockStyle.Fill;
            checkpointCard.Controls.Add(checkpointDetail);
            checkpointCard.Controls.Add(checkpointValue);
            checkpointCard.Controls.Add(checkpointTitle);
            cards.Controls.Add(checkpointCard, 1, 0);
            root.Controls.Add(cards, 0, 1);

            RoundedPanel actionsCard = new RoundedPanel();
            actionsCard.Dock = DockStyle.Fill;
            actionsCard.Margin = new Padding(0, 0, 0, 12);
            actionsCard.Padding = new Padding(14, 13, 14, 13);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;

            _restoreButton = Ui.CreateButton("恢复本工具修改", true);
            _restoreButton.Width = 138;
            _restoreButton.Click += delegate { RestoreToolSettings(); };
            _checkpointButton = Ui.CreateButton("创建系统还原点", false);
            _checkpointButton.Width = 132;
            _checkpointButton.Click += delegate { CreateCheckpoint(); };
            Button openRestore = Ui.CreateButton("打开系统还原", false);
            openRestore.Width = 118;
            openRestore.Click += delegate { OpenSystemRestore(); };
            Button openLogs = Ui.CreateButton("打开操作日志", false);
            openLogs.Width = 118;
            openLogs.Click += delegate { OpenLogs(); };
            actions.Controls.Add(_restoreButton);
            actions.Controls.Add(_checkpointButton);
            actions.Controls.Add(openRestore);
            actions.Controls.Add(openLogs);
            actionsCard.Controls.Add(actions);
            root.Controls.Add(actionsCard, 0, 2);

            RoundedPanel activityCard = new RoundedPanel();
            activityCard.Dock = DockStyle.Fill;
            activityCard.Margin = new Padding(0, 0, 0, 10);
            activityCard.Padding = new Padding(16, 12, 16, 12);
            TableLayoutPanel activityLayout = new TableLayoutPanel();
            activityLayout.Dock = DockStyle.Fill;
            activityLayout.ColumnCount = 1;
            activityLayout.RowCount = 2;
            activityLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            activityLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _status = Ui.Label(
                "恢复记录",
                Palette.SectionTitle,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            _status.Dock = DockStyle.Fill;
            _activity = new RichTextBox();
            _activity.Dock = DockStyle.Fill;
            _activity.ReadOnly = true;
            _activity.BorderStyle = BorderStyle.None;
            _activity.BackColor = Palette.Card;
            _activity.ForeColor = Palette.Ink;
            _activity.Font = Palette.Body;
            _activity.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(_activity.Handle, Palette.IsDark, false);
            };
            _activity.Text = "尚未执行恢复操作。";
            activityLayout.Controls.Add(_status, 0, 0);
            activityLayout.Controls.Add(_activity, 0, 1);
            activityCard.Controls.Add(activityLayout);
            root.Controls.Add(activityCard, 0, 3);

            Label attribution = Ui.Label(
                "清理安全策略参考 BitBroom（MIT）· 帧时间采样使用 Intel PresentMon（MIT）",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            attribution.Dock = DockStyle.Fill;
            root.Controls.Add(attribution, 0, 4);

            Load += delegate { RefreshState(); };
        }

        public void RefreshState()
        {
            BackupStore backup = BackupStore.Load();
            if (backup.HasSettings)
            {
                _backupValue.Text = "可恢复";
                _backupValue.ForeColor = Palette.Success;
                string detail = "已保存首次优化前的系统设置";
                try
                {
                    if (File.Exists(AppPaths.BackupFile))
                    {
                        detail = "备份时间 " +
                            File.GetLastWriteTime(AppPaths.BackupFile).ToString("MM-dd HH:mm");
                    }
                }
                catch
                {
                }
                int disabledStartup = StartupManagerService.DisabledBackupCount();
                if (disabledStartup > 0)
                {
                    detail += "；另有 " + disabledStartup + " 个启动项已禁用";
                }
                _backupDetail.Text = detail;
            }
            else
            {
                _backupValue.Text = "暂无备份";
                _backupValue.ForeColor = Palette.Muted;
                _backupDetail.Text = "执行一次游戏加速后会自动保存原始设置";
            }
            _restoreButton.Enabled = !_busy && backup.HasSettings;
        }

        private void RestoreToolSettings()
        {
            if (_busy)
            {
                return;
            }
            BackupStore backup = BackupStore.Load();
            if (!backup.HasSettings)
            {
                _status.Text = "没有可恢复的设置";
                _status.ForeColor = Palette.Warning;
                return;
            }
            _busy = true;
            SetBusy(true, "正在恢复系统设置…");
            try
            {
                bool reboot;
                List<string> lines = SystemOptimizer.RestoreAll(backup, out reboot);
                StringBuilder message = new StringBuilder();
                foreach (string line in lines)
                {
                    message.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line);
                }
                if (reboot)
                {
                    message.AppendLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 虚拟内存修改需要重启后生效");
                }
                _activity.Text = message.ToString();
                _status.Text = "已恢复本工具修改";
                _status.ForeColor = Palette.Success;
                _owner.UpdateKnownSystemState();
            }
            catch (Exception ex)
            {
                _status.Text = "恢复失败：" + ex.Message;
                _status.ForeColor = Palette.Danger;
            }
            finally
            {
                _busy = false;
                SetBusy(false, _status.Text);
                RefreshState();
            }
        }

        private void CreateCheckpoint()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetBusy(true, "正在创建 Windows 还原点，可能需要一到两分钟…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemRestoreResult result = SystemRestoreService.CreateRestorePoint();
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false, result.Message);
                    _activity.AppendText(
                        "\r\n[" + DateTime.Now.ToString("HH:mm:ss") + "] " +
                        result.Message + "\r\n");
                    _status.Text = result.Message;
                    _status.ForeColor =
                        result.Success || result.Skipped ? Palette.Success : Palette.Danger;
                    RefreshState();
                });
            });
        }

        private void OpenSystemRestore()
        {
            try
            {
                SystemRestoreService.OpenSystemRestore();
            }
            catch (Exception ex)
            {
                _status.Text = "无法打开系统还原：" + ex.Message;
                _status.ForeColor = Palette.Danger;
            }
        }

        private void OpenLogs()
        {
            try
            {
                SystemRestoreService.OpenLogs();
            }
            catch (Exception ex)
            {
                _status.Text = "无法打开日志目录：" + ex.Message;
                _status.ForeColor = Palette.Danger;
            }
        }

        private void SetBusy(bool busy, string text)
        {
            _restoreButton.Enabled = !busy && BackupStore.Load().HasSettings;
            _checkpointButton.Enabled = !busy;
            if (!String.IsNullOrWhiteSpace(text))
            {
                _status.Text = text;
                _status.ForeColor = busy ? Palette.AccentDark : _status.ForeColor;
            }
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class SecurityPage : UserControl
    {
        private readonly Label _realtimeValue;
        private readonly Label _signatureValue;
        private readonly Label _scanValue;
        private readonly Label _status;
        private readonly DataGridView _grid;
        private readonly ProgressBar _progress;
        private readonly Button _quickButton;
        private readonly Button _customButton;
        private readonly Button _updateButton;
        private readonly Button _stopButton;
        private readonly Button _openButton;
        private readonly System.Windows.Forms.Timer _scanTimer;
        private bool _busy;
        private bool _loading;
        private bool _cancelRequested;
        private DateTime _scanStarted;
        private string _scanPrefix = "";
        private DateTime _lastRefresh = DateTime.MinValue;

        public SecurityPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "安全防护",
                    "调用 Windows Defender 检查系统安全，不常驻后台、不加载病毒库",
                    "刷新状态",
                    delegate { RefreshState(true); }),
                0,
                0);

            TableLayoutPanel stats = new TableLayoutPanel();
            stats.Dock = DockStyle.Fill;
            stats.Margin = new Padding(0, 0, 0, 12);
            stats.ColumnCount = 3;
            stats.RowCount = 1;
            stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++)
            {
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            _realtimeValue = Ui.BuildStatCard(stats, 0, "实时保护", "读取中", Palette.Success);
            _signatureValue = Ui.BuildStatCard(stats, 1, "病毒库版本", "读取中", Palette.Accent);
            _scanValue = Ui.BuildStatCard(stats, 2, "上次快速扫描", "读取中", Palette.Warning);
            root.Controls.Add(stats, 0, 1);

            RoundedPanel actions = new RoundedPanel();
            actions.Dock = DockStyle.Fill;
            actions.Margin = new Padding(0, 0, 0, 12);
            actions.Padding = new Padding(14, 10, 14, 9);

            TableLayoutPanel actionLayout = new TableLayoutPanel();
            actionLayout.Dock = DockStyle.Fill;
            actionLayout.ColumnCount = 2;
            actionLayout.RowCount = 1;
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 600F));
            actionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.LeftToRight;
            buttons.WrapContents = false;
            buttons.Margin = new Padding(0);

            _quickButton = Ui.CreateButton("快速扫描", true);
            _quickButton.Width = 104;
            _quickButton.Click += delegate { StartScan(true); };
            _customButton = Ui.CreateButton("自定义扫描", false);
            _customButton.Width = 108;
            _customButton.Click += delegate { StartScan(false); };
            _updateButton = Ui.CreateButton("更新病毒库", false);
            _updateButton.Width = 108;
            _updateButton.Click += delegate { UpdateSignatures(); };
            _stopButton = Ui.CreateButton("停止扫描", false);
            _stopButton.Width = 90;
            _stopButton.Enabled = false;
            _stopButton.Click += delegate { StopScan(); };
            _openButton = Ui.CreateButton("Windows 安全中心", false);
            _openButton.Width = 132;
            _openButton.Click += delegate { DefenderService.OpenWindowsSecurity(); };
            buttons.Controls.Add(_quickButton);
            buttons.Controls.Add(_customButton);
            buttons.Controls.Add(_updateButton);
            buttons.Controls.Add(_stopButton);
            buttons.Controls.Add(_openButton);

            Panel statusPanel = new Panel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.Margin = new Padding(10, 0, 0, 0);
            _status = Ui.Label(
                "正在读取 Windows Defender 状态…",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _status.Dock = DockStyle.Fill;
            _progress = new ProgressBar();
            _progress.Dock = DockStyle.Bottom;
            _progress.Height = 4;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;
            statusPanel.Controls.Add(_status);
            statusPanel.Controls.Add(_progress);

            _scanTimer = new System.Windows.Forms.Timer();
            _scanTimer.Interval = 1000;
            _scanTimer.Tick += delegate
            {
                if (!_busy)
                {
                    return;
                }
                int seconds = (int)Math.Max(
                    0,
                    (DateTime.Now - _scanStarted).TotalSeconds);
                _status.Text = String.Format(
                    "{0} 已运行 {1} 分 {2} 秒，请保持电脑通电。",
                    _scanPrefix,
                    seconds / 60,
                    seconds % 60);
            };

            actionLayout.Controls.Add(buttons, 0, 0);
            actionLayout.Controls.Add(statusPanel, 1, 0);
            actions.Controls.Add(actionLayout);
            root.Controls.Add(actions, 0, 2);

            RoundedPanel gridCard = new RoundedPanel();
            gridCard.Dock = DockStyle.Fill;
            gridCard.Margin = new Padding(0, 0, 0, 12);
            gridCard.Padding = new Padding(12);
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            Ui.StyleGrid(_grid);
            _grid.ReadOnly = true;

            DataGridViewTextBoxColumn time = new DataGridViewTextBoxColumn();
            time.Name = "Time";
            time.HeaderText = "检测时间";
            time.FillWeight = 16F;
            time.MinimumWidth = 118;

            DataGridViewTextBoxColumn threat = new DataGridViewTextBoxColumn();
            threat.Name = "Threat";
            threat.HeaderText = "威胁名称";
            threat.FillWeight = 30F;
            threat.MinimumWidth = 180;

            DataGridViewTextBoxColumn severity = new DataGridViewTextBoxColumn();
            severity.Name = "Severity";
            severity.HeaderText = "级别";
            severity.FillWeight = 10F;
            severity.MinimumWidth = 64;

            DataGridViewTextBoxColumn state = new DataGridViewTextBoxColumn();
            state.Name = "State";
            state.HeaderText = "状态";
            state.FillWeight = 12F;
            state.MinimumWidth = 78;

            DataGridViewTextBoxColumn file = new DataGridViewTextBoxColumn();
            file.Name = "File";
            file.HeaderText = "相关文件";
            file.FillWeight = 32F;
            file.MinimumWidth = 200;

            _grid.Columns.Add(time);
            _grid.Columns.Add(threat);
            _grid.Columns.Add(severity);
            _grid.Columns.Add(state);
            _grid.Columns.Add(file);
            gridCard.Controls.Add(_grid);
            root.Controls.Add(gridCard, 0, 3);

            Label note = Ui.Label(
                "扫描由 Windows Defender 执行，本工具只负责发起任务和展示结果；Defender 的病毒库和扫描引擎常驻内存由系统管理。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            note.Dock = DockStyle.Fill;
            root.Controls.Add(note, 0, 4);

            Load += delegate { RefreshState(false); };
        }

        public void RefreshState()
        {
            RefreshState(false);
        }

        public void RefreshState(bool force)
        {
            if (_busy || _loading)
            {
                return;
            }
            if (!force && _lastRefresh != DateTime.MinValue &&
                DateTime.Now - _lastRefresh < TimeSpan.FromSeconds(60))
            {
                return;
            }
            _loading = true;
            _status.Text = "正在读取 Windows Defender 状态…";
            _status.ForeColor = Palette.Muted;
            ThreadPool.QueueUserWorkItem(delegate
            {
                DefenderStatus status = DefenderService.ReadStatus();
                List<DefenderThreat> threats = DefenderService.ReadThreats();
                bool engine = !String.IsNullOrWhiteSpace(DefenderService.FindMpCmdRun());
                SafeBeginInvoke(delegate { ApplyStatus(status, threats, engine); });
            });
        }

        private void ApplyStatus(
            DefenderStatus status,
            List<DefenderThreat> threats,
            bool engineAvailable)
        {
            _loading = false;
            _lastRefresh = DateTime.Now;

            bool available = status.Available && String.IsNullOrWhiteSpace(status.Error);
            _realtimeValue.Text = available
                ? (status.RealTimeProtectionEnabled ? "已开启" : "已关闭")
                : "不可用";
            _realtimeValue.ForeColor = available && status.RealTimeProtectionEnabled
                ? Palette.Success
                : Palette.Danger;
            _signatureValue.Text = available && !String.IsNullOrWhiteSpace(status.SignatureVersion)
                ? status.SignatureVersion
                : "不可用";
            _scanValue.Text = status.LastQuickScan.HasValue
                ? status.LastQuickScan.Value.ToString("MM-dd HH:mm")
                : "暂无记录";

            _grid.Rows.Clear();
            foreach (DefenderThreat threat in threats)
            {
                int index = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[index];
                row.Cells["Time"].Value = threat.DetectedAt.HasValue
                    ? threat.DetectedAt.Value.ToString("yyyy-MM-dd HH:mm")
                    : "—";
                row.Cells["Threat"].Value = threat.Name;
                row.Cells["Severity"].Value = threat.Severity;
                row.Cells["State"].Value = threat.Status;
                row.Cells["File"].Value = String.IsNullOrWhiteSpace(threat.Resources)
                    ? "—"
                    : threat.Resources;
                row.Cells["File"].ToolTipText = threat.Resources;
                row.Cells["Severity"].Style.ForeColor =
                    String.Equals(threat.Severity, "严重", StringComparison.Ordinal) ||
                    String.Equals(threat.Severity, "高", StringComparison.Ordinal)
                        ? Palette.Danger
                        : Palette.Warning;
            }

            _quickButton.Enabled = engineAvailable && available && !_busy;
            _customButton.Enabled = engineAvailable && available && !_busy;
            _updateButton.Enabled = engineAvailable && available && !_busy;

            if (!available)
            {
                _status.Text = String.IsNullOrWhiteSpace(status.Error)
                    ? "无法读取 Windows Defender 状态，可能由第三方杀毒软件接管。"
                    : "无法读取 Windows Defender 状态：" + status.Error;
                _status.ForeColor = Palette.Warning;
                return;
            }

            string signatureDate = status.SignatureUpdated.HasValue
                ? "，更新于 " + status.SignatureUpdated.Value.ToString("MM-dd HH:mm")
                : String.Empty;
            _status.Text = String.Format(
                "实时保护{0}；扫描在后台进行，可继续使用电脑。历史威胁记录 {1} 条{2}。",
                status.RealTimeProtectionEnabled ? "已开启" : "已关闭",
                threats.Count,
                signatureDate);
            _status.ForeColor = status.RealTimeProtectionEnabled
                ? Palette.Muted
                : Palette.Warning;
        }

        private void StartScan(bool quick)
        {
            if (_busy)
            {
                return;
            }
            string path = null;
            if (!quick)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择要交给 Windows Defender 扫描的文件夹";
                    dialog.ShowNewFolderButton = false;
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    path = dialog.SelectedPath;
                }
            }

            _busy = true;
            _cancelRequested = false;
            _scanStarted = DateTime.Now;
            _scanPrefix = quick
                ? "正在快速扫描系统关键位置…"
                : "正在扫描所选文件夹…";
            SetBusy(true, _scanPrefix);
            _scanTimer.Start();
            ThreadPool.QueueUserWorkItem(delegate
            {
                DefenderScanResult result = DefenderService.Scan(
                    quick,
                    path,
                    delegate(string line)
                    {
                        FileLogger.Write("Defender: " + line);
                    });
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    _scanTimer.Stop();
                    SetBusy(false, null);
                    ShowScanResult(result);
                    RefreshState(true);
                });
            });
        }

        private void ShowScanResult(DefenderScanResult result)
        {
            if (_cancelRequested)
            {
                _status.Text = "扫描已停止。";
                _status.ForeColor = Palette.Warning;
                return;
            }
            if (!String.IsNullOrWhiteSpace(result.Error))
            {
                _status.Text = "扫描失败：" + result.Error;
                _status.ForeColor = Palette.Danger;
                return;
            }
            if (result.ExitCode == 0)
            {
                _status.Text = "扫描完成，未发现威胁。";
                _status.ForeColor = Palette.Success;
                return;
            }
            if (result.ExitCode == 2)
            {
                _status.Text = "扫描完成，发现并处理了威胁，请查看下方记录或打开 Windows 安全中心。";
                _status.ForeColor = Palette.Warning;
                return;
            }
            _status.Text = "扫描结束，退出代码 " + result.ExitCode + "，建议打开 Windows 安全中心查看详情。";
            _status.ForeColor = Palette.Warning;
        }

        private void StopScan()
        {
            if (DefenderService.CancelScan())
            {
                _cancelRequested = true;
                _status.Text = "正在停止扫描…";
                _status.ForeColor = Palette.Warning;
            }
            else
            {
                _status.Text = "当前没有正在运行的扫描。";
                _status.ForeColor = Palette.Muted;
            }
        }

        private void UpdateSignatures()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            SetBusy(true, "正在更新 Windows Defender 病毒库…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string message;
                bool success = DefenderService.UpdateSignatures(out message);
                SafeBeginInvoke(delegate
                {
                    _busy = false;
                    SetBusy(false, null);
                    _status.Text = message;
                    _status.ForeColor = success ? Palette.Success : Palette.Warning;
                    RefreshState(true);
                });
            });
        }

        private void SetBusy(bool busy, string text)
        {
            _quickButton.Enabled = !busy;
            _customButton.Enabled = !busy;
            _updateButton.Enabled = !busy;
            _stopButton.Enabled = busy;
            _progress.Visible = busy;
            if (!String.IsNullOrWhiteSpace(text))
            {
                _status.Text = text;
                _status.ForeColor = Palette.AccentDark;
            }
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class ProgramsPage : UserControl
    {
        private readonly DataGridView _grid;
        private readonly TextBox _search;
        private readonly Label _status;
        private readonly Label _totalValue;
        private readonly Label _uninstallableValue;
        private readonly Label _sizeValue;
        private readonly Button _uninstallButton;
        private readonly Button _forceButton;
        private readonly Button _openButton;
        private List<InstalledProgram> _programs = new List<InstalledProgram>();
        private string _actionMessage;
        private bool _loading;
        private bool _loaded;
        private bool _busyAction;

        public ProgramsPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "软件管理",
                    "查看已安装程序、打开安装目录或调用程序自带的卸载器",
                    "刷新列表",
                    delegate { ScanPrograms(); }),
                0,
                0);

            TableLayoutPanel stats = new TableLayoutPanel();
            stats.Dock = DockStyle.Fill;
            stats.Margin = new Padding(0, 0, 0, 12);
            stats.ColumnCount = 3;
            stats.RowCount = 1;
            stats.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 3; i++)
            {
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            _totalValue = Ui.BuildStatCard(stats, 0, "已安装程序", "正在读取", Palette.Accent);
            _uninstallableValue = Ui.BuildStatCard(stats, 1, "提供卸载器", "正在读取", Palette.Aqua);
            _sizeValue = Ui.BuildStatCard(stats, 2, "登记占用", "正在读取", Palette.Warning);
            root.Controls.Add(stats, 0, 1);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.Margin = new Padding(0, 0, 0, 10);
            Label searchLabel = Ui.Label(
                "搜索",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            searchLabel.AutoSize = true;
            searchLabel.Location = new Point(0, 15);
            toolbar.Controls.Add(searchLabel);

            _search = new TextBox();
            _search.AutoSize = false;
            _search.BorderStyle = BorderStyle.FixedSingle;
            _search.Font = Palette.Body;
            _search.BackColor = Palette.Card;
            _search.ForeColor = Palette.Ink;
            _search.Location = new Point(42, 11);
            _search.Height = 30;
            _search.Width = 330;
            _search.TextChanged += delegate { ApplyFilter(); };
            _search.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(_search.Handle, Palette.IsDark, false);
                NativeSystem.SetCueBanner(_search.Handle, "输入程序名称或发布者…");
            };
            toolbar.Controls.Add(_search);
            root.Controls.Add(toolbar, 0, 2);

            RoundedPanel gridCard = new RoundedPanel();
            gridCard.Dock = DockStyle.Fill;
            gridCard.Margin = new Padding(0, 0, 0, 12);
            gridCard.Padding = new Padding(12);
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            Ui.StyleGrid(_grid);
            _grid.ReadOnly = true;
            _grid.SelectionChanged += delegate { UpdateButtons(); };
            _grid.CellDoubleClick += delegate { OpenLocation(); };

            DataGridViewTextBoxColumn name = new DataGridViewTextBoxColumn();
            name.Name = "Name";
            name.HeaderText = "程序名称";
            name.FillWeight = 32F;
            name.MinimumWidth = 190;

            DataGridViewTextBoxColumn publisher = new DataGridViewTextBoxColumn();
            publisher.Name = "Publisher";
            publisher.HeaderText = "发布者";
            publisher.FillWeight = 22F;
            publisher.MinimumWidth = 130;

            DataGridViewTextBoxColumn version = new DataGridViewTextBoxColumn();
            version.Name = "Version";
            version.HeaderText = "版本";
            version.FillWeight = 12F;
            version.MinimumWidth = 70;

            DataGridViewTextBoxColumn size = new DataGridViewTextBoxColumn();
            size.Name = "Size";
            size.HeaderText = "大小";
            size.FillWeight = 12F;
            size.MinimumWidth = 74;

            DataGridViewTextBoxColumn installed = new DataGridViewTextBoxColumn();
            installed.Name = "Installed";
            installed.HeaderText = "安装日期";
            installed.FillWeight = 12F;
            installed.MinimumWidth = 92;

            DataGridViewTextBoxColumn state = new DataGridViewTextBoxColumn();
            state.Name = "State";
            state.HeaderText = "状态";
            state.ReadOnly = true;
            state.FillWeight = 12F;
            state.MinimumWidth = 90;

            _grid.Columns.Add(name);
            _grid.Columns.Add(publisher);
            _grid.Columns.Add(version);
            _grid.Columns.Add(size);
            _grid.Columns.Add(installed);
            _grid.Columns.Add(state);
            gridCard.Controls.Add(_grid);
            root.Controls.Add(gridCard, 0, 3);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            root.Controls.Add(actions, 0, 4);

            _status = Ui.Label(
                "正在读取已安装程序…",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _status.AutoSize = true;
            _status.Location = new Point(2, 20);
            actions.Controls.Add(_status);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 430;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _uninstallButton = Ui.CreateButton("卸载选中", true);
            _uninstallButton.Width = 108;
            _uninstallButton.Enabled = false;
            _uninstallButton.Click += delegate { UninstallSelected(); };
            _forceButton = Ui.CreateButton("强制移除", false);
            _forceButton.Width = 96;
            _forceButton.Enabled = false;
            _forceButton.ForeColor = Palette.Danger;
            _forceButton.EnabledChanged += delegate
            {
                _forceButton.ForeColor = _forceButton.Enabled
                    ? Palette.Danger
                    : Palette.Muted;
            };
            _forceButton.Click += delegate { ForceRemoveSelected(); };
            _openButton = Ui.CreateButton("打开安装目录", false);
            _openButton.Width = 124;
            _openButton.Enabled = false;
            _openButton.Click += delegate { OpenLocation(); };
            buttons.Controls.Add(_uninstallButton);
            buttons.Controls.Add(_forceButton);
            buttons.Controls.Add(_openButton);
            actions.Controls.Add(buttons);
            actions.Resize += delegate
            {
                buttons.Left = actions.ClientSize.Width - buttons.Width;
            };
        }

        public void EnsureLoaded()
        {
            if (!_loaded && !_loading)
            {
                ScanPrograms();
            }
        }

        private void ScanPrograms()
        {
            if (_loading)
            {
                return;
            }
            _loading = true;
            _uninstallButton.Enabled = false;
            _forceButton.Enabled = false;
            _openButton.Enabled = false;
            _status.Text = "正在读取注册表中的已安装程序…";
            _status.ForeColor = Palette.Muted;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<InstalledProgram> items = InstalledProgramService.Scan();
                SafeBeginInvoke(delegate
                {
                    _loading = false;
                    _loaded = true;
                    _programs = items;
                    UpdateSummary();
                    ApplyFilter();
                });
            });
        }

        private void UpdateSummary()
        {
            int uninstallable = 0;
            long size = 0;
            foreach (InstalledProgram program in _programs)
            {
                if (program.CanUninstall)
                {
                    uninstallable++;
                }
                if (program.SizeBytes > 0)
                {
                    size += program.SizeBytes;
                }
            }
            _totalValue.Text = _programs.Count + " 个";
            _uninstallableValue.Text = uninstallable + " 个";
            _sizeValue.Text = size > 0 ? Format.Bytes(size) : "未登记";
        }

        private void ApplyFilter()
        {
            string keyword = _search == null
                ? String.Empty
                : _search.Text.Trim();
            _grid.Rows.Clear();
            int shown = 0;
            foreach (InstalledProgram program in _programs)
            {
                if (keyword.Length > 0 &&
                    program.Name.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0 &&
                    (program.Publisher == null ||
                     program.Publisher.IndexOf(
                         keyword,
                         StringComparison.CurrentCultureIgnoreCase) < 0))
                {
                    continue;
                }
                int index = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = program;
                row.Cells["Name"].Value = program.Name;
                row.Cells["Publisher"].Value = String.IsNullOrWhiteSpace(program.Publisher)
                    ? "—"
                    : program.Publisher;
                row.Cells["Version"].Value = String.IsNullOrWhiteSpace(program.Version)
                    ? "—"
                    : program.Version;
                row.Cells["Size"].Value = program.SizeBytes > 0
                    ? Format.Bytes(program.SizeBytes)
                    : "—";
                row.Cells["Installed"].Value = String.IsNullOrWhiteSpace(program.InstallDate)
                    ? "—"
                    : program.InstallDate;
                if (program.UninstallerMissing)
                {
                    row.Cells["State"].Value = "卸载器缺失";
                    row.Cells["State"].Style.ForeColor = Palette.Danger;
                }
                else if (!program.CanUninstall)
                {
                    row.Cells["State"].Value = "无卸载命令";
                    row.Cells["State"].Style.ForeColor = Palette.Warning;
                }
                else
                {
                    row.Cells["State"].Value = "可卸载";
                    row.Cells["State"].Style.ForeColor = Palette.Success;
                }
                row.Cells["Name"].ToolTipText = program.Name;
                shown++;
            }
            if (_loaded)
            {
                string message = String.Format(
                    "共 {0} 个程序，当前显示 {1} 个；卸载会调用程序自带卸载器。",
                    _programs.Count,
                    shown);
                if (!String.IsNullOrWhiteSpace(_actionMessage))
                {
                    message = _actionMessage + " " + message;
                    _actionMessage = null;
                }
                _status.Text = message;
                _status.ForeColor = Palette.Muted;
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            InstalledProgram program = SelectedProgram();
            if (_uninstallButton != null)
            {
                _uninstallButton.Enabled = !_loading &&
                    !_busyAction &&
                    program != null &&
                    program.CanUninstall &&
                    !program.UninstallerMissing;
            }
            if (_forceButton != null)
            {
                _forceButton.Enabled = !_loading && !_busyAction && program != null;
            }
            if (_openButton != null)
            {
                _openButton.Enabled = !_loading && !_busyAction && program != null &&
                    !String.IsNullOrWhiteSpace(program.InstallLocation) &&
                    Directory.Exists(program.InstallLocation);
            }
        }

        private InstalledProgram SelectedProgram()
        {
            if (_grid == null || _grid.SelectedRows.Count == 0)
            {
                return null;
            }
            return _grid.SelectedRows[0].Tag as InstalledProgram;
        }

        private void UninstallSelected()
        {
            InstalledProgram program = SelectedProgram();
            if (program == null)
            {
                return;
            }
            DialogResult confirm = MessageBox.Show(
                this,
                "确定要卸载「" + program.Name + "」吗？\n将启动该程序自带的卸载程序，按它自己的流程完成卸载。",
                "确认卸载",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
            UninstallLaunchResult launch = InstalledProgramService.LaunchUninstall(program);
            if (!launch.Started)
            {
                _status.Text = "无法启动卸载程序：" + launch.Error;
                _status.ForeColor = Palette.Danger;
                return;
            }

            SetProgramsBusy(true);
            _status.Text = "已启动「" + program.Name + "」的卸载程序，正在等待其结束…";
            _status.ForeColor = Palette.AccentDark;
            int processId = launch.ProcessId;
            string name = program.Name;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool exited = false;
                try
                {
                    Process process = Process.GetProcessById(processId);
                    exited = process.WaitForExit(15 * 60 * 1000);
                    if (exited)
                    {
                        process.WaitForExit();
                    }
                }
                catch
                {
                    exited = true;
                }
                SafeBeginInvoke(delegate
                {
                    SetProgramsBusy(false);
                    _actionMessage = exited
                        ? "「" + name + "」的卸载程序已退出；如果它仍在列表中，可尝试「强制移除」。"
                        : "「" + name + "」的卸载程序仍在运行，可稍后刷新列表。";
                    _loaded = false;
                    ScanPrograms();
                });
            });
        }

        private void ForceRemoveSelected()
        {
            InstalledProgram program = SelectedProgram();
            if (program == null)
            {
                return;
            }
            DialogResult confirm = MessageBox.Show(
                this,
                "强制移除只删除注册表中的卸载登记，不会运行程序自带的卸载器。\n\n" +
                "程序：" + program.Name + "\n" +
                "发布者：" + (String.IsNullOrWhiteSpace(program.Publisher) ? "未知" : program.Publisher) +
                "\n\n确定继续吗？",
                "强制移除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            bool deleteFolder = false;
            if (!String.IsNullOrWhiteSpace(program.InstallLocation) &&
                Directory.Exists(program.InstallLocation))
            {
                DialogResult folder = MessageBox.Show(
                    this,
                    "是否同时删除安装目录？\n\n" + program.InstallLocation +
                    "\n\n选择「否」只移除注册表登记，文件会保留。",
                    "删除安装目录",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (folder == DialogResult.Cancel)
                {
                    return;
                }
                deleteFolder = folder == DialogResult.Yes;
            }

            string message;
            if (InstalledProgramService.ForceRemove(program, deleteFolder, out message))
            {
                _actionMessage = message;
                _status.Text = message;
                _status.ForeColor = Palette.Success;
                _loaded = false;
                ScanPrograms();
            }
            else
            {
                _status.Text = message;
                _status.ForeColor = Palette.Danger;
            }
        }

        private void SetProgramsBusy(bool busy)
        {
            _busyAction = busy;
            if (_search != null)
            {
                _search.Enabled = !busy;
            }
            if (_grid != null)
            {
                _grid.Enabled = !busy;
            }
            UpdateButtons();
        }

        private void OpenLocation()
        {
            InstalledProgram program = SelectedProgram();
            if (program == null)
            {
                return;
            }
            string error;
            if (!InstalledProgramService.OpenInstallLocation(program, out error))
            {
                _status.Text = error;
                _status.ForeColor = Palette.Warning;
            }
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class UsageBar : Control
    {
        public double Percent;
        public Color FillColor = Palette.Accent;
        public Color TrackColor = Palette.SoftBlue;

        public UsageBar()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Height = 8;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int barHeight = Math.Max(4, Ui.Scale(e.Graphics, Math.Min(10, Height)));
            int top = (Height - barHeight) / 2;
            int radius = barHeight / 2;
            Rectangle track = new Rectangle(0, top, Math.Max(1, Width - 1), barHeight);
            using (GraphicsPath trackPath = Ui.RoundedPath(track, radius))
            using (SolidBrush trackBrush = new SolidBrush(TrackColor))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
            }
            double percent = Math.Max(0, Math.Min(100, Percent));
            if (percent <= 0)
            {
                return;
            }
            int fillWidth = (int)Math.Round(track.Width * percent / 100.0);
            if (fillWidth < barHeight)
            {
                fillWidth = barHeight;
            }
            Rectangle fill = new Rectangle(track.Left, track.Top, fillWidth, barHeight);
            using (GraphicsPath fillPath = Ui.RoundedPath(fill, radius))
            using (SolidBrush fillBrush = new SolidBrush(FillColor))
            {
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }
    }

    internal sealed class SpacePage : UserControl
    {
        private const int MaxCards = 4;

        private readonly TableLayoutPanel _driveCards;
        private readonly RoundedPanel[] _cards = new RoundedPanel[MaxCards];
        private readonly Label[] _driveNames = new Label[MaxCards];
        private readonly Label[] _driveValues = new Label[MaxCards];
        private readonly UsageBar[] _driveBars = new UsageBar[MaxCards];
        private readonly ComboBox _driveCombo;
        private readonly ComboBox _sizeCombo;
        private readonly DataGridView _grid;
        private readonly Label _status;
        private readonly Button _scanButton;
        private readonly Button _stopButton;
        private readonly Button _openButton;
        private volatile bool _cancel;
        private bool _scanning;
        private int _lastDriveCount;

        public SpacePage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "空间分析",
                    "查看磁盘占用，扫描大文件；只读操作，不会删除任何内容",
                    null,
                    null),
                0,
                0);

            _driveCards = new TableLayoutPanel();
            _driveCards.Dock = DockStyle.Fill;
            _driveCards.Margin = new Padding(0, 0, 0, 10);
            _driveCards.ColumnCount = MaxCards;
            _driveCards.RowCount = 1;
            _driveCards.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            for (int i = 0; i < MaxCards; i++)
            {
                _driveCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
                RoundedPanel card = new RoundedPanel();
                card.Dock = DockStyle.Fill;
                card.Margin = new Padding(i == 0 ? 0 : 5, 0, i == MaxCards - 1 ? 0 : 5, 0);
                card.Padding = new Padding(14, 10, 14, 10);

                Label nameLabel = Ui.Label(
                    "磁盘",
                    Palette.Small,
                    Palette.Muted,
                    ContentAlignment.MiddleLeft);
                nameLabel.AutoSize = true;
                nameLabel.Location = new Point(14, 10);
                card.Controls.Add(nameLabel);

                Label valueLabel = Ui.Label(
                    "读取中",
                    Palette.BodyBold,
                    Palette.Ink,
                    ContentAlignment.MiddleLeft);
                valueLabel.AutoSize = true;
                valueLabel.Location = new Point(14, 32);
                card.Controls.Add(valueLabel);

                UsageBar bar = new UsageBar();
                bar.Location = new Point(14, 62);
                bar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                bar.Width = 140;
                card.Controls.Add(bar);
                card.Resize += delegate
                {
                    bar.Width = Math.Max(20, card.ClientSize.Width - 28);
                };

                _cards[i] = card;
                _driveNames[i] = nameLabel;
                _driveValues[i] = valueLabel;
                _driveBars[i] = bar;
                _driveCards.Controls.Add(card, i, 0);
            }
            root.Controls.Add(_driveCards, 0, 1);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.Margin = new Padding(0, 0, 0, 10);

            Label driveLabel = Ui.Label(
                "扫描位置",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            driveLabel.AutoSize = true;
            driveLabel.Location = new Point(0, 15);
            toolbar.Controls.Add(driveLabel);

            _driveCombo = CreateToolbarCombo();
            _driveCombo.Location = new Point(68, 10);
            _driveCombo.Width = 190;
            toolbar.Controls.Add(_driveCombo);

            Label sizeLabel = Ui.Label(
                "大于",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            sizeLabel.AutoSize = true;
            sizeLabel.Location = new Point(276, 15);
            toolbar.Controls.Add(sizeLabel);

            _sizeCombo = CreateToolbarCombo();
            _sizeCombo.Location = new Point(314, 10);
            _sizeCombo.Width = 120;
            _sizeCombo.Items.AddRange(new object[]
            {
                "100 MB",
                "500 MB",
                "1 GB",
                "5 GB"
            });
            _sizeCombo.SelectedIndex = 2;
            toolbar.Controls.Add(_sizeCombo);

            _scanButton = Ui.CreateButton("开始扫描", true);
            _scanButton.Width = 108;
            _scanButton.Location = new Point(450, 10);
            _scanButton.Click += delegate { StartScan(); };
            toolbar.Controls.Add(_scanButton);

            Label hint = Ui.Label(
                "跳过 Windows、回收站、系统还原等系统目录，不跟随符号链接",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            hint.AutoSize = true;
            hint.Location = new Point(574, 15);
            toolbar.Controls.Add(hint);
            root.Controls.Add(toolbar, 0, 2);

            RoundedPanel gridCard = new RoundedPanel();
            gridCard.Dock = DockStyle.Fill;
            gridCard.Margin = new Padding(0, 0, 0, 12);
            gridCard.Padding = new Padding(12);
            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            Ui.StyleGrid(_grid);
            _grid.ReadOnly = true;
            _grid.CellDoubleClick += delegate { OpenSelectedFile(); };

            DataGridViewTextBoxColumn file = new DataGridViewTextBoxColumn();
            file.Name = "File";
            file.HeaderText = "文件";
            file.FillWeight = 48F;
            file.MinimumWidth = 260;

            DataGridViewTextBoxColumn size = new DataGridViewTextBoxColumn();
            size.Name = "Size";
            size.HeaderText = "大小";
            size.FillWeight = 12F;
            size.MinimumWidth = 80;

            DataGridViewTextBoxColumn modified = new DataGridViewTextBoxColumn();
            modified.Name = "Modified";
            modified.HeaderText = "修改时间";
            modified.FillWeight = 16F;
            modified.MinimumWidth = 120;

            DataGridViewTextBoxColumn folder = new DataGridViewTextBoxColumn();
            folder.Name = "Folder";
            folder.HeaderText = "所在目录";
            folder.FillWeight = 24F;
            folder.MinimumWidth = 180;

            _grid.Columns.Add(file);
            _grid.Columns.Add(size);
            _grid.Columns.Add(modified);
            _grid.Columns.Add(folder);
            gridCard.Controls.Add(_grid);
            root.Controls.Add(gridCard, 0, 3);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            root.Controls.Add(actions, 0, 4);

            _status = Ui.Label(
                "选择磁盘后开始扫描大文件。",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _status.AutoSize = true;
            _status.Location = new Point(2, 20);
            actions.Controls.Add(_status);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.Width = 300;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _openButton = Ui.CreateButton("打开所在位置", false);
            _openButton.Width = 124;
            _openButton.Enabled = false;
            _openButton.Click += delegate { OpenSelectedFile(); };
            _stopButton = Ui.CreateButton("停止扫描", false);
            _stopButton.Width = 96;
            _stopButton.Enabled = false;
            _stopButton.Click += delegate { _cancel = true; };
            buttons.Controls.Add(_openButton);
            buttons.Controls.Add(_stopButton);
            actions.Controls.Add(buttons);
            actions.Resize += delegate
            {
                buttons.Left = actions.ClientSize.Width - buttons.Width;
            };
            _grid.SelectionChanged += delegate
            {
                _openButton.Enabled = _grid.SelectedRows.Count > 0;
            };

            Load += delegate { RefreshDrives(); };
        }

        public void RefreshDrives()
        {
            List<DriveUsage> drives = DiskSpaceService.ScanDrives();
            string previous = _driveCombo.SelectedItem == null
                ? null
                : Convert.ToString(_driveCombo.SelectedItem);
            _driveCombo.Items.Clear();
            for (int i = 0; i < drives.Count && i < MaxCards; i++)
            {
                DriveUsage drive = drives[i];
                _cards[i].Visible = true;
                _driveNames[i].Text = drive.Name + " · " + drive.Label;
                _driveValues[i].Text = String.Format(
                    "{0} 可用 / {1}",
                    Format.Bytes(drive.FreeBytes),
                    Format.Bytes(drive.TotalBytes));
                _driveBars[i].Percent = drive.UsedPercent;
                _driveBars[i].FillColor = drive.UsedPercent >= 90
                    ? Palette.Danger
                    : drive.UsedPercent >= 75
                        ? Palette.Warning
                        : Palette.Accent;
                _driveBars[i].Invalidate();
            }
            for (int i = drives.Count; i < MaxCards; i++)
            {
                _cards[i].Visible = false;
            }
            _lastDriveCount = drives.Count;
            foreach (DriveUsage drive in drives)
            {
                _driveCombo.Items.Add(drive.Name);
            }
            if (_driveCombo.Items.Count > 0)
            {
                int index = previous == null ? 0 : _driveCombo.Items.IndexOf(previous);
                _driveCombo.SelectedIndex = index >= 0 ? index : 0;
            }
        }

        private static ComboBox CreateToolbarCombo()
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.FlatStyle = FlatStyle.Flat;
            combo.Font = Palette.Body;
            combo.BackColor = Palette.Card;
            combo.ForeColor = Palette.Ink;
            combo.Height = 30;
            combo.HandleCreated += delegate
            {
                NativeSystem.ApplyControlTheme(combo.Handle, Palette.IsDark, true);
            };
            return combo;
        }

        private long MinimumBytes()
        {
            switch (_sizeCombo.SelectedIndex)
            {
                case 0:
                    return 100L * 1024L * 1024L;
                case 1:
                    return 500L * 1024L * 1024L;
                case 3:
                    return 5L * 1024L * 1024L * 1024L;
                default:
                    return 1024L * 1024L * 1024L;
            }
        }

        private void StartScan()
        {
            if (_scanning || _driveCombo.SelectedItem == null)
            {
                if (_driveCombo.SelectedItem == null)
                {
                    _status.Text = "没有可扫描的磁盘。";
                    _status.ForeColor = Palette.Warning;
                }
                return;
            }
            string drive = Convert.ToString(_driveCombo.SelectedItem);
            string root = drive.EndsWith("\\", StringComparison.Ordinal)
                ? drive
                : drive + "\\";
            long minimum = MinimumBytes();
            _cancel = false;
            _scanning = true;
            _scanButton.Enabled = false;
            _stopButton.Enabled = true;
            _openButton.Enabled = false;
            _grid.Rows.Clear();
            _status.ForeColor = Palette.Muted;
            _status.Text = "正在扫描 " + root + " …";

            ThreadPool.QueueUserWorkItem(delegate
            {
                LargeFileScanResult result = DiskSpaceService.ScanLargeFiles(
                    root,
                    minimum,
                    200,
                    delegate(string text)
                    {
                        SafeBeginInvoke(delegate { _status.Text = text; });
                    },
                    delegate { return _cancel; });
                SafeBeginInvoke(delegate { ApplyScanResult(result, minimum); });
            });
        }

        private void ApplyScanResult(LargeFileScanResult result, long minimum)
        {
            _scanning = false;
            _scanButton.Enabled = true;
            _stopButton.Enabled = false;
            _grid.Rows.Clear();
            foreach (LargeFileInfo file in result.Files)
            {
                int index = _grid.Rows.Add();
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = file;
                row.Cells["File"].Value = System.IO.Path.GetFileName(file.Path);
                row.Cells["Size"].Value = Format.Bytes(file.SizeBytes);
                row.Cells["Modified"].Value = file.Modified.ToString("yyyy-MM-dd HH:mm");
                row.Cells["Folder"].Value = file.Directory;
                row.Cells["File"].ToolTipText = file.Path;
                row.Cells["Folder"].ToolTipText = file.Directory;
            }
            if (!String.IsNullOrWhiteSpace(result.Error))
            {
                _status.Text = "扫描失败：" + result.Error;
                _status.ForeColor = Palette.Danger;
                return;
            }
            if (result.Cancelled)
            {
                _status.Text = String.Format(
                    "已停止扫描，共检查 {0} 个文件，找到 {1} 个大于 {2} 的文件。",
                    result.ScannedFiles,
                    result.Files.Count,
                    Format.Bytes(minimum));
                _status.ForeColor = Palette.Warning;
                return;
            }
            _status.Text = String.Format(
                "扫描完成：检查 {0} 个文件（共 {1}），找到 {2} 个大于 {3} 的文件。",
                result.ScannedFiles,
                Format.Bytes(result.ScannedBytes),
                result.Files.Count,
                Format.Bytes(minimum));
            _status.ForeColor = Palette.Muted;
            _openButton.Enabled = _grid.SelectedRows.Count > 0;
        }

        private void OpenSelectedFile()
        {
            if (_grid.SelectedRows.Count == 0)
            {
                return;
            }
            LargeFileInfo file = _grid.SelectedRows[0].Tag as LargeFileInfo;
            if (file == null)
            {
                return;
            }
            try
            {
                Process.Start("explorer.exe", "/select,\"" + file.Path + "\"");
            }
            catch (Exception ex)
            {
                _status.Text = "无法打开位置：" + ex.Message;
                _status.ForeColor = Palette.Danger;
            }
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal sealed class SystemInfoPage : UserControl
    {
        private readonly Dictionary<string, Label> _values =
            new Dictionary<string, Label>();
        private readonly ToolTip _tips = new ToolTip();
        private readonly Label _status;
        private bool _loading;
        private DateTime _lastRefresh = DateTime.MinValue;

        public SystemInfoPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            Controls.Add(root);

            root.Controls.Add(
                Ui.BuildPageHeader(
                    "系统信息",
                    "处理器、显卡、内存、磁盘和系统版本的一屏概览",
                    "重新读取",
                    delegate { RefreshInfo(true); }),
                0,
                0);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0, 0, 0, 8);
            content.ColumnCount = 2;
            content.RowCount = 3;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 250F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 176F));

            RoundedPanel devices = BuildInfoCard(
                "设备与系统",
                new string[] { "计算机名", "设备型号", "操作系统", "系统版本", "安装日期", "上次开机" });
            devices.Margin = new Padding(0, 0, 6, 10);
            content.Controls.Add(devices, 0, 0);

            RoundedPanel runtime = BuildInfoCard(
                "运行状态",
                new string[] { "已运行时长", "当前用户", "系统架构", "数据目录" });
            runtime.Margin = new Padding(6, 0, 0, 10);
            content.Controls.Add(runtime, 1, 0);

            RoundedPanel cpu = BuildInfoCard(
                "处理器",
                new string[] { "处理器型号", "核心 / 线程", "最高主频" });
            cpu.Margin = new Padding(0, 0, 6, 10);
            content.Controls.Add(cpu, 0, 1);

            RoundedPanel gpu = BuildInfoCard(
                "显卡",
                new string[] { "显卡型号", "显存" });
            gpu.Margin = new Padding(6, 0, 0, 10);
            content.Controls.Add(gpu, 1, 1);

            RoundedPanel memory = BuildInfoCard(
                "内存",
                new string[] { "总容量", "内存条", "当前占用" });
            memory.Margin = new Padding(0, 0, 6, 0);
            content.Controls.Add(memory, 0, 2);

            RoundedPanel disks = BuildInfoCard(
                "磁盘",
                new string[] { "磁盘数量", "磁盘列表", "系统盘可用" });
            disks.Margin = new Padding(6, 0, 0, 0);
            content.Controls.Add(disks, 1, 2);
            root.Controls.Add(content, 0, 1);

            _status = Ui.Label(
                "正在读取系统信息…",
                Palette.Small,
                Palette.Muted,
                ContentAlignment.MiddleLeft);
            _status.Dock = DockStyle.Fill;
            root.Controls.Add(_status, 0, 2);

            Load += delegate { RefreshInfo(false); };
        }

        public void RefreshInfo()
        {
            RefreshInfo(false);
        }

        public void RefreshInfo(bool force)
        {
            if (_loading)
            {
                return;
            }
            if (!force && _lastRefresh != DateTime.MinValue &&
                DateTime.Now - _lastRefresh < TimeSpan.FromSeconds(90))
            {
                return;
            }
            _loading = true;
            _status.Text = "正在读取系统信息…";
            _status.ForeColor = Palette.Muted;
            ThreadPool.QueueUserWorkItem(delegate
            {
                SystemInfoSnapshot snapshot = SystemInfoService.Read();
                SafeBeginInvoke(delegate { ApplySnapshot(snapshot); });
            });
        }

        private void ApplySnapshot(SystemInfoSnapshot snapshot)
        {
            _loading = false;
            _lastRefresh = DateTime.Now;

            SetValue("计算机名", snapshot.ComputerName);
            SetValue(
                "设备型号",
                JoinParts(snapshot.Manufacturer, snapshot.Model));
            SetValue("操作系统", snapshot.OsCaption);
            SetValue(
                "系统版本",
                JoinParts(
                    snapshot.OsVersion,
                    String.IsNullOrWhiteSpace(snapshot.OsBuild)
                        ? null
                        : "Build " + snapshot.OsBuild,
                    snapshot.Architecture));
            SetValue(
                "安装日期",
                snapshot.InstallDate.HasValue
                    ? snapshot.InstallDate.Value.ToString("yyyy-MM-dd")
                    : "未知");
            SetValue(
                "上次开机",
                snapshot.LastBoot.HasValue
                    ? snapshot.LastBoot.Value.ToString("yyyy-MM-dd HH:mm")
                    : "未知");

            TimeSpan uptime = snapshot.Uptime;
            SetValue("已运行时长", FormatUptime(uptime));
            SetValue("当前用户", snapshot.UserName);
            SetValue("系统架构", snapshot.Architecture);
            SetValue("数据目录", AppPaths.DataDirectory);

            SetValue("处理器型号", snapshot.CpuName);
            SetValue(
                "核心 / 线程",
                snapshot.CpuCores > 0
                    ? snapshot.CpuCores + " 核 / " + snapshot.CpuThreads + " 线程"
                    : "未知");
            SetValue(
                "最高主频",
                snapshot.CpuClockMhz > 0
                    ? (snapshot.CpuClockMhz / 1000.0).ToString("0.00") + " GHz"
                    : "未知");

            SetValue("显卡型号", snapshot.GpuName);
            SetValue("显存", snapshot.GpuMemoryBytes > 0
                ? Format.Bytes(snapshot.GpuMemoryBytes)
                : "未读取到");

            SetValue("总容量", Format.Bytes(snapshot.TotalMemoryBytes));
            string moduleText = "未读取到";
            if (snapshot.MemoryModules.Count > 0)
            {
                MemoryModuleInfo module = snapshot.MemoryModules[0];
                StringBuilder modules = new StringBuilder();
                if (snapshot.MemoryModules.Count > 1)
                {
                    modules.Append(snapshot.MemoryModules.Count).Append(" 条 · ");
                }
                modules.Append(Format.Bytes(module.CapacityBytes));
                if (!String.IsNullOrWhiteSpace(module.Speed))
                {
                    modules.Append(" · ").Append(module.Speed).Append(" MT/s");
                }
                if (!String.IsNullOrWhiteSpace(module.Manufacturer))
                {
                    modules.Append(" · ").Append(module.Manufacturer);
                }
                moduleText = modules.ToString();
            }
            SetValue("内存条", moduleText);
            MemoryMetrics memory = NativeSystem.GetMemory();
            SetValue(
                "当前占用",
                String.Format(
                    "{0} 可用 / {1}",
                    Format.Bytes(memory.AvailablePhysical),
                    Format.Bytes(memory.TotalPhysical)));

            SetValue("磁盘数量", snapshot.Disks.Count + " 块");
            string diskListText = "未读取到";
            if (snapshot.Disks.Count > 0)
            {
                DiskDeviceInfo disk = snapshot.Disks[0];
                StringBuilder diskText = new StringBuilder();
                if (snapshot.Disks.Count > 1)
                {
                    diskText.Append(snapshot.Disks.Count).Append(" 块 · ");
                }
                diskText.Append(String.IsNullOrWhiteSpace(disk.Model) ? "磁盘" : disk.Model);
                if (disk.SizeBytes > 0)
                {
                    diskText.Append(" · ").Append(Format.Bytes(disk.SizeBytes));
                }
                if (!String.IsNullOrWhiteSpace(disk.Status))
                {
                    diskText.Append(" · ").Append(disk.Status);
                }
                diskListText = diskText.ToString();
            }
            SetValue("磁盘列表", diskListText);

            string systemDrive = null;
            try
            {
                systemDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
            }
            catch
            {
            }
            string systemDriveText = "未知";
            foreach (DriveUsage drive in DiskSpaceService.ScanDrives())
            {
                if (systemDrive != null &&
                    drive.Name.StartsWith(
                        systemDrive.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    systemDriveText = String.Format(
                        "{0} 剩余 {1} / {2}",
                        drive.Name,
                        Format.Bytes(drive.FreeBytes),
                        Format.Bytes(drive.TotalBytes));
                    break;
                }
            }
            SetValue("系统盘可用", systemDriveText);

            _status.Text = "信息读取于 " + DateTime.Now.ToString("HH:mm:ss") +
                "，磁盘和内存为系统报告值。";
            _status.ForeColor = Palette.Muted;
        }

        private void SetValue(string key, string value)
        {
            Label label;
            if (!_values.TryGetValue(key, out label) || label == null)
            {
                return;
            }
            label.Text = String.IsNullOrWhiteSpace(value) ? "—" : value;
            _tips.SetToolTip(label, label.Text);
        }

        private static string JoinParts(params string[] parts)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string part in parts)
            {
                if (String.IsNullOrWhiteSpace(part))
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    builder.Append(" · ");
                }
                builder.Append(part.Trim());
            }
            return builder.ToString();
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime <= TimeSpan.Zero)
            {
                return "未知";
            }
            if (uptime.TotalDays >= 1)
            {
                return String.Format(
                    "{0} 天 {1} 小时 {2} 分",
                    (int)uptime.TotalDays,
                    uptime.Hours,
                    uptime.Minutes);
            }
            return String.Format("{0} 小时 {1} 分", uptime.Hours, uptime.Minutes);
        }

        private RoundedPanel BuildInfoCard(string title, string[] keys)
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(18, 12, 18, 8);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = keys.Length + 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            float rowPercent = keys.Length > 0 ? 100F / keys.Length : 100F;
            for (int i = 0; i < keys.Length; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, rowPercent));
            }

            Label titleLabel = Ui.Label(
                title,
                Palette.SectionTitle,
                Palette.Ink,
                ContentAlignment.MiddleLeft);
            titleLabel.Dock = DockStyle.Fill;
            layout.Controls.Add(titleLabel, 0, 0);
            layout.SetColumnSpan(titleLabel, 2);

            for (int i = 0; i < keys.Length; i++)
            {
                Label keyLabel = Ui.Label(
                    keys[i],
                    Palette.Small,
                    Palette.Muted,
                    ContentAlignment.MiddleLeft);
                keyLabel.Dock = DockStyle.Fill;
                keyLabel.AutoSize = false;
                Label valueLabel = Ui.Label(
                    "读取中…",
                    Palette.Body,
                    Palette.Ink,
                    ContentAlignment.MiddleLeft);
                valueLabel.Dock = DockStyle.Fill;
                valueLabel.AutoSize = false;
                layout.Controls.Add(keyLabel, 0, i + 1);
                layout.Controls.Add(valueLabel, 1, i + 1);
                _values[keys[i]] = valueLabel;
            }
            card.Controls.Add(layout);
            return card;
        }

        private void SafeBeginInvoke(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                {
                    BeginInvoke(action);
                }
            }
            catch
            {
            }
        }
    }

    internal static class SelfTest
    {
        public static bool Run(string reportPath)
        {
            if (String.IsNullOrWhiteSpace(reportPath))
            {
                reportPath = Path.Combine(Path.GetTempPath(), "PcManagerLite-self-test.txt");
            }
            try
            {
                StringBuilder report = new StringBuilder();
                report.AppendLine("PcManagerLite self-test");
                report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                report.AppendLine("64-bit process: " + Environment.Is64BitProcess);
                report.AppendLine(
                    "Page files: " +
                    String.Join(" | ", SystemOptimizer.GetPageFileEntries()));
                report.AppendLine(
                    "Active page files: " +
                    String.Join(" | ", SystemOptimizer.GetActivePageFileEntries()));
                report.AppendLine("Page file display: " + SystemOptimizer.GetPageFileDisplay());
                report.AppendLine("Power plan: " + SystemOptimizer.GetPowerPlanName());
                GameSnapshot games = GameDetector.Snapshot();
                report.AppendLine(
                    "Games: installedSteam=" + games.InstalledSteamGames +
                    ", running=" + games.RunningGames.Count +
                    ", summary=" + games.Summary);
                foreach (GameProcessInfo runningGame in games.RunningGames)
                {
                    report.AppendLine(
                        "  Game process: " + runningGame.DisplayName +
                        " [" + runningGame.ProcessName + ":" + runningGame.ProcessId + "]");
                }
                string presentMon = FrameCaptureService.FindPresentMon();
                report.AppendLine(
                    "PresentMon: " +
                    (String.IsNullOrWhiteSpace(presentMon) ? "not found" : presentMon));

                NetworkReport network = NetworkDiagnostics.ReadAdapter();
                report.AppendLine(
                    "Network adapter: " + network.AdapterName + " / " + network.Summary);

                string deleteDetails;
                bool safeDelete = CleanupScanner.RunSafeDeleteSelfTest(out deleteDetails);
                report.AppendLine("Safe delete test: " + safeDelete + " / " + deleteDetails);

                string frameDetails;
                bool frameParser = FrameCaptureService.RunParserSelfTest(out frameDetails);
                report.AppendLine("Frame CSV parser test: " + frameParser + " / " + frameDetails);

                CleanupScanResult cleanup = CleanupScanner.ScanAll(null);
                report.AppendLine(
                    "Cleanup scan: files=" + cleanup.TotalFiles +
                    ", bytes=" + cleanup.TotalBytes +
                    ", categories=" + cleanup.Categories.Count +
                    ", error=" + (cleanup.Error ?? "none"));
                foreach (CleanupCategory category in cleanup.Categories)
                {
                    report.AppendLine(
                        String.Format(
                            "  {0}: files={1}, bytes={2}, selected={3}, errors={4}",
                            category.Title,
                            category.FileCount,
                            category.SizeBytes,
                            category.DefaultSelected,
                            category.AccessErrors));
                }

                NetworkReport pingReport = NetworkDiagnostics.Test(null);
                report.AppendLine(
                    "Network test: " +
                    pingReport.Summary +
                    ", latency=" + pingReport.InternetLatencyMs.ToString("0") +
                    ", loss=" + pingReport.PacketLossPercent.ToString("0.#") +
                    ", dns=" + pingReport.DnsResolved);

                List<InstalledProgram> installedPrograms = InstalledProgramService.Scan();
                int uninstallablePrograms = 0;
                foreach (InstalledProgram program in installedPrograms)
                {
                    if (program.CanUninstall)
                    {
                        uninstallablePrograms++;
                    }
                }
                report.AppendLine(
                    "Installed programs: total=" + installedPrograms.Count +
                    ", uninstallable=" + uninstallablePrograms);

                List<DriveUsage> drives = DiskSpaceService.ScanDrives();
                report.AppendLine("Drives: " + drives.Count);
                foreach (DriveUsage drive in drives)
                {
                    report.AppendLine(
                        String.Format(
                            "  {0} {1}: used={2:0.0}% free={3}",
                            drive.Name,
                            drive.Label,
                            drive.UsedPercent,
                            Format.Bytes(drive.FreeBytes)));
                }

                SystemInfoSnapshot systemInfo = SystemInfoService.Read();
                report.AppendLine(
                    "System info: cpu=" + systemInfo.CpuName +
                    ", cores=" + systemInfo.CpuCores +
                    ", gpu=" + systemInfo.GpuName +
                    ", memoryModules=" + systemInfo.MemoryModules.Count +
                    ", disks=" + systemInfo.Disks.Count +
                    ", os=" + systemInfo.OsCaption);
                report.AppendLine(
                    "Scheduled cleanup task present: " +
                    ScheduledCleanupService.IsEnabled());

                DefenderStatus defender = DefenderService.ReadStatus();
                report.AppendLine(
                    "Defender: available=" + defender.Available +
                    ", realtime=" + defender.RealTimeProtectionEnabled +
                    ", signature=" + defender.SignatureVersion +
                    ", engine=" + (DefenderService.FindMpCmdRun() ?? "not found") +
                    ", error=" + (defender.Error ?? "none"));

                BackupStore backup = BackupStore.Load();
                report.AppendLine("Restorable system backup present: " + backup.HasSettings);

                List<StartupItem> startupItems = StartupManagerService.Scan();
                int disableableStartup = 0;
                foreach (StartupItem item in startupItems)
                {
                    if (item.CanDisable)
                    {
                        disableableStartup++;
                    }
                }
                report.AppendLine(
                    "Startup items: total=" + startupItems.Count +
                    ", disableable=" + disableableStartup +
                    ", disabledBackups=" + StartupManagerService.DisabledBackupCount());
                foreach (StartupItem item in startupItems)
                {
                    report.AppendLine(
                        "  " + item.SourceLabel + " | " + item.Name +
                        " | " + item.RiskLabel +
                        " | " + item.Command);
                }

                string runKeyPath =
                    @"Software\Microsoft\Windows\CurrentVersion\Run";
                object originalAutostart = null;
                bool autostartOn = false;
                bool autostartOff = false;
                try
                {
                    using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, false))
                    {
                        if (runKey != null)
                        {
                            originalAutostart = runKey.GetValue("PcManagerLite", null);
                        }
                    }
                    AppStartupService.SetEnabled(true);
                    autostartOn = AppStartupService.IsEnabled();
                    AppStartupService.SetEnabled(false);
                    autostartOff = !AppStartupService.IsEnabled();
                }
                finally
                {
                    using (RegistryKey runKey = Registry.CurrentUser.CreateSubKey(runKeyPath))
                    {
                        if (originalAutostart == null)
                        {
                            runKey.DeleteValue("PcManagerLite", false);
                        }
                        else
                        {
                            runKey.SetValue(
                                "PcManagerLite",
                                originalAutostart,
                                RegistryValueKind.String);
                        }
                    }
                }
                report.AppendLine(
                    "Self-start toggle test: on=" + autostartOn +
                    ", off=" + autostartOff);

                List<ProcessGroup> groups = ProcessScanner.ScanGroups();
                report.AppendLine("Candidate process groups: " + groups.Count);
                foreach (ProcessGroup group in groups)
                {
                    report.AppendLine(
                        String.Format(
                            "  {0}: count={1}, memory={2}, risk={3}",
                            group.DisplayName,
                            group.ProcessIds.Count,
                            group.WorkingSetBytes,
                            group.Risk));
                }
                string[] protectedNames = { "steam", "steamservice", "steamwebhelper", "explorer" };
                foreach (string protectedName in protectedNames)
                {
                    bool found = false;
                    foreach (ProcessGroup group in groups)
                    {
                        if (String.Equals(
                            group.ProcessName,
                            protectedName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    report.AppendLine(
                        "Protected excluded " + protectedName + ": " + (!found));
                }
                report.AppendLine("Immediate action model: enabled (no secondary confirmation)");

                string settingsPath = Path.Combine(AppPaths.DataDirectory, "settings.ini");
                bool settingsExisted = File.Exists(settingsPath);
                string originalSettings = settingsExisted
                    ? File.ReadAllText(settingsPath, Encoding.UTF8)
                    : null;
                ManagerSettings themeSettings = ManagerSettings.Load();
                string originalTheme = themeSettings.Theme;
                themeSettings.CheckUpdatesOnStart = false;
                bool themeSwitchPassed = false;
                try
                {
                    using (MainForm form = new MainForm(themeSettings, false))
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        form.Show();
                        Application.DoEvents();

                        form.SwitchTheme("dark");
                        Application.DoEvents();
                        bool darkPassed = Palette.IsDark && HasVisibleSidebar(form);
                        form.SwitchTheme("light");
                        Application.DoEvents();
                        bool lightPassed = !Palette.IsDark && HasVisibleSidebar(form);
                        form.SwitchTheme(originalTheme);
                        Application.DoEvents();
                        themeSwitchPassed =
                            darkPassed &&
                            lightPassed &&
                            HasVisibleSidebar(form);
                        report.AppendLine("Immediate theme switch test: " + themeSwitchPassed);

                        report.AppendLine(
                            "UI working set before trim MB: " +
                            (Process.GetCurrentProcess().WorkingSet64 / 1048576.0).ToString("0.0"));
                        NativeSystem.TrimCurrentProcessWorkingSet();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        NativeSystem.TrimCurrentProcessWorkingSet();
                        report.AppendLine(
                            "UI working set after trim MB: " +
                            (Process.GetCurrentProcess().WorkingSet64 / 1048576.0).ToString("0.0"));
                        report.AppendLine(
                            "UI private bytes MB: " +
                            (Process.GetCurrentProcess().PrivateMemorySize64 / 1048576.0)
                                .ToString("0.0"));
                        form.Close();
                    }
                }
                finally
                {
                    if (settingsExisted)
                    {
                        File.WriteAllText(settingsPath, originalSettings, Encoding.UTF8);
                    }
                    else if (File.Exists(settingsPath))
                    {
                        File.Delete(settingsPath);
                    }
                }

                string directory = Path.GetDirectoryName(reportPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return safeDelete &&
                    frameParser &&
                    autostartOn &&
                    autostartOff &&
                    themeSwitchPassed &&
                    games.RunningGames != null;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(reportPath, ex.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
                return false;
            }
        }

        public static bool SaveScreenshot(string path, string page, string theme)
        {
            try
            {
                ManagerSettings screenshotSettings = ManagerSettings.Load();
                if (!String.IsNullOrWhiteSpace(theme))
                {
                    screenshotSettings.Theme = theme;
                }
                string[] screenshotArguments = Environment.GetCommandLineArgs();
                for (int i = 0; i < screenshotArguments.Length - 1; i++)
                {
                    if (String.Equals(
                        screenshotArguments[i],
                        "--accent",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        screenshotSettings.Accent = Palette.NormalizeAccent(
                            screenshotArguments[i + 1]);
                        Palette.ApplyAccent(screenshotSettings.Accent);
                    }
                }
                using (MainForm form = new MainForm(screenshotSettings, false))
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.Show();
                    Application.DoEvents();
                    form.NavigateForTest(page);
                    Application.DoEvents();
                    bool settingsPage = String.Equals(
                        page,
                        "settings",
                        StringComparison.OrdinalIgnoreCase);
                    bool cleanupPage = String.Equals(
                        page,
                        "cleanup",
                        StringComparison.OrdinalIgnoreCase);
                    DateTime deadline = DateTime.UtcNow.AddSeconds(
                        settingsPage ? 4 : cleanupPage ? 8 : 2.5);
                    while (DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(50);
                    }
                    form.PerformLayout();
                    Application.DoEvents();

                    Bitmap bitmap = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    string directory = Path.GetDirectoryName(path);
                    if (!String.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    bitmap.Dispose();
                    form.Close();
                }
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(path + ".error.txt", ex.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
                return false;
            }
        }

        private static bool HasVisibleSidebar(Form form)
        {
            foreach (Control control in form.Controls)
            {
                if (control is Panel &&
                    control.Dock == DockStyle.Left &&
                    control.Visible &&
                    control.Width >= 180)
                {
                    return true;
                }
            }
            return false;
        }

    }
}
