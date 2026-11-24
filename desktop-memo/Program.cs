using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DesktopMemo
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        public static void Main()
        {
            try
            {
                RunApplication(HasStartupArgument());
            }
            catch (Exception ex)
            {
                WriteStartupCrash(ex);
                throw;
            }
        }

        private static bool HasStartupArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], StartupRegistrar.StartupArgument, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static void RunApplication(bool startCollapsed)
        {
            MemoApp.ShowWindowMessageId = unchecked((int)RegisterWindowMessage(MemoApp.ShowWindowMessageName));
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\DesktopMemo.SingleInstance.v1", out createdNew);
            if (!createdNew)
            {
                if (!startCollapsed)
                    ActivateRunningWindow();
                return;
            }

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            SystemEvents.PowerModeChanged += OnSystemPowerModeChanged;

            // Self-heal autostart on first-instance launch: recreate the
            // logon/wake scheduled task if it is missing. Run this BEFORE
            // building the UI so a policy-blocked Task Scheduler can never
            // prevent the window from appearing.
            try { StartupRegistrar.SelfHeal(); }
            catch { }

            Window window;
            try
            {
                window = (Window)XamlReader.Parse(LoadWindowXaml());
            }
            catch (Exception ex)
            {
                MessageBox.Show("界面加载失败：\n\n" + ex.Message, "桌面备忘录",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var memoApp = new MemoApp(window, startCollapsed);
            app.MainWindow = window;
            app.Run(window);
            GC.KeepAlive(memoApp);
            GC.KeepAlive(_singleInstanceMutex);
        }

        private static MemoApp _powerMemoApp;

        private static void OnSystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                _powerMemoApp = MemoApp.Instance;
                if (_powerMemoApp != null)
                {
                    _powerMemoApp.HandlePowerResume();
                }
            }
        }

        private static string LoadWindowXaml()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream("ui.xaml"))
            {
                if (stream == null)
                    throw new InvalidOperationException("找不到内嵌界面资源 ui.xaml。");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static void ActivateRunningWindow()
        {
            uint message = RegisterWindowMessage(MemoApp.ShowWindowMessageName);
            string processName = Process.GetCurrentProcess().ProcessName;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    if (process.Id == Process.GetCurrentProcess().Id)
                        continue;
                    IntPtr handle = FindAppWindow(process.Id);
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, 5);
                        if (message != 0)
                            PostMessage(handle, message, IntPtr.Zero, IntPtr.Zero);
                        SetForegroundWindow(handle);
                        return;
                    }
                }
                Thread.Sleep(120);
            }
        }

        internal static IntPtr FindAppWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint owner;
                GetWindowThreadProcessId(hWnd, out owner);
                if (owner != (uint)processId)
                    return true;
                if (GetWindowTextLength(hWnd) == 0)
                    return true;
                found = hWnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteErrorLog(e.Exception);
            MessageBox.Show("桌面备忘录遇到一个问题：\n\n" + e.Exception.Message, "桌面备忘录",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
                WriteErrorLog(ex);
        }

        internal static string ResolveDataDir()
        {
            string appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopMemo");
            if (CanUseDataDir(appData))
                return appData;

            string local = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "桌面备忘录数据");
            if (CanUseDataDir(local))
                return local;

            string temp = Path.Combine(Path.GetTempPath(), "DesktopMemo");
            Directory.CreateDirectory(temp);
            return temp;
        }

        private static bool CanUseDataDir(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteStartupCrash(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "DesktopMemo-startup.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n", Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void WriteErrorLog(Exception ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopMemo");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n", Encoding.UTF8);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);
    }

    internal enum NoteKind { Short, Long }
    internal enum LongMode { Forever, Range }

    internal static class StartupRegistrar
    {
        internal const string StartupArgument = "--startup";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopMemo";
        private const string AltValueName = "桌面备忘录";
        internal const string ShortcutName = "DesktopMemo.lnk";

        // A scheduled task is the third autostart mechanism. HKCU Run and the
        // Startup folder shortcut only fire at LOGON. This machine uses Modern
        // Standby: waking from it does NOT re-logon, so those two never run
        // after a wake and the app is left unstarted. Task Scheduler is a
        // system service that is always alive, so a task with a LogonTrigger
        // plus event triggers for "returned from low power state" covers both
        // cold boot / re-logon and Modern Standby / sleep resume.
        internal const string TaskName = "DesktopMemo";
        private const string TaskDescription =
            "Desktop Memo autostart: logon and wake from Modern Standby or sleep";

        // NOTE: the XPath must use SINGLE quotes around the provider name.
        // Doubling them (SQL-style escaping) makes the query invalid and the
        // trigger registers fine but never fires. Verified against the local
        // System event log: Power-Troubleshooter id=1 and Kernel-Power id=107.
        private const string WakeSubscription =
            "&lt;QueryList&gt;&lt;Query Id=\"0\" Path=\"System\"&gt;&lt;Select Path=\"System\"&gt;" +
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and (EventID=1)]]" +
            "&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;";
        private const string SleepSubscription =
            "&lt;QueryList&gt;&lt;Query Id=\"0\" Path=\"System\"&gt;&lt;Select Path=\"System\"&gt;" +
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=107)]]" +
            "&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;";

        internal static string ExecutablePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        internal static string LaunchCommand
        {
            get { return "\"" + ExecutablePath + "\" " + StartupArgument; }
        }

        internal static string StartupFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Start Menu\Programs\Startup");
            }
        }

        internal static string ShortcutPath
        {
            get { return Path.Combine(StartupFolder, ShortcutName); }
        }

        internal static string ReadCommand()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null)
                    return null;
                object value = key.GetValue(ValueName);
                if (value == null)
                    value = key.GetValue(AltValueName);
                return value as string;
            }
        }

        internal static bool IsEnabled()
        {
            try
            {
                string stored = ReadCommand();
                bool regOk = !string.IsNullOrEmpty(stored) &&
                    string.Equals(stored.Trim(), LaunchCommand, StringComparison.OrdinalIgnoreCase);
                bool shortcutOk = File.Exists(ShortcutPath);
                return regOk && shortcutOk;
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsShortcutEnabled()
        {
            try { return File.Exists(ShortcutPath); }
            catch { return false; }
        }

        internal static void Enable()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null)
                    throw new InvalidOperationException("无法打开注册表启动项。");
                key.SetValue(ValueName, LaunchCommand, RegistryValueKind.String);
                try { key.DeleteValue(AltValueName, false); }
                catch { }
            }
            CreateStartupShortcut();
            // Third mechanism: logon + wake scheduled task (best-effort; the
            // result is intentionally ignored so a policy that blocks Task
            // Scheduler never makes the "enable" action fail with an error).
            CreateScheduledTask();
        }

        internal static void Disable()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null)
                    return;
                try { key.DeleteValue(ValueName, false); }
                catch { }
                try { key.DeleteValue(AltValueName, false); }
                catch { }
            }
            DeleteStartupShortcut();
            DeleteScheduledTask();
        }

        internal static void CreateStartupShortcut()
        {
            DeleteStartupShortcut();
            try
            {
                Directory.CreateDirectory(StartupFolder);
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type shortcutType = shellType.InvokeMember("", BindingFlags.GetProperty,
                    null, shortcut, null).GetType();
                shellType.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, shortcut, new object[] { ExecutablePath });
                shellType.InvokeMember("Arguments", BindingFlags.SetProperty,
                    null, shortcut, new object[] { StartupArgument });
                shellType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, shortcut, new object[] { Path.GetDirectoryName(ExecutablePath) });
                shellType.InvokeMember("WindowStyle", BindingFlags.SetProperty,
                    null, shortcut, new object[] { 7 });
                shellType.InvokeMember("Description", BindingFlags.SetProperty,
                    null, shortcut, new object[] { "Desktop Memo Startup" });
                shellType.InvokeMember("Save", BindingFlags.InvokeMethod,
                    null, shortcut, null);
            }
            catch
            {
            }
        }

        internal static void DeleteStartupShortcut()
        {
            try
            {
                if (File.Exists(ShortcutPath))
                    File.Delete(ShortcutPath);
            }
            catch
            {
            }
        }

        private static string TaskXmlPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DesktopMemo", "autostart-task.xml");
            }
        }

        private static string SchTasksPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "schtasks.exe");
            }
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                        .Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static int RunHidden(string exe, string arguments, out string output)
        {
            try
            {
                var info = new ProcessStartInfo();
                info.FileName = exe;
                info.Arguments = arguments;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.CreateNoWindow = true;
                using (Process process = Process.Start(info))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit(25000);
                    output = (stdout + stderr).Trim();
                    return process.HasExited ? process.ExitCode : -999;
                }
            }
            catch (Exception ex)
            {
                output = ex.GetType().Name + ": " + ex.Message;
                return -1;
            }
        }

        private static string BuildTaskXml()
        {
            string user = XmlEscape(Environment.UserDomainName + "\\" + Environment.UserName);
            string exe = XmlEscape(ExecutablePath);
            string workDir = XmlEscape(Path.GetDirectoryName(ExecutablePath));
            return
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>" + XmlEscape(TaskDescription) + "</Description></RegistrationInfo>\r\n" +
                "  <Triggers>\r\n" +
                "    <LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger>\r\n" +
                "    <EventTrigger><Enabled>true</Enabled><Subscription>" + WakeSubscription + "</Subscription></EventTrigger>\r\n" +
                "    <EventTrigger><Enabled>true</Enabled><Subscription>" + SleepSubscription + "</Subscription></EventTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType>" +
                "<RunLevel>LeastPrivilege</RunLevel><UserId>" + user + "</UserId></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <StartWhenAvailable>true</StartWhenAvailable>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command>" +
                "<Arguments>" + StartupArgument + "</Arguments>" +
                "<WorkingDirectory>" + workDir + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
        }

        internal static bool IsTaskEnabled()
        {
            try
            {
                string output;
                int rc = RunHidden(SchTasksPath,
                    "/Query /TN \"" + TaskName + "\" /FO LIST", out output);
                return rc == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool CreateScheduledTask()
        {
            try
            {
                string xmlPath = TaskXmlPath;
                Directory.CreateDirectory(Path.GetDirectoryName(xmlPath));
                // schtasks /Create /XML requires a UTF-16 encoded file.
                File.WriteAllText(xmlPath, BuildTaskXml(), Encoding.Unicode);
                string output;
                int rc = RunHidden(SchTasksPath,
                    "/Create /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\" /F", out output);
                try { File.Delete(xmlPath); }
                catch { }
                return rc == 0;
            }
            catch
            {
                return false;
            }
        }

        internal static bool DeleteScheduledTask()
        {
            try
            {
                string output;
                int rc = RunHidden(SchTasksPath,
                    "/Delete /TN \"" + TaskName + "\" /F", out output);
                try { if (File.Exists(TaskXmlPath)) File.Delete(TaskXmlPath); }
                catch { }
                // rc 1 means the task did not exist, which is already the goal.
                return rc == 0 || rc == 1;
            }
            catch
            {
                return false;
            }
        }

        // Called once on every first-instance launch. If the user has autostart
        // enabled, make sure the wake/logon scheduled task exists; recreate it
        // if it was removed (e.g. by a Windows update or a cleanup tool).
        // If the user disabled autostart, do nothing at all.
        internal static bool SelfHeal()
        {
            try
            {
                if (!IsEnabled())
                    return false;
                if (IsTaskEnabled())
                    return false;
                return CreateScheduledTask();
            }
            catch
            {
                return false;
            }
        }

        internal static string DescribeState()
        {
            var builder = new StringBuilder();
            builder.AppendLine("桌面备忘录 · 开机自启动状态");
            builder.AppendLine("检查时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("程序路径：" + ExecutablePath);
            builder.AppendLine("启动参数：" + StartupArgument + "（开机后在右下角只显示悬浮球，不弹窗、不抢焦点）");
            builder.AppendLine();
            builder.AppendLine("注册表方式：HKEY_CURRENT_USER\\" + RunKeyPath);
            builder.AppendLine("数值名称：" + ValueName);
            builder.AppendLine("命令行：\"" + LaunchCommand + "\"");
            string stored = ReadCommand();
            builder.AppendLine("注册表状态：" + (string.IsNullOrEmpty(stored) ? "（无）" : "已写入"));
            builder.AppendLine();
            builder.AppendLine("快捷方式方式：" + ShortcutPath);
            builder.AppendLine("快捷方式状态：" + (IsShortcutEnabled() ? "已创建" : "未创建"));
            builder.AppendLine();
            builder.AppendLine("计划任务方式：任务计划程序库 \\" + TaskName);
            builder.AppendLine("计划任务状态：" + (IsTaskEnabled() ? "已注册（登录 + 唤醒触发）" : "未注册"));
            builder.AppendLine();
            builder.AppendLine("当前总状态：" + (IsEnabled() ? "已开启（注册表 + 快捷方式 + 计划任务三重保障）" : "未开启"));
            builder.AppendLine();
            builder.AppendLine("验证方式：");
            builder.AppendLine("1. 任务管理器「启动」选项卡查看 DesktopMemo，状态应为「已启用」。");
            builder.AppendLine("2. 运行 reg query \"HKCU\\" + RunKeyPath + "\" /v " + ValueName);
            builder.AppendLine("3. 检查 Startup 文件夹是否存在 " + ShortcutName);
            builder.AppendLine("4. 运行 schtasks /Query /TN " + TaskName + " /FO LIST 应显示已注册。");
            builder.AppendLine("5. 注销/重启电脑，登录后右下角应出现悬浮球。");
            builder.AppendLine("6. 让电脑进入 Modern Standby 再唤醒，右下角应自动重新出现悬浮球。");
            builder.AppendLine("7. 运行 schtasks /Run /TN " + TaskName + " 或手动执行该命令行，效果与开机启动一致。");
            return builder.ToString();
        }
    }

    internal sealed class MemoApp
    {
        private const string AppTitle = "桌面备忘录";
        private static readonly Brush SuccessBrush = CreateBrush(0x76, 0x90, 0x62);
        private static readonly Brush AccentBrush = CreateBrush(0xB4, 0x6B, 0x2D);
        private static readonly Brush MutedBrush = CreateBrush(0x8C, 0x82, 0x72);
        private static readonly Brush ErrorBrush = CreateBrush(0xC0, 0x50, 0x3F);
        private static readonly Brush ShortBrush = CreateBrush(0xB4, 0x6B, 0x2D);
        private static readonly Brush LongForeverBrush = CreateBrush(0x76, 0x90, 0x62);
        private static readonly Brush LongRangeBrush = CreateBrush(0x5B, 0x7C, 0x99);
        private const double DockWidth = 340;
        private const double DockHeight = 540;
        private const double BallSize = 52;
        private const double DockMargin = 10;
        private const int DockIdleSeconds = 30;
        private const string SettingsKey = @"Software\DesktopMemo";

        internal static MemoApp Instance;

        private readonly Window _window;
        private readonly ObservableCollection<Note> _notes = new ObservableCollection<Note>();
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _dockTimer;
        private readonly DispatcherTimer _expireTimer;
        private DispatcherTimer _suppressTimer;
        private readonly string _dataDir;
        private readonly string _dataFile;
        private readonly string _backupFile;
        private readonly bool _startCollapsed;

        private ListBox _notesList;
        private TextBox _titleBox;
        private TextBox _contentBox;
        private Button _newButton;
        private Button _deleteButton;
        private Button _pinButton;
        private Button _minButton;
        private Button _closeButton;
        private Button _saveButton;
        private Button _settingsButton;
        private Button _autostartButton;
        private Button _quitButton;
        private Border _menuPanel;
        private Border _ballRoot;
        private FrameworkElement _cardRoot;
        private FrameworkElement _cardShadow;
        private TextBlock _autostartStateText;
        private TextBlock _statusIcon;
        private TextBlock _statusText;
        private TextBlock _charCountText;
        private TextBlock _metaText;
        private TextBlock _noteCountText;
        private TextBlock _kindBadgeText;
        private Grid _editorPanel;
        private Border _createPanel;
        private Button _typeShortButton;
        private Button _typeLongButton;
        private StackPanel _longModePanel;
        private Button _modeForeverButton;
        private Button _modeRangeButton;
        private StackPanel _dateRangePanel;
        private DatePicker _startDatePicker;
        private DatePicker _endDatePicker;
        private TextBlock _createHintText;
        private Button _createCancelButton;
        private Button _createConfirmButton;
        private Button _completeButton;
        private TextBlock _completeButtonText;
        private TextBlock _completeButtonIcon;
        private ScaleTransform _completeButtonScale;
        private TextBlock _completeStateText;
        private Border _statsPanel;
        private TextBlock _statsContent;
        private Button _statsCloseButton;
        private Button _statsButton;

        private Note _current;
        private bool _loading;
        private bool _dirty;
        private bool _saveErrorShown;
        private bool _isClosing;
        private bool _collapsed;
        private bool _userPinned = true;   // 浮窗始终置顶（alwaysOnTop），图钉按钮恒为已置顶
        private bool _suppressActivation; // 防止内部切换时触发激活逻辑
        private bool _suppressDeactivated; // 防止内部 Show/Hide 循环
        private bool _ownModalActive;      // 自身模态弹窗（MessageBox/日期选择器）打开时，禁止失焦隐藏
        private HwndSource _hwndSource;

        private NoteKind? _pendingKind;
        private LongMode? _pendingLongMode;

        internal const int ShowWindowMessage = 0x0400 + 0x7A11;
        internal const string ShowWindowMessageName = "DesktopMemo.ShowWindow.v1";
        internal static int ShowWindowMessageId;

        // ===== 窗口样式/行为 P/Invoke 与常量 =====
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080; // 工具窗口样式：Win+D 不隐藏、不进 Alt+Tab
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_MINIMIZE = 0xF020;

        public MemoApp(Window window, bool startCollapsed)
        {
            _window = window;
            _startCollapsed = startCollapsed;
            Instance = this;

            string dataDirOverride = Environment.GetEnvironmentVariable("DESKTOP_MEMO_DATA_DIR");
            _dataDir = string.IsNullOrEmpty(dataDirOverride)
                ? Program.ResolveDataDir()
                : dataDirOverride;
            _dataFile = Path.Combine(_dataDir, "notes.xml");
            _backupFile = Path.Combine(_dataDir, "notes.backup.xml");
            Directory.CreateDirectory(_dataDir);

            FindControls();
            _notesList.ItemsSource = _notes;

            _saveTimer = new DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(650);
            _saveTimer.Tick += OnSaveTimerTick;

            _dockTimer = new DispatcherTimer();
            _dockTimer.Interval = TimeSpan.FromSeconds(DockIdleSeconds);
            _dockTimer.Tick += OnDockTimerTick;

            _expireTimer = new DispatcherTimer();
            _expireTimer.Interval = TimeSpan.FromMinutes(1);
            _expireTimer.Tick += OnExpireTimerTick;
            _expireTimer.Start();

            WireEvents();
            LoadNotes();
            PurgeExpiredNotes();

            _loading = true;
            _notesList.SelectedIndex = _notes.Count > 0 ? 0 : -1;
            _loading = false;
            if (_notes.Count > 0)
                LoadNote(_notes[0], false);

            if (_startCollapsed)
                ApplyCollapsedLayout(true);

            PositionWindow();
            SetStatus("已保存", false);

            UpdateNoteCount();
            _window.Loaded += OnWindowLoaded;
            _window.Closing += OnWindowClosing;
            _window.PreviewKeyDown += OnWindowPreviewKeyDown;
            _window.SourceInitialized += OnWindowSourceInitialized;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        }

        internal void HandlePowerResume()
        {
            _window.Dispatcher.BeginInvoke(new Action(delegate
            {
                PurgeExpiredNotes();
                if (_collapsed)
                    ApplyCollapsedLayout(true);
                PositionWindow();
                _window.Topmost = true;
            }));
        }

        private void OnWindowSourceInitialized(object sender, EventArgs e)
        {
            _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(_window).Handle);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProcHook);

            // 设置 WS_EX_TOOLWINDOW 扩展样式：使窗口在 Win+D“显示桌面”时不被隐藏，
            // 且不出现在 Alt+Tab。等价于 Electron 的 type:'toolbar'。
            IntPtr handle = new WindowInteropHelper(_window).Handle;
            int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLong(handle, GWL_EXSTYLE, exStyle);
            SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 拦截最小化命令：禁止浮窗被最小化（Win+D 或系统菜单的最小化都会走到这里）。
            // 返回 handled=true 可丢弃该命令，浮窗保持显示。
            if (msg == WM_SYSCOMMAND)
            {
                int cmd = wParam.ToInt32() & 0xFFF0;
                if (cmd == SC_MINIMIZE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            // 自定义消息：来自第二实例，用于把已收起的窗口重新展开。
            if (ShowWindowMessageId != 0 && msg == ShowWindowMessageId)
            {
                handled = true;
                _window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_window.WindowState == WindowState.Minimized)
                        _window.WindowState = WindowState.Normal;
                    if (!_window.IsVisible)
                        _window.Show();
                    PositionWindow();
                    ExpandFromBall();
                }));
            }
            return IntPtr.Zero;
        }

        private void FindControls()
        {
            _notesList = Find<ListBox>("NotesList");
            _titleBox = Find<TextBox>("TitleBox");
            _contentBox = Find<TextBox>("ContentBox");
            _newButton = Find<Button>("NewButton");
            _deleteButton = Find<Button>("DeleteButton");
            _pinButton = Find<Button>("PinButton");
            _minButton = Find<Button>("MinButton");
            _closeButton = Find<Button>("CloseButton");
            _saveButton = Find<Button>("SaveButton");
            _statusIcon = Find<TextBlock>("StatusIcon");
            _statusText = Find<TextBlock>("StatusText");
            _charCountText = Find<TextBlock>("CharCountText");
            _metaText = Find<TextBlock>("MetaText");
            _noteCountText = Find<TextBlock>("NoteCountText");
            _settingsButton = Find<Button>("SettingsButton");
            _autostartButton = Find<Button>("AutostartButton");
            _quitButton = Find<Button>("QuitButton");
            _menuPanel = Find<Border>("MenuPanel");
            _ballRoot = Find<Border>("BallRoot");
            _cardRoot = Find<FrameworkElement>("CardRoot");
            _cardShadow = Find<FrameworkElement>("CardShadow");
            _autostartStateText = Find<TextBlock>("AutostartStateText");
            _kindBadgeText = Find<TextBlock>("KindBadgeText");
            _editorPanel = Find<Grid>("EditorPanel");
            _createPanel = Find<Border>("CreatePanel");
            _typeShortButton = Find<Button>("TypeShortButton");
            _typeLongButton = Find<Button>("TypeLongButton");
            _longModePanel = Find<StackPanel>("LongModePanel");
            _modeForeverButton = Find<Button>("ModeForeverButton");
            _modeRangeButton = Find<Button>("ModeRangeButton");
            _dateRangePanel = Find<StackPanel>("DateRangePanel");
            _startDatePicker = Find<DatePicker>("StartDatePicker");
            _endDatePicker = Find<DatePicker>("EndDatePicker");
            _createHintText = Find<TextBlock>("CreateHintText");
            _createCancelButton = Find<Button>("CreateCancelButton");
            _createConfirmButton = Find<Button>("CreateConfirmButton");
            _completeButton = Find<Button>("CompleteButton");
            _completeButtonText = Find<TextBlock>("CompleteButtonText");
            _completeButtonIcon = Find<TextBlock>("CompleteButtonIcon");
            _completeButtonScale = _completeButton.RenderTransform as ScaleTransform;
            _completeStateText = Find<TextBlock>("CompleteStateText");
            _statsPanel = Find<Border>("StatsPanel");
            _statsContent = Find<TextBlock>("StatsContent");
            _statsCloseButton = Find<Button>("StatsCloseButton");
            _statsButton = Find<Button>("StatsButton");
        }

        private T Find<T>(string name) where T : class
        {
            T element = _window.FindName(name) as T;
            if (element == null)
                throw new InvalidOperationException("界面元素缺失：" + name);
            return element;
        }

        private void WireEvents()
        {
            _notesList.SelectionChanged += NotesListSelectionChanged;
            _titleBox.TextChanged += EditorTextChanged;
            _contentBox.TextChanged += EditorTextChanged;

            _newButton.Click += delegate { OpenCreatePanel(); };
            _deleteButton.Click += delegate { DeleteCurrentNote(); };
            _saveButton.Click += delegate { SaveNow(); };
            _pinButton.Click += delegate { ToggleTopmost(); };
            _minButton.Click += delegate { CollapseToBall(); };
            _closeButton.Click += delegate { CollapseToBall(); };
            _settingsButton.Click += delegate { ToggleMenu(); };
            _autostartButton.Click += delegate { ToggleAutostart(); };
            _quitButton.Click += delegate { QuitFromMenu(); };
            _titleBox.PreviewKeyDown += TitleBoxPreviewKeyDown;
            _ballRoot.MouseLeftButtonDown += BallMouseLeftButtonDown;
            _ballRoot.MouseLeftButtonUp += BallMouseLeftButtonUp;

            _typeShortButton.Click += delegate { SelectCreateType(NoteKind.Short); };
            _typeLongButton.Click += delegate { SelectCreateType(NoteKind.Long); };
            _modeForeverButton.Click += delegate { SelectLongMode(LongMode.Forever); };
            _modeRangeButton.Click += delegate { SelectLongMode(LongMode.Range); };
            _createCancelButton.Click += delegate { CloseCreatePanel(); };
            _createConfirmButton.Click += delegate { ConfirmCreateNote(); };
            _startDatePicker.SelectedDateChanged += delegate { UpdateCreateHint(); };
            _endDatePicker.SelectedDateChanged += delegate { UpdateCreateHint(); };
            // 日期选择器弹出日历时按"自身弹窗"处理，避免主窗口失焦被隐藏
            _startDatePicker.CalendarOpened += delegate { _ownModalActive = true; };
            _startDatePicker.CalendarClosed += delegate { _ownModalActive = false; };
            _endDatePicker.CalendarOpened += delegate { _ownModalActive = true; };
            _endDatePicker.CalendarClosed += delegate { _ownModalActive = false; };

            _completeButton.Click += delegate { ToggleComplete(); };
            _statsButton.Click += delegate { ToggleStats(); };
            _statsCloseButton.Click += delegate { HideStats(); };

            _window.PreviewMouseDown += OnWindowPreviewMouseDown;
            _window.PreviewMouseWheel += delegate { RestartDockTimer(); };
            _window.StateChanged += OnWindowStateChanged;
            _window.Deactivated += OnWindowDeactivated;
            _window.Activated += OnWindowActivated;
        }

        // 窗口失去焦点：始终保持在右下角可见，不隐藏（用户要求浮窗不被任何应用/Win+D 隐藏）。
        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            HideMenu();
            if (_isClosing)
                return;
            // 失焦时重新定位到右下角并确保在最上层，但不隐藏，也不切换到其他窗口。
            try
            {
                PositionWindow();
            }
            catch
            {
            }
        }

        // 窗口获得焦点：恢复显示并重新定位
        private void OnWindowActivated(object sender, EventArgs e)
        {
            if (_isClosing || _suppressActivation)
                return;
            if (!_window.IsVisible)
            {
                _suppressActivation = true;
                _suppressDeactivated = true;
                try
                {
                    _window.Show();
                    PositionWindow();
                    if (_collapsed)
                        ApplyCollapsedLayout(true);
                    else
                        ApplyCollapsedLayout(false);
                    PositionWindow();
                }
                finally
                {
                    _suppressActivation = false;
                    _suppressDeactivated = false;
                }
            }
            if (_collapsed)
                PositionWindow();
        }

        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            RestartDockTimer();
            if (_menuPanel != null && _menuPanel.Visibility == Visibility.Visible)
            {
                DependencyObject source = e.OriginalSource as DependencyObject;
                if (!IsDescendantOf(source, _menuPanel) && !IsDescendantOf(source, _settingsButton))
                    HideMenu();
            }
            if (_statsPanel != null && _statsPanel.Visibility == Visibility.Visible)
            {
                DependencyObject source2 = e.OriginalSource as DependencyObject;
                if (!IsDescendantOf(source2, _statsPanel) && !IsDescendantOf(source2, _statsButton))
                    HideStats();
            }
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor))
                    return true;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 浮窗始终置顶（alwaysOnTop）
            _window.Topmost = true;
            PositionWindow();
            RestartDockTimer();
            UpdateAutostartVisual();

            if (_startCollapsed)
            {
                ApplyCollapsedLayout(true);
                PositionWindow();
                return;
            }

            FrameworkElement root = _window.FindName("Root") as FrameworkElement;
            TranslateTransform slide = _window.FindName("RootSlide") as TranslateTransform;
            if (root != null)
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260));
                fade.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                fade.FillBehavior = FillBehavior.Stop;
                root.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            if (slide != null)
            {
                var move = new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(300));
                move.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.FillBehavior = FillBehavior.Stop;
                slide.BeginAnimation(TranslateTransform.XProperty, move);
            }

            _window.Dispatcher.BeginInvoke(new Action(FocusEditor), DispatcherPriority.ApplicationIdle);
        }

        private void FocusEditor()
        {
            if (_isClosing)
                return;
            if (_createPanel.Visibility == Visibility.Visible)
            {
                _typeShortButton.Focus();
                return;
            }
            _contentBox.Focus();
            _contentBox.CaretIndex = _contentBox.Text.Length;
        }

        private void PositionWindow()
        {
            Rect workArea = SystemParameters.WorkArea;

            if (_collapsed)
            {
                double ballBox = BallSize + 24;
                _window.Width = ballBox;
                _window.Height = ballBox;
                _window.Left = Math.Max(workArea.Left, workArea.Right - ballBox);
                _window.Top = Math.Max(workArea.Top, workArea.Bottom - ballBox);
                return;
            }

            double maxWidth = Math.Max(300, workArea.Width - 24);
            double maxHeight = Math.Max(420, workArea.Height - 24);
            _window.Width = Math.Min(DockWidth, maxWidth);
            _window.Height = Math.Min(DockHeight, maxHeight);
            _window.Left = Math.Max(workArea.Left + 8, workArea.Right - _window.Width - DockMargin);
            _window.Top = Math.Max(workArea.Top + 8, workArea.Bottom - _window.Height - DockMargin);
        }

        private void ApplyCollapsedLayout(bool collapsed)
        {
            _collapsed = collapsed;
            Visibility cardVisibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            if (_cardRoot != null)
                _cardRoot.Visibility = cardVisibility;
            if (_cardShadow != null)
                _cardShadow.Visibility = cardVisibility;
            if (_ballRoot != null)
                _ballRoot.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            HideMenu();
        }

        internal void CollapseToBall()
        {
            if (_isClosing)
                return;

            SaveIfDirty();
            _saveTimer.Stop();
            if (!_collapsed)
            {
                CloseCreatePanel();
                HideStats();
                ApplyCollapsedLayout(true);
            }
            PositionWindow();

            _window.Topmost = _userPinned;
            _suppressDeactivated = true;
            try { _window.Activate(); }
            finally { }
            // 延迟取消抑制：防止收起后紧接着的焦点切换立即隐藏悬浮球
            if (_suppressTimer == null)
            {
                _suppressTimer = new DispatcherTimer();
                _suppressTimer.Interval = TimeSpan.FromMilliseconds(800);
                _suppressTimer.Tick += delegate
                {
                    _suppressTimer.Stop();
                    _suppressDeactivated = false;
                };
            }
            _suppressTimer.Stop();
            _suppressTimer.Start();
        }

        internal void ExpandFromBall()
        {
            if (_isClosing)
                return;

            _suppressActivation = true;
            _suppressDeactivated = true;
            try
            {
                if (!_window.IsVisible)
                {
                    _window.Show();
                }
                if (_collapsed)
                    ApplyCollapsedLayout(false);
                _window.Topmost = true;
                PositionWindow();
                // 再次设置位置，确保 Show() 后位置生效
                _window.Top = Math.Max(SystemParameters.WorkArea.Top + 8,
                    SystemParameters.WorkArea.Bottom - _window.Height - 10);
                _window.Left = Math.Max(SystemParameters.WorkArea.Left + 8,
                    SystemParameters.WorkArea.Right - _window.Width - 10);
                RestartDockTimer();
                _window.Activate();
            }
            finally
            {
                _suppressActivation = false;
            }
            // 延迟取消抑制：防止 ExpandFromBall 后紧接着的 WA_INACTIVE（来自第二实例退出）立即隐藏窗口
            if (_suppressTimer == null)
            {
                _suppressTimer = new DispatcherTimer();
                _suppressTimer.Interval = TimeSpan.FromMilliseconds(800);
                _suppressTimer.Tick += delegate
                {
                    _suppressTimer.Stop();
                    _suppressDeactivated = false;
                };
            }
            _suppressTimer.Stop();
            _suppressTimer.Start();
            _window.Dispatcher.BeginInvoke(new Action(FocusEditor), DispatcherPriority.ApplicationIdle);
        }

        private void BallMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void BallMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ExpandFromBall();
        }

        private void ToggleMenu()
        {
            if (_menuPanel.Visibility == Visibility.Visible)
            {
                HideMenu();
                return;
            }
            HideStats();
            UpdateAutostartVisual();
            _menuPanel.Visibility = Visibility.Visible;
        }

        private void HideMenu()
        {
            if (_menuPanel != null && _menuPanel.Visibility != Visibility.Collapsed)
                _menuPanel.Visibility = Visibility.Collapsed;
        }

        private void ToggleAutostart()
        {
            try
            {
                if (StartupRegistrar.IsEnabled())
                {
                    StartupRegistrar.Disable();
                    SetStatus("已关闭开机自启动", false);
                }
                else
                {
                    StartupRegistrar.Enable();
                    SetStatus("已开启开机自启动", false);
                }
            }
            catch (Exception ex)
            {
                _ownModalActive = true;
                try
                {
                    MessageBox.Show(_window,
                        "开机自启动设置失败：\n\n" + ex.Message,
                        AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally { _ownModalActive = false; }
            }
            UpdateAutostartVisual();
        }

        private void UpdateAutostartVisual()
        {
            bool enabled = StartupRegistrar.IsEnabled();
            if (_autostartStateText != null)
            {
                _autostartStateText.Text = enabled ? "已开启" : "已关闭";
                _autostartStateText.Foreground = enabled ? AccentBrush : MutedBrush;
            }
            if (_autostartButton != null)
            {
                _autostartButton.ToolTip = enabled
                    ? "点击关闭开机自启动（注册表 + 启动文件夹快捷方式）"
                    : "点击开启开机自启动：登录后右下角显示悬浮球，唤醒后自动恢复";
            }
        }

        private void QuitFromMenu()
        {
            HideMenu();
            _window.Close();
        }

        private void RestartDockTimer()
        {
            if (_dockTimer == null || _isClosing)
                return;
            _dockTimer.Stop();
            _dockTimer.Start();
        }

        private void OnDockTimerTick(object sender, EventArgs e)
        {
            if (_isClosing || _window.WindowState == WindowState.Minimized)
                return;
            if (_collapsed)
                return;
            PositionWindow();
        }

        private void OnExpireTimerTick(object sender, EventArgs e)
        {
            PurgeExpiredNotes();
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (_isClosing)
                return;
            // 禁止浮窗被最小化（Win+D 等场景会把窗口置为 Minimized）。
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
                _window.Show();
                PositionWindow();
                if (_collapsed)
                    ApplyCollapsedLayout(true);
                return;
            }
            if (_window.WindowState == WindowState.Normal)
            {
                PositionWindow();
                RestartDockTimer();
            }
        }

        private void OnSystemParametersChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isClosing)
                return;
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "WorkArea")
            {
                PositionWindow();
                _window.Topmost = true;
            }
        }

        // ===== 备忘类型创建流程 =====

        private void OpenCreatePanel()
        {
            if (!SaveIfDirty())
                return;
            CloseCreatePanel();
            _pendingKind = null;
            _pendingLongMode = null;
            _startDatePicker.SelectedDate = DateTime.Today;
            _endDatePicker.SelectedDate = DateTime.Today.AddDays(7);
            _longModePanel.Visibility = Visibility.Collapsed;
            _dateRangePanel.Visibility = Visibility.Collapsed;
            _createHintText.Text = "";
            _createConfirmButton.IsEnabled = false;
            _typeShortButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
            _typeLongButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
            _modeForeverButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
            _modeRangeButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
            _editorPanel.Visibility = Visibility.Collapsed;
            _createPanel.Visibility = Visibility.Visible;
            _typeShortButton.Focus();
        }

        private void CloseCreatePanel()
        {
            if (_createPanel.Visibility != Visibility.Visible)
                return;
            _createPanel.Visibility = Visibility.Collapsed;
            _editorPanel.Visibility = Visibility.Visible;
            _pendingKind = null;
            _pendingLongMode = null;
            FocusEditor();
        }

        private void SelectCreateType(NoteKind kind)
        {
            _pendingKind = kind;
            _typeShortButton.BorderBrush = kind == NoteKind.Short ? AccentBrush
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
            _typeLongButton.BorderBrush = kind == NoteKind.Long ? AccentBrush
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));

            if (kind == NoteKind.Long)
            {
                _longModePanel.Visibility = Visibility.Visible;
                if (_pendingLongMode == null)
                {
                    _pendingLongMode = LongMode.Forever;
                }
                UpdateLongModeButtons();
                _dateRangePanel.Visibility = _pendingLongMode == LongMode.Range
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                _longModePanel.Visibility = Visibility.Collapsed;
                _dateRangePanel.Visibility = Visibility.Collapsed;
            }
            UpdateCreateHint();
        }

        private void SelectLongMode(LongMode mode)
        {
            _pendingLongMode = mode;
            UpdateLongModeButtons();
            _dateRangePanel.Visibility = mode == LongMode.Range
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateCreateHint();
        }

        private void UpdateLongModeButtons()
        {
            _modeForeverButton.BorderBrush = _pendingLongMode == LongMode.Forever ? AccentBrush
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
            _modeRangeButton.BorderBrush = _pendingLongMode == LongMode.Range ? AccentBrush
                : new SolidColorBrush(Color.FromRgb(0xE0, 0xD3, 0xBD));
        }

        private void UpdateCreateHint()
        {
            if (_pendingKind == null)
            {
                _createHintText.Text = "";
                _createConfirmButton.IsEnabled = false;
                return;
            }

            if (_pendingKind == NoteKind.Short)
            {
                DateTime expire = DateTime.Now.AddHours(24);
                _createHintText.Text = "短期备忘将在 " + expire.ToString("M月d日 HH:mm") + " 自动失效";
                _createConfirmButton.IsEnabled = true;
                return;
            }

            if (_pendingLongMode == LongMode.Forever)
            {
                _createHintText.Text = "长期备忘将永久保留，直至你手动删除";
                _createConfirmButton.IsEnabled = true;
                return;
            }

            DateTime? start = _startDatePicker.SelectedDate;
            DateTime? end = _endDatePicker.SelectedDate;
            if (start == null || end == null)
            {
                _createHintText.Text = "请选择起始日期和结束日期";
                _createConfirmButton.IsEnabled = false;
                return;
            }
            if (end.Value < start.Value)
            {
                _createHintText.Text = "结束日期不能早于起始日期";
                _createConfirmButton.IsEnabled = false;
                return;
            }
            _createHintText.Text = "保留区间：" + start.Value.ToString("yyyy-MM-dd") + " 至 " + end.Value.ToString("yyyy-MM-dd");
            _createConfirmButton.IsEnabled = true;
        }

        private void ConfirmCreateNote()
        {
            if (_pendingKind == null)
                return;

            if (_current != null &&
                string.IsNullOrWhiteSpace(_current.Title) &&
                string.IsNullOrWhiteSpace(_current.Content))
            {
                _notes.Remove(_current);
                _current = null;
            }

            var note = new Note();
            note.Id = Guid.NewGuid().ToString("N");
            note.Kind = _pendingKind.Value;
            note.CreatedAt = DateTime.Now;
            note.Updated = note.CreatedAt;

            if (_pendingKind == NoteKind.Short)
            {
                note.ExpireAt = note.CreatedAt.AddHours(24);
                note.LongMode = LongMode.Forever;
            }
            else
            {
                note.LongMode = _pendingLongMode ?? LongMode.Forever;
                if (note.LongMode == LongMode.Range)
                {
                    note.StartDate = _startDatePicker.SelectedDate ?? DateTime.Today;
                    note.EndDate = _endDatePicker.SelectedDate ?? DateTime.Today.AddDays(7);
                }
            }

            _notes.Insert(0, note);
            CloseCreatePanel();
            _loading = true;
            _notesList.SelectedIndex = 0;
            _loading = false;

            LoadNote(note, false);
            _dirty = true;
            SaveAll(false);
            _titleBox.Focus();
            UpdateNoteCount();
        }

        // ===== 数据加载与失效处理 =====

        private void LoadNotes()
        {
            bool hadFile = File.Exists(_dataFile);
            if (hadFile)
            {
                try
                {
                    XDocument document = XDocument.Load(_dataFile);
                    if (document.Root != null)
                    {
                        foreach (XElement element in document.Root.Elements("note"))
                        {
                            string id = (string)element.Attribute("id");
                            DateTime updated;
                            string updatedValue = (string)element.Attribute("updated");
                            if (string.IsNullOrEmpty(updatedValue) ||
                                !DateTime.TryParse(updatedValue, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out updated))
                            {
                                updated = DateTime.Now;
                            }

                            string kindStr = (string)element.Attribute("kind");
                            string longModeStr = (string)element.Attribute("longMode");
                            string createdStr = (string)element.Attribute("created");
                            string expireStr = (string)element.Attribute("expireAt");
                            string startStr = (string)element.Attribute("startDate");
                            string endStr = (string)element.Attribute("endDate");
                            string completedStr = (string)element.Attribute("completed");
                            string completedAtStr = (string)element.Attribute("completedAt");

                            NoteKind kind = string.Equals(kindStr, "Short", StringComparison.OrdinalIgnoreCase)
                                ? NoteKind.Short : NoteKind.Long;
                            LongMode mode = string.Equals(longModeStr, "Range", StringComparison.OrdinalIgnoreCase)
                                ? LongMode.Range : LongMode.Forever;

                            DateTime created = updated;
                            if (!string.IsNullOrEmpty(createdStr) &&
                                DateTime.TryParse(createdStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out created))
                            {
                            }

                            DateTime? expireAt = null;
                            if (!string.IsNullOrEmpty(expireStr))
                            {
                                DateTime ep;
                                if (DateTime.TryParse(expireStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out ep))
                                    expireAt = ep;
                            }

                            DateTime? startDate = null;
                            if (!string.IsNullOrEmpty(startStr))
                            {
                                DateTime sd;
                                if (DateTime.TryParse(startStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out sd))
                                    startDate = sd;
                            }

                            DateTime? endDate = null;
                            if (!string.IsNullOrEmpty(endStr))
                            {
                                DateTime ed;
                                if (DateTime.TryParse(endStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out ed))
                                    endDate = ed;
                            }

                            bool completed = string.Equals(completedStr, "true", StringComparison.OrdinalIgnoreCase);

                            DateTime? completedAt = null;
                            if (!string.IsNullOrEmpty(completedAtStr))
                            {
                                DateTime ca;
                                if (DateTime.TryParse(completedAtStr, CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind, out ca))
                                    completedAt = ca;
                            }

                            string title = (string)element.Element("title");
                            string content = (string)element.Element("content");

                            _notes.Add(new Note
                            {
                                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
                                Title = title ?? string.Empty,
                                Content = content ?? string.Empty,
                                Kind = kind,
                                LongMode = mode,
                                CreatedAt = created,
                                ExpireAt = expireAt,
                                StartDate = startDate,
                                EndDate = endDate,
                                Completed = completed,
                                CompletedAt = completedAt,
                                Updated = updated
                            });
                        }
                    }
                }
                catch (Exception)
                {
                    PreserveCorruptStore();
                    _notes.Clear();
                }
            }

            if (_notes.Count == 0)
            {
                _notes.Add(CreateWelcomeNote());
                SaveAll(false);
                _dirty = false;
            }
        }

        internal void PurgeExpiredNotes()
        {
            DateTime now = DateTime.Now;
            bool changed = false;
            for (int i = _notes.Count - 1; i >= 0; i--)
            {
                Note note = _notes[i];
                if (note.IsExpired(now))
                {
                    if (_notes.Count <= 1)
                        break;
                    bool wasCurrent = (_current == note);
                    _notes.RemoveAt(i);
                    changed = true;
                    if (wasCurrent)
                    {
                        _current = null;
                        int idx = Math.Min(i, _notes.Count - 1);
                        if (idx >= 0)
                        {
                            _loading = true;
                            _notesList.SelectedIndex = idx;
                            _loading = false;
                            LoadNote(_notes[idx], true);
                        }
                    }
                }
            }
            if (changed)
            {
                SaveAll(false);
                UpdateNoteCount();
            }
        }

        private void PreserveCorruptStore()
        {
            try
            {
                string target = _dataFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(_dataFile, target, true);
            }
            catch
            {
            }
        }

        private static Note CreateWelcomeNote()
        {
            var note = new Note();
            note.Id = Guid.NewGuid().ToString("N");
            note.Kind = NoteKind.Long;
            note.LongMode = LongMode.Forever;
            note.CreatedAt = DateTime.Now;
            note.Title = "欢迎使用桌面备忘录";
            note.Content =
                "这里是你专属的侧边小天地。\n\n" +
                "· 点击「新建备忘」时先选类型：短期或长期\n" +
                "· 短期备忘在添加 24 小时后自动失效\n" +
                "· 长期可选永久保留，或指定起止日期区间\n" +
                "· 点右下角打卡按钮，可在「待办 / 已完成」之间来回切换\n" +
                "· 点统计图标查看每天已完成/未完成数量\n" +
                "· 所有修改自动保存，Ctrl+S 立即保存\n" +
                "· 点「—」收起为悬浮球，点击悬浮球重新展开\n" +
                "· 齿轮菜单可开关开机自启动\n\n" +
                "现在，写下你的第一条备忘吧。";
            note.Updated = note.CreatedAt;
            return note;
        }

        private void NotesListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading)
                return;
            Note selected = _notesList.SelectedItem as Note;
            if (selected == null || selected == _current)
                return;
            if (!SaveIfDirty())
                return;
            LoadNote(selected, true);
        }

        private void LoadNote(Note note, bool moveCaret)
        {
            _saveTimer.Stop();
            _loading = true;
            _current = note;
            _notesList.SelectedItem = note;
            _titleBox.Text = note.Title;
            _contentBox.Text = note.Content;
            _loading = false;
            _dirty = false;

            UpdateKindBadge();
            UpdateCharCount();
            UpdateMetaText();
            UpdateCompleteVisual();
            SetStatus("已保存", false);
            _contentBox.ScrollToHome();
            if (moveCaret)
            {
                _contentBox.CaretIndex = 0;
            }
        }

        private void UpdateKindBadge()
        {
            if (_current == null || _kindBadgeText == null)
                return;
            _kindBadgeText.Text = _current.KindShortText;
            _kindBadgeText.Foreground = _current.KindBrush;
        }

        private void EditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _current == null)
                return;

            _current.Title = _titleBox.Text;
            _current.Content = _contentBox.Text;
            _dirty = true;
            UpdateCharCount();
            SetStatus("正在保存…", true);
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTimerTick(object sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveAll(true);
        }

        private void DeleteCurrentNote()
        {
            if (_current == null)
                return;
            if (_notes.Count <= 1)
                return;

            _ownModalActive = true;
            MessageBoxResult result;
            try
            {
                result = MessageBox.Show(_window,
                    "确定要删除「" + _current.DisplayTitle + "」吗？\n\n删除后无法恢复。",
                    "删除备忘", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            }
            finally { _ownModalActive = false; }
            if (result != MessageBoxResult.Yes)
                return;

            int index = _notes.IndexOf(_current);
            _loading = true;
            _notes.Remove(_current);
            if (index >= _notes.Count)
                index = _notes.Count - 1;
            _notesList.SelectedIndex = index;
            _loading = false;

            _current = null;
            LoadNote(_notes[index], true);
            _dirty = true;
            SaveAll(false);
        }

        private void SaveNow()
        {
            _saveTimer.Stop();
            _dirty = true;
            SaveAll(true);
        }

        private bool SaveIfDirty()
        {
            if (!_dirty)
                return true;
            return SaveAll(false);
        }

        private bool SaveAll(bool showStatus)
        {
            if (_current != null)
            {
                _current.Title = _titleBox.Text;
                _current.Content = _contentBox.Text;
            }

            try
            {
                DateTime now = DateTime.Now;
                if (_current != null)
                    _current.Updated = now;

                WriteStore();
                _dirty = false;
                _saveErrorShown = false;
                UpdateMetaText();
                UpdateNoteCount();
                if (showStatus)
                    SetStatus("已保存 " + now.ToString("HH:mm"), false);
                else
                    SetStatus("已保存", false);
                return true;
            }
            catch (Exception ex)
            {
                SetErrorStatus();
                if (!_saveErrorShown)
                {
                    _saveErrorShown = true;
                    _ownModalActive = true;
                    try
                    {
                        MessageBox.Show(_window,
                            "备忘保存失败，请检查磁盘空间或文件权限。\n\n" + ex.Message,
                            AppTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    finally { _ownModalActive = false; }
                }
                return false;
            }
        }

        private void WriteStore()
        {
            Directory.CreateDirectory(_dataDir);

            XDocument document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("notes",
                    new XAttribute("version", "2"),
                    BuildNoteElements()));

            string tempFile = _dataFile + ".tmp";
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.Encoding = new UTF8Encoding(false);
            settings.NewLineChars = "\r\n";

            using (XmlWriter writer = XmlWriter.Create(tempFile, settings))
                document.Save(writer);

            if (File.Exists(_dataFile))
            {
                try
                {
                    File.Replace(tempFile, _dataFile, _backupFile, true);
                }
                catch
                {
                    File.Copy(tempFile, _dataFile, true);
                    TryDelete(tempFile);
                }
            }
            else
            {
                File.Move(tempFile, _dataFile);
            }
        }

        private XElement[] BuildNoteElements()
        {
            var elements = new XElement[_notes.Count];
            for (int i = 0; i < _notes.Count; i++)
            {
                Note note = _notes[i];
                var el = new XElement("note",
                    new XAttribute("id", note.Id),
                    new XAttribute("kind", note.Kind == NoteKind.Short ? "Short" : "Long"),
                    new XAttribute("longMode", note.LongMode == LongMode.Range ? "Range" : "Forever"),
                    new XAttribute("created", note.CreatedAt.ToString("o", CultureInfo.InvariantCulture)),
                    new XAttribute("updated", note.Updated.ToString("o", CultureInfo.InvariantCulture)),
                    new XElement("title", note.Title ?? string.Empty),
                    new XElement("content", note.Content ?? string.Empty));
                if (note.ExpireAt.HasValue)
                    el.Add(new XAttribute("expireAt", note.ExpireAt.Value.ToString("o", CultureInfo.InvariantCulture)));
                if (note.StartDate.HasValue)
                    el.Add(new XAttribute("startDate", note.StartDate.Value.ToString("o", CultureInfo.InvariantCulture)));
                if (note.EndDate.HasValue)
                    el.Add(new XAttribute("endDate", note.EndDate.Value.ToString("o", CultureInfo.InvariantCulture)));
                el.Add(new XAttribute("completed", note.Completed ? "true" : "false"));
                if (note.CompletedAt.HasValue)
                    el.Add(new XAttribute("completedAt", note.CompletedAt.Value.ToString("o", CultureInfo.InvariantCulture)));
                elements[i] = el;
            }
            return elements;
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private void ToggleTopmost()
        {
            // 浮窗始终置顶（alwaysOnTop），点击图钉仅保持确认，不改变置顶状态。
            _window.Topmost = true;
            _userPinned = true;
            UpdatePinVisual();
            _window.Activate();
        }

        private void ToggleComplete()
        {
            if (_current == null)
                return;
            // Round 14: check-in is BIDIRECTIONAL again. Every click flips
            // Note.Completed, so the user can move freely between "not completed"
            // and "completed" as often as needed. Note.Completed remains the single
            // source of truth: UpdateCompleteVisual() refreshes label, icon, colour
            // and status text, SaveAll() persists completed/completedAt, and the
            // note-count / stats refresh below keep every related view in sync.
            _current.Completed = !_current.Completed;
            _dirty = true;
            UpdateCompleteVisual();
            AnimateCompletePop();
            SaveAll(false);
            UpdateNoteCount();
            if (_statsPanel != null && _statsPanel.Visibility == Visibility.Visible)
                ShowStats();
        }

        private void UpdateCompleteVisual()
        {
            if (_current == null || _completeButton == null)
                return;
            // Round 14: BIDIRECTIONAL check-in. Setting Tag is the ONLY thing code
            // does for colour: the XAML CompleteCircleStyle maps Tag="Pending"/"Done"
            // to the page's AccentBrush (#B46B2D) / LongForeverBrush (#769062) on the
            // ONE solid disc. There are no hover/pressed tints and no code-set local
            // colour value, so the previous state's pure colour auto-reverts on every
            // flip (nothing can linger). Note.Completed stays the single source of
            // truth; both states keep a distinct label, a distinct icon and a distinct
            // pure colour, and BOTH remain clickable so the user can toggle back and
            // forth. IsEnabled is set explicitly here to guarantee the Done state is
            // never left disabled. Status text and its brush come from the same state.
            bool done = _current.Completed;
            _completeButton.Tag = done ? "Done" : "Pending";
            _completeButton.IsEnabled = true;
            _completeButtonText.Text = done ? "已完成" : "待办";
            if (_completeButtonIcon != null)
                _completeButtonIcon.Text = done ? "\u2713" : "\u25CB";
            _completeButton.ToolTip = done ? "点击取消打卡，标记为未完成" : "点击打卡，标记为已完成";
            if (_completeStateText != null)
            {
                _completeStateText.Text = _current.CompletedText;
                _completeStateText.Foreground = done ? SuccessBrush : MutedBrush;
            }
        }

        private void AnimateCompletePop()
        {
            if (_completeButtonScale == null)
                return;
            // 轻微放大回弹的动效，两种状态触发一致，风格统一。
            DoubleAnimation popX = new DoubleAnimation(1.16, TimeSpan.FromMilliseconds(130));
            popX.AutoReverse = true;
            popX.FillBehavior = FillBehavior.Stop;
            DoubleAnimation popY = new DoubleAnimation(1.16, TimeSpan.FromMilliseconds(130));
            popY.AutoReverse = true;
            popY.FillBehavior = FillBehavior.Stop;
            _completeButtonScale.BeginAnimation(ScaleTransform.ScaleXProperty, popX);
            _completeButtonScale.BeginAnimation(ScaleTransform.ScaleYProperty, popY);
        }

        private void ToggleStats()
        {
            if (_statsPanel.Visibility == Visibility.Visible)
                HideStats();
            else
                ShowStats();
        }

        private void ShowStats()
        {
            HideMenu();
            _statsContent.Text = BuildStatsText();
            _statsPanel.Visibility = Visibility.Visible;
        }

        private void HideStats()
        {
            if (_statsPanel != null && _statsPanel.Visibility != Visibility.Collapsed)
                _statsPanel.Visibility = Visibility.Collapsed;
        }

        private string BuildStatsText()
        {
            var byDay = new System.Collections.Generic.Dictionary<DateTime, System.Collections.Generic.List<Note>>();
            for (int i = 0; i < _notes.Count; i++)
            {
                Note n = _notes[i];
                DateTime day = n.CreatedAt.Date;
                System.Collections.Generic.List<Note> list;
                if (!byDay.TryGetValue(day, out list))
                {
                    list = new System.Collections.Generic.List<Note>();
                    byDay[day] = list;
                }
                list.Add(n);
            }

            var days = new System.Collections.Generic.List<DateTime>(byDay.Keys);
            days.Sort();
            days.Reverse();

            if (days.Count == 0)
                return "暂无备忘记录。";

            var sb = new StringBuilder();
            int shown = 0;
            for (int d = 0; d < days.Count && shown < 14; d++)
            {
                DateTime day = days[d];
                System.Collections.Generic.List<Note> dayNotes = byDay[day];
                int done = 0;
                int undone = 0;
                for (int i = 0; i < dayNotes.Count; i++)
                {
                    if (dayNotes[i].Completed) done++; else undone++;
                }
                int total = dayNotes.Count;

                string dayLabel;
                DateTime today = DateTime.Today;
                if (day == today) dayLabel = "今天";
                else if (day == today.AddDays(-1)) dayLabel = "昨天";
                else if (day == today.AddDays(-2)) dayLabel = "前天";
                else if (day.Year == today.Year) dayLabel = day.ToString("M月d日 ddd");
                else dayLabel = day.ToString("yyyy年M月d日");

                sb.Append(dayLabel);
                sb.Append("  ");
                sb.Append(total.ToString(CultureInfo.InvariantCulture));
                sb.Append("项");
                if (done > 0)
                {
                    sb.Append("  ✓");
                    sb.Append(done.ToString(CultureInfo.InvariantCulture));
                }
                if (undone > 0)
                {
                    sb.Append("  ○");
                    sb.Append(undone.ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("\n");

                for (int i = 0; i < dayNotes.Count; i++)
                {
                    Note n = dayNotes[i];
                    sb.Append("  ");
                    sb.Append(n.Completed ? "✓ " : "○ ");
                    string title = (n.Title ?? string.Empty).Trim();
                    if (title.Length == 0) title = "无标题备忘";
                    if (title.Length > 14) title = title.Substring(0, 14) + "…";
                    sb.Append(title);
                    sb.Append("\n");
                }
                sb.Append("\n");
                shown++;
            }
            return sb.ToString().TrimEnd();
        }

        private void UpdatePinVisual()
        {
            // 浮窗始终置顶（alwaysOnTop），图钉按钮恒为“已置顶”状态。
            _pinButton.Foreground = AccentBrush;
            _pinButton.ToolTip = "窗口已置顶（始终显示在最前）";
        }

        private void TitleBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _contentBox.Focus();
                _contentBox.CaretIndex = _contentBox.Text.Length;
                e.Handled = true;
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            RestartDockTimer();
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Key == Key.N)
                {
                    OpenCreatePanel();
                    e.Handled = true;
                }
                else if (e.Key == Key.S)
                {
                    SaveNow();
                    e.Handled = true;
                }
            }
            if (e.Key == Key.Escape && _createPanel.Visibility == Visibility.Visible)
            {
                CloseCreatePanel();
                e.Handled = true;
            }
        }

        private void UpdateNoteCount()
        {
            _noteCountText.Text = _notes.Count.ToString(CultureInfo.InvariantCulture);
            _deleteButton.IsEnabled = _notes.Count > 1;
            int doneCount = 0;
            for (int i = 0; i < _notes.Count; i++)
            {
                if (_notes[i].Completed) doneCount++;
            }
            if (_statsButton != null)
            {
                _statsButton.ToolTip = "任务完成统计：已完成 " + doneCount.ToString(CultureInfo.InvariantCulture)
                    + " / 共 " + _notes.Count.ToString(CultureInfo.InvariantCulture) + " 条";
            }
        }

        private void UpdateCharCount()
        {
            int count = _contentBox.Text.Replace("\r", string.Empty).Replace("\n", string.Empty).Length;
            _charCountText.Text = count.ToString(CultureInfo.InvariantCulture) + " 字";
        }

        private void UpdateMetaText()
        {
            if (_current != null)
                _metaText.Text = _current.UpdatedText;
        }

        private void SetStatus(string text, bool pending)
        {
            _statusText.Text = text;
            _statusIcon.Text = pending ? "●" : "✓";
            _statusIcon.FontSize = pending ? 9 : 10.5;
            _statusIcon.Foreground = pending ? AccentBrush : SuccessBrush;
            _statusText.Foreground = pending ? AccentBrush : MutedBrush;
        }

        private void SetErrorStatus()
        {
            _statusText.Text = "保存失败";
            _statusIcon.Text = "!";
            _statusIcon.FontSize = 11;
            _statusIcon.Foreground = ErrorBrush;
            _statusText.Foreground = ErrorBrush;
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_isClosing)
                return;

            _saveTimer.Stop();
            if (!SaveIfDirty())
            {
                _ownModalActive = true;
                MessageBoxResult result;
                try
                {
                    result = MessageBox.Show(_window,
                        "最近的修改未能保存。仍要退出吗？",
                        AppTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                }
                finally { _ownModalActive = false; }
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _isClosing = true;
            _dockTimer.Stop();
            _expireTimer.Stop();
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        }

        private static Brush CreateBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    internal sealed class Note : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _content = string.Empty;
        private DateTime _updated = DateTime.Now;
        private DateTime _createdAt = DateTime.Now;
        private NoteKind _kind = NoteKind.Long;
        private LongMode _longMode = LongMode.Forever;
        private DateTime? _expireAt;
        private DateTime? _startDate;
        private DateTime? _endDate;
        private bool _completed;
        private DateTime? _completedAt;

        public string Id { get; set; }

        public bool Completed
        {
            get { return _completed; }
            set
            {
                if (_completed == value) return;
                _completed = value;
                if (value)
                    _completedAt = DateTime.Now;
                else
                    _completedAt = null;
                OnPropertyChanged("Completed");
                OnPropertyChanged("CompletedAt");
                OnPropertyChanged("CompletedText");
                OnPropertyChanged("CompletedBrush");
                OnPropertyChanged("CompletedIcon");
                OnPropertyChanged("CompletedStrike");
            }
        }

        public DateTime? CompletedAt
        {
            get { return _completedAt; }
            set
            {
                if (Nullable.Equals(_completedAt, value)) return;
                _completedAt = value;
                OnPropertyChanged("CompletedAt");
                OnPropertyChanged("CompletedText");
            }
        }

        public Brush CompletedBrush
        {
            get { return _completed ? new SolidColorBrush(Color.FromRgb(0x76, 0x90, 0x62)) : new SolidColorBrush(Color.FromRgb(0xC8, 0xBC, 0xA8)); }
        }

        public string CompletedIcon
        {
            get { return _completed ? "\uE73E" : "\uE739"; }
        }

        public string CompletedText
        {
            get
            {
                if (!_completed) return "未完成";
                if (_completedAt.HasValue)
                {
                    DateTime c = _completedAt.Value;
                    DateTime now = DateTime.Now;
                    if (c.Date == now.Date) return "今天完成 " + c.ToString("HH:mm");
                    if (c.Date == now.Date.AddDays(-1)) return "昨天完成 " + c.ToString("HH:mm");
                    if (c.Year == now.Year) return c.ToString("M月d日完成");
                    return c.ToString("yyyy年M月d日完成");
                }
                return "已完成";
            }
        }

        public TextDecorationCollection CompletedStrike
        {
            get
            {
                if (!_completed) return null;
                var deco = new TextDecoration();
                deco.Location = TextDecorationLocation.Strikethrough;
                var coll = new TextDecorationCollection();
                coll.Add(deco);
                return coll;
            }
        }

        public NoteKind Kind
        {
            get { return _kind; }
            set
            {
                if (_kind == value) return;
                _kind = value;
                OnPropertyChanged("Kind");
                OnPropertyChanged("KindShortText");
                OnPropertyChanged("KindBrush");
            }
        }

        public LongMode LongMode
        {
            get { return _longMode; }
            set
            {
                if (_longMode == value) return;
                _longMode = value;
                OnPropertyChanged("LongMode");
                OnPropertyChanged("KindShortText");
                OnPropertyChanged("KindBrush");
            }
        }

        public DateTime CreatedAt
        {
            get { return _createdAt; }
            set
            {
                if (_createdAt == value) return;
                _createdAt = value;
                OnPropertyChanged("CreatedAt");
            }
        }

        public DateTime? ExpireAt
        {
            get { return _expireAt; }
            set
            {
                if (Nullable.Equals(_expireAt, value)) return;
                _expireAt = value;
                OnPropertyChanged("ExpireAt");
                OnPropertyChanged("KindShortText");
            }
        }

        public DateTime? StartDate
        {
            get { return _startDate; }
            set { _startDate = value; OnPropertyChanged("StartDate"); OnPropertyChanged("KindShortText"); }
        }

        public DateTime? EndDate
        {
            get { return _endDate; }
            set { _endDate = value; OnPropertyChanged("EndDate"); OnPropertyChanged("KindShortText"); }
        }

        public string Title
        {
            get { return _title; }
            set
            {
                string newValue = value ?? string.Empty;
                if (_title == newValue)
                    return;
                _title = newValue;
                OnPropertyChanged("Title");
                OnPropertyChanged("DisplayTitle");
            }
        }

        public string Content
        {
            get { return _content; }
            set
            {
                string newValue = value ?? string.Empty;
                if (_content == newValue)
                    return;
                _content = newValue;
                OnPropertyChanged("Content");
                OnPropertyChanged("Preview");
            }
        }

        public DateTime Updated
        {
            get { return _updated; }
            set
            {
                if (_updated == value)
                    return;
                _updated = value;
                OnPropertyChanged("Updated");
                OnPropertyChanged("TimeShort");
                OnPropertyChanged("UpdatedText");
            }
        }

        public string DisplayTitle
        {
            get
            {
                string title = (_title ?? string.Empty).Trim();
                return title.Length == 0 ? "无标题备忘" : title;
            }
        }

        public string Preview
        {
            get
            {
                string text = (_content ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
                while (text.IndexOf("  ", StringComparison.Ordinal) >= 0)
                    text = text.Replace("  ", " ");
                if (text.Length == 0)
                    return "暂无内容";
                if (text.Length > 34)
                    return text.Substring(0, 34) + "…";
                return text;
            }
        }

        public Brush KindBrush
        {
            get
            {
                if (_kind == NoteKind.Short)
                    return new SolidColorBrush(Color.FromRgb(0xB4, 0x6B, 0x2D));
                if (_longMode == LongMode.Range)
                    return new SolidColorBrush(Color.FromRgb(0x5B, 0x7C, 0x99));
                return new SolidColorBrush(Color.FromRgb(0x76, 0x90, 0x62));
            }
        }

        public string KindShortText
        {
            get
            {
                if (_kind == NoteKind.Short)
                {
                    if (_expireAt.HasValue)
                    {
                        TimeSpan left = _expireAt.Value - DateTime.Now;
                        if (left.TotalHours <= 0)
                            return "短期·已失效";
                        if (left.TotalHours < 1)
                            return "短期·剩" + (int)left.TotalMinutes + "分";
                        return "短期·剩" + (int)left.TotalHours + "时";
                    }
                    return "短期";
                }
                if (_longMode == LongMode.Range)
                {
                    if (_endDate.HasValue)
                    {
                        DateTime today = DateTime.Today;
                        if (today > _endDate.Value)
                            return "长期·已过期";
                        return "长期·至" + _endDate.Value.ToString("M/d");
                    }
                    return "长期·区间";
                }
                return "长期·永久";
            }
        }

        public bool IsExpired(DateTime now)
        {
            if (_kind == NoteKind.Short)
            {
                if (_expireAt.HasValue && now > _expireAt.Value)
                    return true;
            }
            else if (_longMode == LongMode.Range)
            {
                if (_endDate.HasValue && now.Date > _endDate.Value.Date)
                    return true;
            }
            return false;
        }

        public string TimeShort
        {
            get
            {
                DateTime now = DateTime.Now;
                if (_updated.Date == now.Date)
                    return "今天 " + _updated.ToString("HH:mm");
                if (_updated.Date == now.Date.AddDays(-1))
                    return "昨天 " + _updated.ToString("HH:mm");
                if (_updated.Year == now.Year)
                    return _updated.ToString("M月d日");
                return _updated.ToString("yyyy年M月d日");
            }
        }

        public string UpdatedText
        {
            get
            {
                DateTime now = DateTime.Now;
                string when;
                if (_updated.Date == now.Date)
                    when = "今天 " + _updated.ToString("HH:mm");
                else if (_updated.Date == now.Date.AddDays(-1))
                    when = "昨天 " + _updated.ToString("HH:mm");
                else if (_updated.Year == now.Year)
                    when = _updated.ToString("M月d日 HH:mm");
                else
                    when = _updated.ToString("yyyy年M月d日");
                return "最近编辑 " + when;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}