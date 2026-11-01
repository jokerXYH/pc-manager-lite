using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("电脑管家 Lite")]
[assembly: System.Reflection.AssemblyDescription("轻量的清理、游戏加速与网络修复工具")]
[assembly: System.Reflection.AssemblyProduct("电脑管家 Lite")]
[assembly: System.Reflection.AssemblyCompany("Local")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]

namespace PcManagerLite
{
    internal static class PcManagerProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (HasArgument(args, "--self-test"))
            {
                string report = GetArgumentValue(args, "--report");
                Environment.ExitCode = SelfTest.Run(report) ? 0 : 1;
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

            string screenshot = GetArgumentValue(args, "--screenshot");
            if (screenshot != null)
            {
                string page = GetArgumentValue(args, "--page");
                Environment.ExitCode = SelfTest.SaveScreenshot(screenshot, page) ? 0 : 1;
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

                Application.Run(new MainForm());
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
    }

    internal static class Palette
    {
        public static readonly Color Canvas = Color.FromArgb(244, 248, 252);
        public static readonly Color Card = Color.White;
        public static readonly Color Sidebar = Color.FromArgb(12, 39, 68);
        public static readonly Color SidebarHover = Color.FromArgb(24, 61, 98);
        public static readonly Color SidebarMuted = Color.FromArgb(159, 188, 218);
        public static readonly Color Accent = Color.FromArgb(21, 132, 255);
        public static readonly Color AccentDark = Color.FromArgb(10, 105, 226);
        public static readonly Color AccentHover = Color.FromArgb(8, 115, 232);
        public static readonly Color Aqua = Color.FromArgb(27, 198, 183);
        public static readonly Color Ink = Color.FromArgb(25, 37, 54);
        public static readonly Color Muted = Color.FromArgb(111, 126, 145);
        public static readonly Color Border = Color.FromArgb(221, 231, 242);
        public static readonly Color SoftBlue = Color.FromArgb(235, 244, 255);
        public static readonly Color Warning = Color.FromArgb(245, 166, 35);
        public static readonly Color Danger = Color.FromArgb(229, 85, 98);
        public static readonly Color Success = Color.FromArgb(29, 174, 120);
        public static readonly Color HeroStart = Color.FromArgb(18, 102, 235);
        public static readonly Color HeroEnd = Color.FromArgb(30, 190, 181);

        public static readonly Font Body = new Font("Microsoft YaHei UI", 9F);
        public static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        public static readonly Font Small = new Font("Microsoft YaHei UI", 8.25F);
        public static readonly Font PageTitle = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold);
        public static readonly Font SectionTitle = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
        public static readonly Font Metric = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold);
        public static readonly Font SidebarBrand = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
    }

    internal enum ManagerView
    {
        Dashboard,
        Cleanup,
        Game,
        Network,
        Startup,
        Recovery
    }

    internal enum NavIconKind
    {
        Home,
        Cleanup,
        Game,
        Network,
        Startup,
        Recovery
    }

    internal sealed class MainForm : Form
    {
        private readonly Panel _contentHost;
        private readonly Dictionary<ManagerView, Control> _pages =
            new Dictionary<ManagerView, Control>();
        private readonly Dictionary<ManagerView, SidebarButton> _navButtons =
            new Dictionary<ManagerView, SidebarButton>();
        private readonly DashboardPage _dashboardPage;
        private readonly CleanupPage _cleanupPage;
        private readonly GamePage _gamePage;
        private readonly NetworkPage _networkPage;
        private readonly StartupPage _startupPage;
        private readonly RecoveryPage _recoveryPage;
        private bool _quickChecking;
        private ManagerView _activeView = ManagerView.Dashboard;

        public MainForm()
        {
            Text = "电脑管家 Lite";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1200, 790);
            MinimumSize = new Size(1040, 690);
            BackColor = Palette.Canvas;
            ForeColor = Palette.Ink;
            Font = Palette.Body;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            Panel sidebar = BuildSidebar();
            _contentHost = new Panel();
            _contentHost.Dock = DockStyle.Fill;
            _contentHost.BackColor = Palette.Canvas;
            _contentHost.Padding = new Padding(22, 18, 22, 18);
            Controls.Add(_contentHost);
            Controls.Add(sidebar);

            _dashboardPage = new DashboardPage();
            _cleanupPage = new CleanupPage();
            _gamePage = new GamePage(this);
            _networkPage = new NetworkPage();
            _startupPage = new StartupPage();
            _recoveryPage = new RecoveryPage(this);

            _pages[ManagerView.Dashboard] = _dashboardPage;
            _pages[ManagerView.Cleanup] = _cleanupPage;
            _pages[ManagerView.Game] = _gamePage;
            _pages[ManagerView.Network] = _networkPage;
            _pages[ManagerView.Startup] = _startupPage;
            _pages[ManagerView.Recovery] = _recoveryPage;

            foreach (Control page in _pages.Values)
            {
                page.Dock = DockStyle.Fill;
                page.Visible = false;
                _contentHost.Controls.Add(page);
            }

            _dashboardPage.NavigateRequested += Navigate;
            _dashboardPage.QuickCheckRequested += StartQuickCheck;

            Load += delegate
            {
                Navigate(ManagerView.Dashboard);
                UpdateKnownSystemState();
            };
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    NativeSystem.TrimCurrentProcessWorkingSet();
                }
            };
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
            else
            {
                Navigate(ManagerView.Dashboard);
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
            version.Text = "轻量版 2.2";
            version.Font = Palette.Small;
            version.ForeColor = Palette.SidebarMuted;
            version.AutoSize = true;
            version.Location = new Point(70, 46);
            brand.Controls.Add(version);
            sidebar.Controls.Add(brand);

            Panel nav = new Panel();
            nav.Dock = DockStyle.Top;
            nav.Height = 402;
            nav.Padding = new Padding(10, 6, 10, 0);

            AddSidebarButton(nav, ManagerView.Dashboard, "首页体检", NavIconKind.Home);
            AddSidebarButton(nav, ManagerView.Cleanup, "垃圾清理", NavIconKind.Cleanup);
            AddSidebarButton(nav, ManagerView.Game, "游戏加速", NavIconKind.Game);
            AddSidebarButton(nav, ManagerView.Network, "网络修复", NavIconKind.Network);
            AddSidebarButton(nav, ManagerView.Startup, "启动项管理", NavIconKind.Startup);
            AddSidebarButton(nav, ManagerView.Recovery, "恢复中心", NavIconKind.Recovery);
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
            button.Height = 54;
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
            using (GraphicsPath path = Ui.RoundedPath(new Rectangle(0, 2, Width - 1, Height - 5), 9))
            using (SolidBrush brush = new SolidBrush(background))
            {
                e.Graphics.FillPath(brush, path);
            }

            Color iconColor = _active ? Color.White : Palette.SidebarMuted;
            DrawIcon(e.Graphics, new Rectangle(17, 17, 18, 18), _iconKind, iconColor);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                _active ? Palette.BodyBold : Font,
                new Rectangle(48, 0, Width - 55, Height),
                _active ? Color.White : Palette.SidebarMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (Focused && _keyboardFocus)
            {
                ControlPaint.DrawFocusRectangle(
                    e.Graphics,
                    new Rectangle(4, 5, Width - 9, Height - 11),
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
                else if (kind == NavIconKind.Startup)
                {
                    graphics.DrawArc(pen, box.Left + 2, box.Top + 2, box.Width - 4, box.Height - 4, 40, 285);
                    graphics.DrawLine(pen, box.Left + 9, box.Top + 1, box.Left + 9, box.Top + 7);
                    graphics.DrawLine(pen, box.Left + 13, box.Top + 5, box.Right - 4, box.Top + 5);
                    graphics.DrawLine(pen, box.Left + 9, box.Top + 9, box.Left + 9, box.Top + 13);
                    graphics.DrawLine(pen, box.Left + 9, box.Top + 13, box.Left + 13, box.Top + 13);
                }
                else
                {
                    graphics.DrawArc(pen, box.Left + 3, box.Top + 1, box.Width - 6, box.Height - 2, 205, 310);
                    graphics.DrawLine(pen, box.Left + 9, box.Top + 3, box.Left + 9, box.Top + 9);
                    graphics.DrawLine(pen, box.Left + 9, box.Top + 3, box.Right - 4, box.Top + 3);
                    graphics.DrawArc(pen, box.Left + 6, box.Top + 7, 7, 7, 20, 270);
                }
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
            using (GraphicsPath path = Ui.RoundedPath(
                new Rectangle(0, 0, Width - 1, Height - 1),
                Radius))
            using (SolidBrush fill = new SolidBrush(FillColor))
            {
                e.Graphics.FillPath(fill, path);
                if (BorderWidth > 0)
                {
                    using (Pen pen = new Pen(BorderColor, BorderWidth))
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
            using (GraphicsPath path = Ui.RoundedPath(
                new Rectangle(0, 0, Width - 1, Height - 1),
                16))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                ClientRectangle,
                Palette.HeroStart,
                Palette.HeroEnd,
                18F))
            {
                e.Graphics.FillPath(brush, path);
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
            int size = Math.Min(Width, Height) - 24;
            Rectangle ring = new Rectangle(
                (Width - size) / 2,
                10,
                size,
                size);

            using (Pen background = new Pen(Color.FromArgb(82, 255, 255, 255), 10F))
            {
                background.StartCap = LineCap.Round;
                background.EndCap = LineCap.Round;
                e.Graphics.DrawArc(background, ring, -90, 360);
            }

            int value = _score < 0 ? 360 : (int)(360.0 * _score / 100.0);
            using (Pen foreground = new Pen(Color.White, 10F))
            {
                foreground.StartCap = LineCap.Round;
                foreground.EndCap = LineCap.Round;
                e.Graphics.DrawArc(foreground, ring, -90, value);
            }

            string number = _score < 0 ? "—" : _score.ToString();
            using (Font numberFont = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold))
            {
                Rectangle numberArea = new Rectangle(0, ring.Top + 22, Width, 48);
                TextRenderer.DrawText(
                    e.Graphics,
                    number,
                    numberFont,
                    numberArea,
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(
                e.Graphics,
                _status,
                Palette.BodyBold,
                new Rectangle(0, ring.Top + 72, Width, 24),
                Color.White,
                TextFormatFlags.HorizontalCenter);
        }
    }

    internal static class Ui
    {
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
            button.BackColor = primary ? Palette.Accent : Color.White;
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
                        : Color.FromArgb(183, 207, 234);
                    button.ForeColor = Color.White;
                }
                else
                {
                    button.BackColor = button.Enabled ? Color.White : Color.FromArgb(246, 248, 251);
                    button.ForeColor = button.Enabled ? Palette.Ink : Palette.Muted;
                }
            };
            button.MouseEnter += delegate
            {
                if (!button.Enabled)
                {
                    return;
                }
                button.BackColor = primary ? Palette.AccentHover : Color.FromArgb(247, 251, 255);
            };
            button.MouseLeave += delegate
            {
                if (!button.Enabled)
                {
                    return;
                }
                button.BackColor = primary ? Palette.Accent : Color.White;
            };
            return button;
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
            grid.BackgroundColor = Color.White;
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
            grid.GridColor = Color.FromArgb(235, 241, 247);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Palette.Ink;
            grid.DefaultCellStyle.SelectionBackColor = Palette.SoftBlue;
            grid.DefaultCellStyle.SelectionForeColor = Palette.Ink;
            grid.DefaultCellStyle.Font = Palette.Body;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 251, 254);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Palette.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 251, 254);
            grid.ColumnHeadersDefaultCellStyle.Font = Palette.Small;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
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
        private readonly Label _scoreHint;
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
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heroLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

            Panel heroText = new Panel();
            heroText.Dock = DockStyle.Fill;
            heroText.BackColor = Color.Transparent;
            Label heroTitle = Ui.Label(
                "让游戏运行得更轻快",
                new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
                Color.White,
                ContentAlignment.MiddleLeft);
            heroTitle.AutoSize = true;
            heroTitle.Location = new Point(0, 10);
            heroText.Controls.Add(heroTitle);

            Label heroDescription = Ui.Label(
                "一次体检会检查可清理文件、网络状态、游戏后台负载和虚拟内存位置。",
                Palette.Body,
                Color.FromArgb(230, 255, 255, 255),
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
            _checkButton.BackColor = Color.White;
            _checkButton.ForeColor = Palette.AccentDark;
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

            _checkButtonLabel = Ui.Label(
                "扫描过程不会自动删除任何文件",
                Palette.Small,
                Color.FromArgb(220, 255, 255, 255),
                ContentAlignment.MiddleLeft);
            _checkButtonLabel.AutoSize = true;
            _checkButtonLabel.Location = new Point(146, 116);
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
                button.Left = card.ClientSize.Width - button.Width - 14;
                button.Top = card.ClientSize.Height - button.Height - 12;
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
            summaryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
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

            buttons.Controls.Add(_cleanButton);
            buttons.Controls.Add(_scanButton);

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

            DataGridViewTextBoxColumn files = new DataGridViewTextBoxColumn();
            files.Name = "Files";
            files.HeaderText = "数量";
            files.ReadOnly = true;
            files.FillWeight = 12F;
            files.MinimumWidth = 70;

            DataGridViewTextBoxColumn note = new DataGridViewTextBoxColumn();
            note.Name = "Note";
            note.HeaderText = "说明";
            note.ReadOnly = true;
            note.FillWeight = 38F;
            note.MinimumWidth = 260;

            _grid.Columns.Add(select);
            _grid.Columns.Add(title);
            _grid.Columns.Add(size);
            _grid.Columns.Add(files);
            _grid.Columns.Add(note);
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
        }

        private void Scan()
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
            _gameCombo.Font = Palette.Body;
            _gameCombo.Location = new Point(16, 43);
            _gameCombo.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _gameCombo.Width = 360;
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
                _gameCombo.Width = Math.Max(180, Math.Min(360, frameCard.ClientSize.Width - 510));
                _frameResult.Width = Math.Max(180, frameCard.ClientSize.Width - _frameResult.Left - 16);
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
            buttons.Width = 460;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;

            _optimizeButton = Ui.CreateButton("一键游戏加速", true);
            _optimizeButton.Width = 124;
            _optimizeButton.Click += delegate { Optimize(); };

            _scanButton = Ui.CreateButton("扫描后台", false);
            _scanButton.Width = 96;
            _scanButton.Click += delegate { Scan(); };

            _restoreButton = Ui.CreateButton("恢复系统设置", false);
            _restoreButton.Width = 112;
            _restoreButton.Click += delegate { RestoreSettings(); };

            buttons.Controls.Add(_optimizeButton);
            buttons.Controls.Add(_scanButton);
            buttons.Controls.Add(_restoreButton);
            actions.Controls.Add(buttons);
            actions.Resize += delegate
            {
                buttons.Left = actions.ClientSize.Width - buttons.Width;
            };

            Scan();
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

            Label titleLabel = Ui.Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 22;
            card.Controls.Add(titleLabel);

            Label valueLabel = Ui.Label(
                value,
                new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                accent,
                ContentAlignment.MiddleLeft);
            valueLabel.Dock = DockStyle.Fill;
            card.Controls.Add(valueLabel);
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
            _repairLog.BackColor = Color.White;
            _repairLog.ForeColor = Palette.Ink;
            _repairLog.Font = Palette.Body;
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
        private List<StartupItem> _items;
        private bool _busy;

        public StartupPage()
        {
            BackColor = Palette.Canvas;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106F));
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
            for (int i = 0; i < 3; i++)
            {
                stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            _totalValue = AddStatCard(stats, 0, "检测到的启动项", "正在扫描", Palette.Accent);
            _disableableValue = AddStatCard(stats, 1, "可安全配置", "正在扫描", Palette.Aqua);
            _selfValue = AddStatCard(stats, 2, "本工具自启动", "未注册", Palette.Success);
            root.Controls.Add(stats, 0, 1);

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
            root.Controls.Add(gridPanel, 0, 2);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Fill;
            root.Controls.Add(actions, 0, 3);

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

            Label titleLabel = Ui.Label(
                title,
                Palette.Small,
                Palette.Muted,
                ContentAlignment.TopLeft);
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 22;
            card.Controls.Add(titleLabel);

            Label valueLabel = Ui.Label(
                value,
                new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                accent,
                ContentAlignment.MiddleLeft);
            valueLabel.Dock = DockStyle.Fill;
            card.Controls.Add(valueLabel);
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
            _activity.BackColor = Color.White;
            _activity.ForeColor = Palette.Ink;
            _activity.Font = Palette.Body;
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

                using (MainForm form = new MainForm())
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.Show();
                    Application.DoEvents();
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

                string directory = Path.GetDirectoryName(reportPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
                return safeDelete && frameParser && games.RunningGames != null;
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

        public static bool SaveScreenshot(string path, string page)
        {
            try
            {
                using (MainForm form = new MainForm())
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-20000, -20000);
                    form.Show();
                    Application.DoEvents();
                    form.NavigateForTest(page);
                    Application.DoEvents();
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

    }
}
