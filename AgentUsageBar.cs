using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using FormsTimer = System.Windows.Forms.Timer;

[assembly: System.Reflection.AssemblyTitle("Agent Usage Bar")]
[assembly: System.Reflection.AssemblyDescription("Compact Codex and Claude Code usage widgets")]
[assembly: System.Reflection.AssemblyCompany("Local utility")]
[assembly: System.Reflection.AssemblyProduct("Agent Usage Bar")]
[assembly: System.Reflection.AssemblyVersion("2.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.0.0.0")]

namespace CodexUsageBar
{
    internal static class Program
    {
        private static Mutex _singleInstance;

        [STAThread]
        private static void Main()
        {
            bool created;
            _singleInstance = new Mutex(true, "Local\\CodexUsageBar", out created);
            if (!created)
            {
                MessageBox.Show(
                    "Agent Usage Bar or the earlier Codex Usage Bar is already running. Look for it in the system tray.",
                    "Agent Usage Bar",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                NativeMethods.TryEnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new UsageApplicationContext());
            }
            finally
            {
                if (_singleInstance != null)
                {
                    _singleInstance.ReleaseMutex();
                    _singleInstance.Dispose();
                }
            }
        }
    }

    internal enum UsageProvider { Codex, Claude }

    internal interface IUsageService : IDisposable
    {
        event Action<UsageSnapshot> SnapshotReceived;
        event Action<string> ServiceError;
        void Start();
        void RequestUsage();
    }

    internal sealed class UsageApplicationContext : ApplicationContext
    {
        private readonly UsageBarForm _codex = new UsageBarForm(UsageProvider.Codex);
        private readonly UsageBarForm _claude = new UsageBarForm(UsageProvider.Claude);

        public UsageApplicationContext()
        {
            _codex.OtherBar = _claude;
            _claude.OtherBar = _codex;
            _codex.ExitRequested += ExitAll;
            _claude.ExitRequested += ExitAll;
            _codex.ShowInitially();
            _claude.ShowInitially();
        }

        private void ExitAll()
        {
            _codex.ExitWidget();
            _claude.ExitWidget();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _codex.Dispose(); _claude.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class UsageWindow
    {
        public double UsedPercent { get; set; }
        public int DurationMinutes { get; set; }
        public DateTime ResetAtUtc { get; set; }

        public double RemainingPercent
        {
            get { return Math.Max(0, Math.Min(100, 100 - UsedPercent)); }
        }

        public string Label
        {
            get
            {
                if (DurationMinutes >= 10000 && DurationMinutes <= 10200)
                {
                    return "WEEKLY";
                }

                if (DurationMinutes > 0 && DurationMinutes % 60 == 0 && DurationMinutes <= 720)
                {
                    int hours = DurationMinutes / 60;
                    return hours == 1 ? "1 HOUR" : string.Format(CultureInfo.InvariantCulture, "{0} HOUR", hours);
                }

                if (DurationMinutes >= 1440 && DurationMinutes % 1440 == 0)
                {
                    int days = DurationMinutes / 1440;
                    return days == 7 ? "WEEKLY" : string.Format(CultureInfo.InvariantCulture, "{0} DAY", days);
                }

                return "USAGE";
            }
        }
    }

    internal sealed class ResetCredit
    {
        public string Title { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    internal sealed class UsageSnapshot
    {
        public UsageWindow WeeklyWindow { get; set; }
        public UsageWindow SessionWindow { get; set; }
        public DateTime FetchedAtUtc { get; set; }
        public string PlanType { get; set; }
        public bool OrdinaryUsageAllowed { get; set; }
        public int ResetCredits { get; set; }
        public List<ResetCredit> ResetCreditDetails { get; set; }
    }

    internal sealed class CodexUsageService : IUsageService
    {
        private readonly object _gate = new object();
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private Process _process;
        private bool _initialized;
        private bool _disposing;
        private int _nextRequestId = 200;
        private string _lastStandardError;

        public event Action<UsageSnapshot> SnapshotReceived;
        public event Action<string> ServiceError;

        public void Start()
        {
            lock (_gate)
            {
                StartProcessLocked();
            }
        }

        public void RequestUsage()
        {
            lock (_gate)
            {
                if (_process == null || SafeHasExited(_process))
                {
                    StartProcessLocked();
                    return;
                }

                if (!_initialized)
                {
                    return;
                }

                SendLocked(new Dictionary<string, object>
                {
                    { "method", "account/rateLimits/read" },
                    { "id", _nextRequestId++ },
                    { "params", new Dictionary<string, object>() }
                });
            }
        }

        private void StartProcessLocked()
        {
            if (_disposing)
            {
                return;
            }

            if (_process != null && !SafeHasExited(_process))
            {
                return;
            }

            _initialized = false;
            _lastStandardError = null;

            string executable = FindCodexExecutable();
            if (string.IsNullOrEmpty(executable))
            {
                RaiseError("Codex is not installed, so usage cannot be read.");
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "app-server --stdio",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };

                Process process = new Process();
                process.StartInfo = startInfo;
                process.EnableRaisingEvents = true;
                process.OutputDataReceived += OnOutputDataReceived;
                process.ErrorDataReceived += OnErrorDataReceived;
                process.Exited += OnProcessExited;

                if (!process.Start())
                {
                    RaiseError("Codex app-server could not be started.");
                    process.Dispose();
                    return;
                }

                _process = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                SendLocked(new Dictionary<string, object>
                {
                    { "method", "initialize" },
                    { "id", 100 },
                    {
                        "params", new Dictionary<string, object>
                        {
                            {
                                "clientInfo", new Dictionary<string, object>
                                {
                                    { "name", "agent_usage_bar" },
                                    { "title", "Agent Usage Bar" },
                                    { "version", "2.0.0" }
                                }
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                RaiseError("Could not connect to Codex: " + FriendlyMessage(ex));
                DisposeProcessLocked();
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data) || _disposing)
            {
                return;
            }

            try
            {
                Dictionary<string, object> root = _json.DeserializeObject(e.Data) as Dictionary<string, object>;
                if (root == null)
                {
                    return;
                }

                object errorValue;
                if (root.TryGetValue("error", out errorValue))
                {
                    Dictionary<string, object> error = errorValue as Dictionary<string, object>;
                    object messageValue;
                    string message = error != null && error.TryGetValue("message", out messageValue)
                        ? Convert.ToString(messageValue, CultureInfo.InvariantCulture)
                        : "The Codex usage request failed.";
                    RaiseError("Codex could not read usage: " + message);
                    return;
                }

                object idValue;
                if (root.TryGetValue("id", out idValue) && ConvertToInt(idValue) == 100)
                {
                    lock (_gate)
                    {
                        if (_process == null || SafeHasExited(_process))
                        {
                            return;
                        }

                        _initialized = true;
                        SendLocked(new Dictionary<string, object>
                        {
                            { "method", "initialized" },
                            { "params", new Dictionary<string, object>() }
                        });
                    }

                    RequestUsage();
                    return;
                }

                object resultValue;
                if (root.TryGetValue("result", out resultValue))
                {
                    Dictionary<string, object> result = resultValue as Dictionary<string, object>;
                    if (result != null && result.ContainsKey("rateLimits"))
                    {
                        UsageSnapshot snapshot = ParseSnapshot(result);
                        if (snapshot != null)
                        {
                            RaiseSnapshot(snapshot);
                        }
                    }
                    return;
                }

                object methodValue;
                if (root.TryGetValue("method", out methodValue) &&
                    string.Equals(Convert.ToString(methodValue, CultureInfo.InvariantCulture), "account/rateLimits/updated", StringComparison.Ordinal))
                {
                    object paramsValue;
                    if (root.TryGetValue("params", out paramsValue))
                    {
                        Dictionary<string, object> update = paramsValue as Dictionary<string, object>;
                        if (update != null && update.ContainsKey("rateLimits"))
                        {
                            UsageSnapshot snapshot = ParseSnapshot(update);
                            if (snapshot != null)
                            {
                                RaiseSnapshot(snapshot);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseError("Codex returned usage data in an unexpected format: " + FriendlyMessage(ex));
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                _lastStandardError = e.Data.Trim();
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            if (_disposing)
            {
                return;
            }

            lock (_gate)
            {
                _initialized = false;
            }

            string detail = string.IsNullOrEmpty(_lastStandardError)
                ? "The Codex usage service stopped. It will reconnect automatically."
                : "The Codex usage service stopped: " + _lastStandardError;
            RaiseError(detail);
        }

        private UsageSnapshot ParseSnapshot(Dictionary<string, object> result)
        {
            Dictionary<string, object> bucket = null;
            object byIdValue;
            if (result.TryGetValue("rateLimitsByLimitId", out byIdValue))
            {
                Dictionary<string, object> byId = byIdValue as Dictionary<string, object>;
                if (byId != null)
                {
                    object codexValue;
                    if (byId.TryGetValue("codex", out codexValue))
                    {
                        bucket = codexValue as Dictionary<string, object>;
                    }
                }
            }

            if (bucket == null)
            {
                object rateLimitsValue;
                if (result.TryGetValue("rateLimits", out rateLimitsValue))
                {
                    bucket = rateLimitsValue as Dictionary<string, object>;
                }
            }

            if (bucket == null)
            {
                return null;
            }

            List<UsageWindow> windows = new List<UsageWindow>();
            AddWindow(bucket, "primary", windows);
            AddWindow(bucket, "secondary", windows);

            UsageWindow weeklyWindow = windows
                .Where(delegate(UsageWindow window) { return window.DurationMinutes > 720; })
                .OrderBy(delegate(UsageWindow window) { return Math.Abs(window.DurationMinutes - 10080); })
                .FirstOrDefault();

            object planValue;
            string plan = bucket.TryGetValue("planType", out planValue)
                ? Convert.ToString(planValue, CultureInfo.InvariantCulture)
                : null;

            bool allowed = true;
            object allowedValue;
            if (result.TryGetValue("ordinaryUsageAllowed", out allowedValue))
            {
                try { allowed = Convert.ToBoolean(allowedValue, CultureInfo.InvariantCulture); }
                catch { allowed = true; }
            }

            int resetCredits = 0;
            List<ResetCredit> resetCreditDetails = new List<ResetCredit>();
            object resetCreditsValue;
            if (result.TryGetValue("rateLimitResetCredits", out resetCreditsValue))
            {
                Dictionary<string, object> resetInfo = resetCreditsValue as Dictionary<string, object>;
                object countValue;
                if (resetInfo != null && resetInfo.TryGetValue("availableCount", out countValue))
                {
                    resetCredits = ConvertToInt(countValue);
                }

                object creditsValue;
                if (resetInfo != null && resetInfo.TryGetValue("credits", out creditsValue))
                {
                    System.Collections.IEnumerable creditItems = creditsValue as System.Collections.IEnumerable;
                    if (creditItems != null)
                    {
                        foreach (object creditValue in creditItems)
                        {
                            Dictionary<string, object> credit = creditValue as Dictionary<string, object>;
                            if (credit == null)
                            {
                                continue;
                            }

                            object statusValue;
                            string status = credit.TryGetValue("status", out statusValue)
                                ? Convert.ToString(statusValue, CultureInfo.InvariantCulture)
                                : null;
                            if (!string.IsNullOrWhiteSpace(status) &&
                                !string.Equals(status, "available", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            object expiresValue;
                            if (!credit.TryGetValue("expiresAt", out expiresValue))
                            {
                                continue;
                            }

                            long expiresSeconds = ConvertToLong(expiresValue);
                            if (expiresSeconds <= 0)
                            {
                                continue;
                            }

                            object titleValue;
                            string title = credit.TryGetValue("title", out titleValue)
                                ? Convert.ToString(titleValue, CultureInfo.InvariantCulture)
                                : null;
                            try
                            {
                                resetCreditDetails.Add(new ResetCredit
                                {
                                    Title = string.IsNullOrWhiteSpace(title) ? "Banked reset" : title.Trim(),
                                    ExpiresAtUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(expiresSeconds)
                                });
                            }
                            catch (ArgumentOutOfRangeException)
                            {
                                // Ignore malformed expiry timestamps without discarding the usage snapshot.
                            }
                        }
                    }
                }
            }

            resetCreditDetails = resetCreditDetails.OrderBy(delegate(ResetCredit credit) { return credit.ExpiresAtUtc; }).ToList();
            if (resetCredits <= 0 && resetCreditDetails.Count > 0)
            {
                resetCredits = resetCreditDetails.Count;
            }

            return new UsageSnapshot
            {
                WeeklyWindow = weeklyWindow,
                FetchedAtUtc = DateTime.UtcNow,
                PlanType = string.IsNullOrWhiteSpace(plan) ? null : plan,
                OrdinaryUsageAllowed = allowed,
                ResetCredits = resetCredits,
                ResetCreditDetails = resetCreditDetails
            };
        }

        private static void AddWindow(Dictionary<string, object> bucket, string key, List<UsageWindow> windows)
        {
            object value;
            if (!bucket.TryGetValue(key, out value) || value == null)
            {
                return;
            }

            Dictionary<string, object> source = value as Dictionary<string, object>;
            if (source == null)
            {
                return;
            }

            object usedValue;
            object durationValue;
            object resetValue;
            if (!source.TryGetValue("usedPercent", out usedValue) ||
                !source.TryGetValue("windowDurationMins", out durationValue) ||
                !source.TryGetValue("resetsAt", out resetValue))
            {
                return;
            }

            double used = ConvertToDouble(usedValue);
            int duration = ConvertToInt(durationValue);
            long resetSeconds = ConvertToLong(resetValue);
            DateTime resetUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(resetSeconds);

            windows.Add(new UsageWindow
            {
                UsedPercent = used,
                DurationMinutes = duration,
                ResetAtUtc = resetUtc
            });
        }

        private void SendLocked(Dictionary<string, object> payload)
        {
            if (_process == null || SafeHasExited(_process))
            {
                return;
            }

            string line = _json.Serialize(payload);
            _process.StandardInput.WriteLine(line);
            _process.StandardInput.Flush();
        }

        private static string FindCodexExecutable()
        {
            string localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "Codex",
                "bin");

            try
            {
                if (Directory.Exists(localRoot))
                {
                    string newest = Directory.GetFiles(localRoot, "codex.exe", SearchOption.AllDirectories)
                        .OrderByDescending(delegate(string path) { return File.GetLastWriteTimeUtc(path); })
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(newest))
                    {
                        return newest;
                    }
                }
            }
            catch
            {
                // Fall through to PATH lookup.
            }

            return FindCodexOnPath(Environment.GetEnvironmentVariable("PATH"));
        }

        private static string FindCodexOnPath(string pathValue)
        {
            // Probe explicit local PATH entries without launching a search helper.
            // Relative entries and the current directory must not select a downloaded executable.
            foreach (string entry in (pathValue ?? string.Empty).Split(Path.PathSeparator))
            {
                string directory = entry.Trim().Trim('"');
                if (directory.Length < 3 || !char.IsLetter(directory[0]) || directory[1] != ':' ||
                    (directory[2] != '\\' && directory[2] != '/'))
                {
                    continue;
                }

                try
                {
                    string candidate = Path.GetFullPath(Path.Combine(directory, "codex.exe"));
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (IOException) { }
                catch (System.Security.SecurityException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        private static bool SafeHasExited(Process process)
        {
            try { return process.HasExited; }
            catch { return true; }
        }

        private void DisposeProcessLocked()
        {
            Process process = _process;
            _process = null;
            _initialized = false;
            if (process == null)
            {
                return;
            }

            try { process.OutputDataReceived -= OnOutputDataReceived; } catch { }
            try { process.ErrorDataReceived -= OnErrorDataReceived; } catch { }
            try { process.Exited -= OnProcessExited; } catch { }
            try { process.StandardInput.Close(); } catch { }
            try
            {
                if (!SafeHasExited(process))
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch { }
            try { process.Dispose(); } catch { }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposing = true;
                DisposeProcessLocked();
            }
        }

        private void RaiseSnapshot(UsageSnapshot snapshot)
        {
            Action<UsageSnapshot> handler = SnapshotReceived;
            if (handler != null)
            {
                handler(snapshot);
            }
        }

        private void RaiseError(string message)
        {
            Action<string> handler = ServiceError;
            if (handler != null)
            {
                handler(message);
            }
        }

        private static string FriendlyMessage(Exception exception)
        {
            return string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        }

        private static int ConvertToInt(object value)
        {
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static long ConvertToLong(object value)
        {
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static double ConvertToDouble(object value)
        {
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }
    }

    internal sealed class WidgetSettings
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentUsageBar");
        private string _settingsPath;

        public int? X { get; set; }
        public int? Y { get; set; }
        public bool TopMost { get; set; }
        public bool DarkMode { get; set; }
        public bool Visible { get; set; }

        public WidgetSettings()
        {
            TopMost = true;
            Visible = true;
        }

        public static WidgetSettings Load(UsageProvider provider)
        {
            WidgetSettings settings = new WidgetSettings();
            settings._settingsPath = Path.Combine(SettingsDirectory,
                provider == UsageProvider.Codex ? "codex-settings.txt" : "claude-settings.txt");
            try
            {
                string readPath = settings._settingsPath;
                if (!File.Exists(readPath) && provider == UsageProvider.Codex)
                {
                    readPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "CodexUsageBar", "settings.txt");
                }
                if (!File.Exists(readPath))
                {
                    return settings;
                }

                foreach (string rawLine in File.ReadAllLines(readPath))
                {
                    string line = rawLine.Trim();
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    int number;
                    bool flag;
                    if (key == "X" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) settings.X = number;
                    if (key == "Y" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) settings.Y = number;
                    if (key == "TopMost" && bool.TryParse(value, out flag)) settings.TopMost = flag;
                    if (key == "DarkMode" && bool.TryParse(value, out flag)) settings.DarkMode = flag;
                    if (key == "Visible" && bool.TryParse(value, out flag)) settings.Visible = flag;
                }
            }
            catch
            {
                // Invalid settings fall back to safe defaults.
            }
            return settings;
        }

        public void Save(Point location, bool topMost, bool darkMode)
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllLines(_settingsPath, new[]
                {
                    "X=" + location.X.ToString(CultureInfo.InvariantCulture),
                    "Y=" + location.Y.ToString(CultureInfo.InvariantCulture),
                    "TopMost=" + topMost.ToString(CultureInfo.InvariantCulture),
                    "DarkMode=" + darkMode.ToString(CultureInfo.InvariantCulture),
                    "Visible=" + Visible.ToString(CultureInfo.InvariantCulture)
                });
            }
            catch
            {
                // The widget remains useful even if position persistence is unavailable.
            }
        }
    }

    internal enum ResetExpiryUrgency
    {
        None,
        Soon,
        Urgent,
        Critical
    }

    internal sealed class UsageBarForm : Form
    {
        private static readonly Color LightBackgroundColor = Color.FromArgb(248, 247, 243);
        private static readonly Color LightSurfaceColor = Color.FromArgb(238, 237, 232);
        private static readonly Color LightBorderColor = Color.FromArgb(213, 211, 204);
        private static readonly Color LightPrimaryTextColor = Color.FromArgb(26, 26, 25);
        private static readonly Color LightMutedTextColor = Color.FromArgb(95, 94, 90);
        private static readonly Color LightQuietTextColor = Color.FromArgb(112, 110, 105);
        private static readonly Color DarkBackgroundColor = Color.FromArgb(23, 23, 23);
        private static readonly Color DarkSurfaceColor = Color.FromArgb(34, 34, 34);
        private static readonly Color DarkBorderColor = Color.FromArgb(59, 59, 59);
        private static readonly Color DarkPrimaryTextColor = Color.FromArgb(242, 242, 238);
        private static readonly Color DarkMutedTextColor = Color.FromArgb(181, 181, 175);
        private static readonly Color DarkQuietTextColor = Color.FromArgb(143, 143, 136);

        private readonly IUsageService _service;
        private readonly UsageProvider _provider;
        internal UsageBarForm OtherBar { get; set; }
        internal event Action ExitRequested;
        private bool _resourcesDisposed;
        private string ProviderName { get { return _provider == UsageProvider.Claude ? "Claude" : "Codex"; } }
        private UsageWindow DisplayWindow
        {
            get { return _snapshot == null ? null :
                (_provider == UsageProvider.Claude && _snapshot.SessionWindow != null ? _snapshot.SessionWindow : _snapshot.WeeklyWindow); }
        }
        private readonly WidgetSettings _settings;
        private readonly FormsTimer _refreshTimer;
        private readonly FormsTimer _clockTimer;
        private readonly FormsTimer _attentionTimer;
        private readonly FormsTimer _savePositionTimer;
        private readonly ToolTip _toolTip;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _topMostItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _darkModeItem;
        private readonly ToolStripMenuItem _visibilityItem;
        private readonly ToolStripMenuItem _copyItem;
        private readonly NotifyIcon _trayIcon;
        private readonly Icon _applicationIcon;
        private Icon _trayUsageIcon;
        private int _trayUsagePercent = int.MinValue;
        private int _trayUsageState = -1;

        private UsageSnapshot _snapshot;
        private string _serviceError;
        private bool _refreshing;
        private bool _refreshHover;
        private bool _allowExit;
        private bool _darkMode;
        private readonly bool _attentionMotionEnabled;
        private float _attentionPhase;
        private DateTime _refreshStartedAtUtc;

        private Color BackgroundColor { get { return _darkMode ? DarkBackgroundColor : LightBackgroundColor; } }
        private Color SurfaceColor { get { return _darkMode ? DarkSurfaceColor : LightSurfaceColor; } }
        private Color BorderColor { get { return _darkMode ? DarkBorderColor : LightBorderColor; } }
        private Color PrimaryTextColor { get { return _darkMode ? DarkPrimaryTextColor : LightPrimaryTextColor; } }
        private Color MutedTextColor { get { return _darkMode ? DarkMutedTextColor : LightMutedTextColor; } }
        private Color QuietTextColor { get { return _darkMode ? DarkQuietTextColor : LightQuietTextColor; } }
        private Color AccentColor { get { return _provider == UsageProvider.Claude
            ? (_darkMode ? Color.FromArgb(222, 160, 128) : Color.FromArgb(174, 89, 56))
            : (_darkMode ? Color.FromArgb(124, 156, 255) : Color.FromArgb(52, 101, 230)); } }
        private Color LiveColor { get { return _darkMode ? Color.FromArgb(91, 201, 149) : Color.FromArgb(38, 137, 90); } }
        private Color AmberColor { get { return _darkMode ? Color.FromArgb(227, 164, 74) : Color.FromArgb(173, 112, 17); } }
        private Color CriticalColor { get { return _darkMode ? Color.FromArgb(229, 107, 107) : Color.FromArgb(196, 65, 65); } }
        private Color RingTrackColor { get { return _darkMode ? Color.FromArgb(64, 64, 64) : Color.FromArgb(222, 220, 214); } }
        private Color HoverColor { get { return _darkMode ? Color.FromArgb(42, 48, 62) : Color.FromArgb(230, 235, 247); } }
        private Color HoverBorderColor { get { return _darkMode ? Color.FromArgb(88, 108, 164) : Color.FromArgb(164, 183, 230); } }

        private readonly Font _brandFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _statusFont = new Font("Segoe UI", 7.0f, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font _labelFont = new Font("Segoe UI", 6.75f, FontStyle.Bold, GraphicsUnit.Point);
        // Pixel-sized fonts keep the fixed 48 px ring stable across mixed-DPI monitors.
        private readonly Font _valueFont = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font _valueCompactFont = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font _refreshFont = new Font("Segoe UI Symbol", 13.0f, FontStyle.Regular, GraphicsUnit.Point);

        public UsageBarForm() : this(UsageProvider.Codex) { }

        public UsageBarForm(UsageProvider provider)
        {
            _provider = provider;
            _settings = WidgetSettings.Load(provider);
            _darkMode = _settings.DarkMode;
            _attentionMotionEnabled = SystemInformation.UIEffectsEnabled;
            _service = provider == UsageProvider.Claude ? (IUsageService)new ClaudeUsageService() : new CodexUsageService();

            Text = ProviderName + " | Agent Usage Bar";
            AccessibleName = ProviderName + " usage limits widget";
            AccessibleRole = AccessibleRole.Pane;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(276, 64);
            MinimumSize = MaximumSize = Size;
            BackColor = BackgroundColor;
            ForeColor = PrimaryTextColor;
            ShowInTaskbar = false;
            TopMost = _settings.TopMost;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _applicationIcon = CreateApplicationIcon();
            Icon = _applicationIcon;

            _menu = BuildMenu(out _topMostItem, out _startupItem, out _darkModeItem, out _visibilityItem, out _copyItem);
            ContextMenuStrip = _menu;

            _trayIcon = new NotifyIcon
            {
                Icon = _applicationIcon,
                Text = ProviderName + " usage - connecting",
                Visible = true,
                ContextMenuStrip = _menu
            };
            _trayIcon.DoubleClick += delegate { ToggleVisibility(); };

            _toolTip = new ToolTip
            {
                AutoPopDelay = 30000,
                InitialDelay = 350,
                ReshowDelay = 150,
                ShowAlways = true
            };
            _toolTip.SetToolTip(this, provider == UsageProvider.Claude ? "Waiting for Claude Code status-line data." : "Connecting to Codex...");

            _refreshTimer = new FormsTimer { Interval = provider == UsageProvider.Claude ? 2000 : 15000 };
            _refreshTimer.Tick += delegate
            {
                if (_provider == UsageProvider.Claude) _service.RequestUsage();
                else RefreshUsage();
            };
            _clockTimer = new FormsTimer { Interval = 1000 };
            _clockTimer.Tick += delegate
            {
                CheckRefreshTimeout();
                UpdateAttentionAnimationState();
                Invalidate();
                UpdateToolTip();
            };
            _attentionTimer = new FormsTimer { Interval = 100 };
            _attentionTimer.Tick += delegate
            {
                ResetExpiryUrgency urgency = GetResetExpiryUrgency();
                _attentionPhase += urgency == ResetExpiryUrgency.Critical ? 0.04f : 0.025f;
                if (_attentionPhase >= 1f) _attentionPhase -= 1f;
                Invalidate();
            };
            _savePositionTimer = new FormsTimer { Interval = 700 };
            _savePositionTimer.Tick += delegate
            {
                _savePositionTimer.Stop();
                _settings.Save(Location, TopMost, _darkMode);
            };

            _service.SnapshotReceived += OnSnapshotReceived;
            _service.ServiceError += OnServiceError;

            Shown += OnShown;
            VisibleChanged += delegate { UpdateAttentionAnimationState(); };
            Move += delegate
            {
                if (Visible && WindowState == FormWindowState.Normal)
                {
                    _savePositionTimer.Stop();
                    _savePositionTimer.Start();
                }
            };
            FormClosing += OnFormClosing;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F5)
                {
                    RefreshUsage();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    HideWidget();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
                {
                    _menu.Show(this, new Point(18, Height - 8));
                    e.Handled = true;
                }
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CsDropShadow = 0x00020000;
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.TryUseRoundedCorners(Handle);
            ApplyRoundedRegion();
        }

        private void OnShown(object sender, EventArgs e)
        {
            RestoreOrChooseLocation();
            _refreshTimer.Start();
            _clockTimer.Start();
            _refreshing = true;
            _refreshStartedAtUtc = DateTime.UtcNow;
            _service.Start();
            UpdateAttentionAnimationState();
            Invalidate();
        }

        private void OnSnapshotReceived(UsageSnapshot snapshot)
        {
            if (IsDisposed)
            {
                return;
            }

            Dispatch(delegate
            {
                _snapshot = snapshot;
                _serviceError = null;
                _refreshing = false;
                _copyItem.Enabled = true;
                UpdateAttentionAnimationState();
                UpdateToolTip();
                Invalidate();
            });
        }

        private void OnServiceError(string message)
        {
            if (IsDisposed)
            {
                return;
            }

            Dispatch(delegate
            {
                _serviceError = message;
                _refreshing = false;
                UpdateToolTip();
                Invalidate();
            });
        }

        private void Dispatch(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (!InvokeRequired) { action(); return; }
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }

        private void RefreshUsage()
        {
            _refreshing = _provider != UsageProvider.Claude;
            _refreshStartedAtUtc = DateTime.UtcNow;
            _serviceError = null;
            _service.RequestUsage();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            RectangleF backgroundBounds = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
            using (GraphicsPath backgroundPath = RoundedRectangle(backgroundBounds, 10f))
            using (SolidBrush backgroundBrush = new SolidBrush(BackgroundColor))
            using (Pen borderPen = new Pen(BorderColor, 1f))
            {
                graphics.FillPath(backgroundBrush, backgroundPath);
                graphics.DrawPath(borderPen, backgroundPath);
            }

            UsageWindow weeklyWindow = DisplayWindow;
            DrawPercentageRing(graphics, new RectangleF(10, 8, 48, 48), weeklyWindow);
            DrawWeeklyLimit(graphics, new Rectangle(70, 0, 142, ClientSize.Height), weeklyWindow);
            DrawDivider(graphics, 220);
            DrawRefreshButton(graphics);
            DrawResetExpiryAttention(graphics, backgroundBounds);
        }

        private void DrawResetExpiryAttention(Graphics graphics, RectangleF bounds)
        {
            ResetExpiryUrgency urgency = GetResetExpiryUrgency();
            if (urgency == ResetExpiryUrgency.None)
            {
                return;
            }

            Color attentionColor = urgency == ResetExpiryUrgency.Critical
                ? CriticalColor
                : (_darkMode ? Color.FromArgb(244, 157, 72) : Color.FromArgb(210, 99, 23));
            float pulse = urgency == ResetExpiryUrgency.Soon || !_attentionMotionEnabled
                ? 0.5f
                : (float)((Math.Sin(_attentionPhase * Math.PI * 2d) + 1d) / 2d);
            int borderAlpha = urgency == ResetExpiryUrgency.Soon ? 145 : 155 + (int)(65f * pulse);

            RectangleF borderBounds = new RectangleF(bounds.X + 1f, bounds.Y + 1f, bounds.Width - 2f, bounds.Height - 2f);
            using (GraphicsPath borderPath = RoundedRectangle(borderBounds, 9f))
            using (Pen attentionPen = new Pen(Color.FromArgb(borderAlpha, attentionColor), urgency == ResetExpiryUrgency.Soon ? 1.35f : 1.75f))
            {
                graphics.DrawPath(attentionPen, borderPath);
            }

            if (urgency == ResetExpiryUrgency.Soon || !_attentionMotionEnabled)
            {
                return;
            }

            RectangleF orbitBounds = new RectangleF(bounds.X + 3f, bounds.Y + 3f, bounds.Width - 6f, bounds.Height - 6f);
            using (GraphicsPath orbitPath = RoundedRectangle(orbitBounds, 7f))
            {
                orbitPath.Flatten();
                PointF[] points = orbitPath.PathPoints;
                for (int trail = 3; trail >= 0; trail--)
                {
                    float progress = _attentionPhase - (trail * 0.012f);
                    if (progress < 0f) progress += 1f;
                    PointF point = PointAlongClosedPath(points, progress);
                    float size = 2.2f + ((3 - trail) * 0.95f);
                    int alpha = 48 + ((3 - trail) * 48);
                    using (SolidBrush ember = new SolidBrush(Color.FromArgb(alpha, attentionColor)))
                    {
                        graphics.FillEllipse(ember, point.X - size / 2f, point.Y - size / 2f, size, size);
                    }
                }
            }
        }

        private static PointF PointAlongClosedPath(PointF[] points, float progress)
        {
            if (points == null || points.Length == 0)
            {
                return PointF.Empty;
            }
            if (points.Length == 1)
            {
                return points[0];
            }

            float totalLength = 0f;
            for (int index = 0; index < points.Length; index++)
            {
                PointF start = points[index];
                PointF end = points[(index + 1) % points.Length];
                totalLength += Distance(start, end);
            }

            float target = Math.Max(0f, Math.Min(1f, progress)) * totalLength;
            float travelled = 0f;
            for (int index = 0; index < points.Length; index++)
            {
                PointF start = points[index];
                PointF end = points[(index + 1) % points.Length];
                float segment = Distance(start, end);
                if (travelled + segment >= target && segment > 0f)
                {
                    float ratio = (target - travelled) / segment;
                    return new PointF(start.X + ((end.X - start.X) * ratio), start.Y + ((end.Y - start.Y) * ratio));
                }
                travelled += segment;
            }
            return points[0];
        }

        private static float Distance(PointF first, PointF second)
        {
            float x = second.X - first.X;
            float y = second.Y - first.Y;
            return (float)Math.Sqrt((x * x) + (y * y));
        }

        private void DrawPercentageRing(Graphics graphics, RectangleF bounds, UsageWindow window)
        {
            RectangleF arcBounds = new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4);
            using (Pen trackPen = new Pen(RingTrackColor, 4f))
            {
                trackPen.StartCap = LineCap.Round;
                trackPen.EndCap = LineCap.Round;
                graphics.DrawArc(trackPen, arcBounds, -90f, 359.9f);
            }

            string valueText = "—";
            Color signal = QuietTextColor;
            if (window != null)
            {
                double percent = Math.Max(0, Math.Min(100, window.RemainingPercent));
                signal = SignalColor(percent);
                valueText = Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
                if (percent > 0)
                {
                    using (Pen signalPen = new Pen(signal, 4f))
                    {
                        signalPen.StartCap = LineCap.Round;
                        signalPen.EndCap = LineCap.Round;
                        graphics.DrawArc(signalPen, arcBounds, -90f, (float)Math.Min(359.9, percent * 3.6));
                    }
                }
            }

            Font valueFont = valueText.Length >= 4 ? _valueCompactFont : _valueFont;
            Rectangle valueBounds = Rectangle.Round(bounds);
            TextFormatFlags valueFlags = TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(
                graphics,
                valueText,
                valueFont,
                valueBounds,
                window == null ? QuietTextColor : PrimaryTextColor,
                valueFlags);
        }

        private void DrawWeeklyLimit(Graphics graphics, Rectangle bounds, UsageWindow window)
        {
            Color statusColor = GetStatusColor();
            Color statusTextColor = statusColor == LiveColor ? MutedTextColor : statusColor;
            TextFormatFlags textFlags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            ResetCredit expiringCredit;
            TimeSpan creditRemaining;
            ResetExpiryUrgency urgency = GetResetExpiryUrgency(out expiringCredit, out creditRemaining);
            bool showCreditExpiry = urgency != ResetExpiryUrgency.None;
            string resetText = showCreditExpiry
                ? (urgency == ResetExpiryUrgency.Soon ? "⌛ " : "🔥 ") + "Reset expires " + FormatCountdownCompact(creditRemaining)
                : (window == null
                    ? (_refreshing ? "Checking usage" : "Not available")
                    : FormatResetCompact(window.ResetAtUtc));
            if (_provider == UsageProvider.Claude && _snapshot != null &&
                _snapshot.SessionWindow != null && _snapshot.WeeklyWindow != null)
            {
                resetText = string.Format(CultureInfo.InvariantCulture, "Weekly {0:0}% left", _snapshot.WeeklyWindow.RemainingPercent);
            }
            Color resetTextColor = showCreditExpiry
                ? (urgency == ResetExpiryUrgency.Critical ? CriticalColor : AmberColor)
                : (window == null ? QuietTextColor : MutedTextColor);

            TextRenderer.DrawText(graphics, ProviderName.ToUpperInvariant(), _labelFont, new Point(bounds.Left, 7), AccentColor, textFlags);
            using (SolidBrush statusDotBrush = new SolidBrush(statusColor))
            {
                graphics.FillEllipse(statusDotBrush, bounds.Left + 41, 10, 4, 4);
            }
            TextRenderer.DrawText(graphics, GetFreshnessText(), _statusFont, new Point(bounds.Left + 50, 6), statusTextColor, textFlags);
            string periodLabel = _provider == UsageProvider.Claude && window != null && window.DurationMinutes == 300
                ? "5-hour remaining" : "Weekly remaining";
            TextRenderer.DrawText(graphics, periodLabel, _brandFont, new Point(bounds.Left, 22), PrimaryTextColor, textFlags);
            TextRenderer.DrawText(
                graphics,
                resetText,
                _statusFont,
                new Rectangle(bounds.Left, 42, bounds.Width, 15),
                resetTextColor,
                textFlags | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
        }

        private void DrawDivider(Graphics graphics, int x)
        {
            using (Pen divider = new Pen(BorderColor, 1f))
            {
                graphics.DrawLine(divider, x, 13, x, ClientSize.Height - 13);
            }
        }

        private void DrawRefreshButton(Graphics graphics)
        {
            Rectangle rect = RefreshRectangle;
            if (_refreshHover)
            {
                using (GraphicsPath path = RoundedRectangle(rect, 7f))
                using (SolidBrush brush = new SolidBrush(HoverColor))
                using (Pen pen = new Pen(HoverBorderColor, 1f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(pen, path);
                }
            }

            string glyph = _refreshing ? "···" : "↻";
            Font font = _refreshing ? _statusFont : _refreshFont;
            SizeF size = graphics.MeasureString(glyph, font);
            using (SolidBrush brush = new SolidBrush(_refreshing || _refreshHover ? AccentColor : MutedTextColor))
            {
                graphics.DrawString(glyph, font, brush,
                    rect.Left + (rect.Width - size.Width) / 2f,
                    rect.Top + (rect.Height - size.Height) / 2f - (_refreshing ? 0f : 1f));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                return;
            }

            if (e.Button == MouseButtons.Left && RefreshRectangle.Contains(e.Location))
            {
                RefreshUsage();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WmNcLButtonDown, new IntPtr(NativeMethods.HtCaption), IntPtr.Zero);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = RefreshRectangle.Contains(e.Location);
            if (hover != _refreshHover)
            {
                _refreshHover = hover;
                Cursor = hover ? Cursors.Hand : Cursors.SizeAll;
                Invalidate(RefreshRectangle);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_refreshHover)
            {
                _refreshHover = false;
                Cursor = Cursors.Default;
                Invalidate(RefreshRectangle);
            }
        }

        private Rectangle RefreshRectangle
        {
            get { return new Rectangle(231, 16, 34, 32); }
        }

        private ContextMenuStrip BuildMenu(
            out ToolStripMenuItem topMostItem,
            out ToolStripMenuItem startupItem,
            out ToolStripMenuItem darkModeItem,
            out ToolStripMenuItem visibilityItem,
            out ToolStripMenuItem copyItem)
        {
            ContextMenuStrip menu = new ContextMenuStrip
            {
                BackColor = SurfaceColor,
                ForeColor = PrimaryTextColor,
                Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point),
                Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(_darkMode)),
                ShowImageMargin = false,
                ShowCheckMargin = true,
                Padding = new Padding(4)
            };

            ToolStripMenuItem refreshItem = new ToolStripMenuItem("Refresh now");
            refreshItem.ShortcutKeyDisplayString = "F5";
            refreshItem.Click += delegate { RefreshUsage(); };

            ToolStripMenuItem copyLocal = new ToolStripMenuItem("Copy usage summary");
            copyLocal.Enabled = false;
            copyLocal.Click += delegate { CopyUsageSummary(); };

            ToolStripMenuItem topMostLocal = new ToolStripMenuItem("Always on top");
            topMostLocal.Checked = TopMost;
            topMostLocal.CheckOnClick = true;
            topMostLocal.Click += delegate
            {
                TopMost = topMostLocal.Checked;
                _settings.Save(Location, TopMost, _darkMode);
            };

            ToolStripMenuItem startupLocal = new ToolStripMenuItem("Start with Windows");
            startupLocal.Checked = StartupManager.IsEnabled();
            startupLocal.CheckOnClick = true;
            startupLocal.Click += delegate
            {
                bool desired = startupLocal.Checked;
                string error;
                if (!StartupManager.SetEnabled(desired, out error))
                {
                    startupLocal.Checked = !desired;
                    MessageBox.Show(error, "Codex Usage Bar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            ToolStripMenuItem darkModeLocal = new ToolStripMenuItem("Dark mode");
            darkModeLocal.Checked = _darkMode;
            darkModeLocal.CheckOnClick = true;
            darkModeLocal.Click += delegate
            {
                _darkMode = darkModeLocal.Checked;
                ApplyTheme();
                _settings.Save(Location, TopMost, _darkMode);
            };

            ToolStripMenuItem resetPositionItem = new ToolStripMenuItem("Move to top-right");
            resetPositionItem.Click += delegate { MoveToTopRight(); };

            ToolStripMenuItem visibilityLocal = new ToolStripMenuItem("Hide widget");
            visibilityLocal.Click += delegate { ToggleVisibility(); };

            ToolStripMenuItem otherItem = new ToolStripMenuItem("Show other bar");
            otherItem.Click += delegate { if (OtherBar != null) OtherBar.ToggleVisibility(); };
            ToolStripMenuItem setupItem = new ToolStripMenuItem("Claude setup instructions");
            setupItem.Click += delegate
            {
                MessageBox.Show(
                    "From the project folder, run scripts\\configure-claude.ps1 in PowerShell.\n\n" +
                    "This connects Claude Code's status line to the Claude bar. Existing custom status lines are preserved.\n\n" +
                    "Claude values arrive after a normal Claude Code response. No extra model request is made by the widget. " +
                    "See docs\\claude-setup.md for details.", "Connect Claude Code", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            ToolStripMenuItem quitItem = new ToolStripMenuItem("Quit Agent Usage Bar");
            quitItem.Click += delegate
            {
                if (ExitRequested != null) ExitRequested();
                else ExitWidget();
            };

            menu.Items.Add(refreshItem);
            menu.Items.Add(copyLocal);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(topMostLocal);
            menu.Items.Add(darkModeLocal);
            menu.Items.Add(startupLocal);
            menu.Items.Add(resetPositionItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(visibilityLocal);
            menu.Items.Add(otherItem);
            menu.Items.Add(setupItem);
            menu.Items.Add(quitItem);
            menu.Opening += delegate
            {
                visibilityLocal.Text = Visible ? "Hide widget" : "Show widget";
                topMostLocal.Checked = TopMost;
                darkModeLocal.Checked = _darkMode;
                startupLocal.Checked = StartupManager.IsEnabled();
                otherItem.Enabled = OtherBar != null;
                if (OtherBar != null) otherItem.Text = (OtherBar.Visible ? "Hide " : "Show ") + OtherBar.ProviderName + " bar";
            };

            topMostItem = topMostLocal;
            startupItem = startupLocal;
            darkModeItem = darkModeLocal;
            visibilityItem = visibilityLocal;
            copyItem = copyLocal;
            return menu;
        }

        private void ApplyTheme()
        {
            BackColor = BackgroundColor;
            ForeColor = PrimaryTextColor;
            if (_menu != null)
            {
                _menu.BackColor = SurfaceColor;
                _menu.ForeColor = PrimaryTextColor;
                _menu.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(_darkMode));
            }
            Invalidate();
        }

        private void ToggleVisibility()
        {
            if (Visible)
            {
                HideWidget();
            }
            else
            {
                ShowWidget();
            }
        }

        private void CheckRefreshTimeout()
        {
            if (_refreshing && _refreshStartedAtUtc != default(DateTime) &&
                DateTime.UtcNow - _refreshStartedAtUtc > TimeSpan.FromSeconds(20))
            {
                _refreshing = false;
                _serviceError = "Codex did not answer within 20 seconds. The widget will retry automatically.";
            }
        }

        private void HideWidget()
        {
            Hide();
            _settings.Visible = false;
            _settings.Save(Location, TopMost, _darkMode);
        }

        private void ShowWidget()
        {
            _settings.Visible = true;
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            if (TopMost)
            {
                TopMost = false;
                TopMost = true;
            }
            _settings.Save(Location, TopMost, _darkMode);
        }

        internal void ShowInitially()
        {
            bool show = _settings.Visible;
            Show();
            if (!show) Hide();
        }

        internal void ExitWidget()
        {
            _allowExit = true;
            Close();
        }

        private void CopyUsageSummary()
        {
            if (_snapshot == null)
            {
                return;
            }

            List<string> parts = new List<string>();
            if (_snapshot.SessionWindow != null) parts.Add(FormatSummaryWindow(_snapshot.SessionWindow));
            if (_snapshot.WeeklyWindow != null)
            {
                parts.Add(FormatSummaryWindow(_snapshot.WeeklyWindow));
            }
            else
            {
                parts.Add("weekly limit not available");
            }
            if (_snapshot.ResetCredits > 0)
            {
                ResetCredit nextCredit = _snapshot.ResetCreditDetails == null
                    ? null
                    : _snapshot.ResetCreditDetails.OrderBy(delegate(ResetCredit credit) { return credit.ExpiresAtUtc; }).FirstOrDefault();
                parts.Add(nextCredit == null
                    ? string.Format(CultureInfo.InvariantCulture, "{0} banked reset(s)", _snapshot.ResetCredits)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        "{0} banked reset(s); next expires {1}",
                        _snapshot.ResetCredits,
                        FormatLocalExpiry(nextCredit.ExpiresAtUtc)));
            }
            parts.Add("updated " + FormatFreshness(_snapshot.FetchedAtUtc));
            Clipboard.SetText(ProviderName + " usage: " + string.Join("; ", parts.ToArray()));
        }

        private static string FormatSummaryWindow(UsageWindow window)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1:0}% left (resets {2})",
                window.Label.ToLowerInvariant(),
                window.RemainingPercent,
                window.ResetAtUtc.ToLocalTime().ToString("ddd d MMM h:mm tt", CultureInfo.CurrentCulture));
        }

        private void RestoreOrChooseLocation()
        {
            if (_settings.X.HasValue && _settings.Y.HasValue)
            {
                Point requested = new Point(_settings.X.Value, _settings.Y.Value);
                Rectangle widgetRect = new Rectangle(requested, Size);
                bool visible = Screen.AllScreens.Any(delegate(Screen screen)
                {
                    Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, widgetRect);
                    return intersection.Width >= 80 && intersection.Height >= 40;
                });
                if (visible)
                {
                    Location = requested;
                    return;
                }
            }
            MoveToTopRight();
        }

        private void MoveToTopRight()
        {
            Screen screen = Screen.PrimaryScreen;
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Right - Width - 22, area.Top + 22 + (_provider == UsageProvider.Claude ? Height + 12 : 0));
            _settings.Save(Location, TopMost, _darkMode);
        }

        private string GetFreshnessText()
        {
            if (_snapshot == null)
            {
                return _serviceError == null ? "Connecting" : (_provider == UsageProvider.Claude ? "Awaiting data" : "Unavailable");
            }

            if (_serviceError != null)
            {
                return "Stale · retry";
            }

            TimeSpan age = DateTime.UtcNow - _snapshot.FetchedAtUtc;
            if (_provider == UsageProvider.Claude)
            {
                if (age.TotalMinutes < 1) return "Seen now";
                if (age.TotalHours < 1) return string.Format(CultureInfo.InvariantCulture, "Seen {0}m ago", (int)age.TotalMinutes);
                return string.Format(CultureInfo.InvariantCulture, "Seen {0}h ago", (int)age.TotalHours);
            }
            if (age.TotalSeconds < 10) return "Updated now";
            if (age.TotalMinutes < 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "Updated {0}s", Math.Max(1, (int)age.TotalSeconds));
            }
            return string.Format(CultureInfo.InvariantCulture, "Updated {0}m", Math.Max(1, (int)age.TotalMinutes));
        }

        private Color GetStatusColor()
        {
            if (_snapshot == null && _serviceError != null)
            {
                return _provider == UsageProvider.Claude ? QuietTextColor : CriticalColor;
            }
            if (_serviceError != null || (_snapshot != null && DateTime.UtcNow - _snapshot.FetchedAtUtc > TimeSpan.FromMinutes(3)))
            {
                return AmberColor;
            }
            return LiveColor;
        }

        private void UpdateAttentionAnimationState()
        {
            ResetExpiryUrgency urgency = GetResetExpiryUrgency();
            bool shouldAnimate = Visible && _attentionMotionEnabled &&
                (urgency == ResetExpiryUrgency.Urgent || urgency == ResetExpiryUrgency.Critical);
            if (shouldAnimate)
            {
                if (!_attentionTimer.Enabled)
                {
                    _attentionTimer.Start();
                }
            }
            else if (_attentionTimer.Enabled)
            {
                _attentionTimer.Stop();
                _attentionPhase = 0f;
            }
        }

        private ResetExpiryUrgency GetResetExpiryUrgency()
        {
            ResetCredit credit;
            TimeSpan remaining;
            return GetResetExpiryUrgency(out credit, out remaining);
        }

        private ResetExpiryUrgency GetResetExpiryUrgency(out ResetCredit credit, out TimeSpan remaining)
        {
            credit = null;
            remaining = TimeSpan.MaxValue;
            if (_snapshot == null || _snapshot.ResetCreditDetails == null || _snapshot.ResetCreditDetails.Count == 0)
            {
                return ResetExpiryUrgency.None;
            }

            credit = _snapshot.ResetCreditDetails.OrderBy(delegate(ResetCredit item) { return item.ExpiresAtUtc; }).FirstOrDefault();
            if (credit == null)
            {
                return ResetExpiryUrgency.None;
            }

            remaining = credit.ExpiresAtUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.FromHours(1)) return ResetExpiryUrgency.Critical;
            if (remaining <= TimeSpan.FromHours(24)) return ResetExpiryUrgency.Urgent;
            if (remaining <= TimeSpan.FromHours(72)) return ResetExpiryUrgency.Soon;
            return ResetExpiryUrgency.None;
        }

        private Color SignalColor(double remainingPercent)
        {
            if (remainingPercent <= 10) return CriticalColor;
            if (remainingPercent <= 25) return AmberColor;
            return AccentColor;
        }

        private static string FormatResetCompact(DateTime resetUtc)
        {
            TimeSpan remaining = resetUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "Resets now";
            }
            if (remaining.TotalMinutes < 1)
            {
                return "Resets <1m";
            }
            if (remaining.TotalHours < 24)
            {
                return string.Format(CultureInfo.InvariantCulture, "Resets in {0}h {1}m", (int)remaining.TotalHours, remaining.Minutes);
            }
            return string.Format(CultureInfo.InvariantCulture, "Resets in {0}d {1}h", (int)remaining.TotalDays, remaining.Hours);
        }

        private static string FormatCountdownCompact(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero) return "now";
            if (remaining.TotalMinutes < 10)
            {
                return string.Format(CultureInfo.InvariantCulture, "in {0}m {1}s", Math.Max(0, remaining.Minutes), Math.Max(0, remaining.Seconds));
            }
            if (remaining.TotalHours < 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "in {0}m", Math.Max(1, (int)remaining.TotalMinutes));
            }
            if (remaining.TotalHours < 24)
            {
                return string.Format(CultureInfo.InvariantCulture, "in {0}h {1}m", (int)remaining.TotalHours, remaining.Minutes);
            }
            return string.Format(CultureInfo.InvariantCulture, "in {0}d {1}h", (int)remaining.TotalDays, remaining.Hours);
        }

        private static string FormatCountdownDetailed(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero) return "expiry reached; awaiting refresh";
            if (remaining.TotalHours < 1)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}m {1}s remaining",
                    Math.Max(0, remaining.Minutes),
                    Math.Max(0, remaining.Seconds));
            }
            if (remaining.TotalDays < 1)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}h {1}m remaining",
                    (int)remaining.TotalHours,
                    remaining.Minutes);
            }
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}d {1}h {2}m remaining",
                (int)remaining.TotalDays,
                remaining.Hours,
                remaining.Minutes);
        }

        private static string FormatLocalExpiry(DateTime expiresAtUtc)
        {
            DateTime local = expiresAtUtc.ToLocalTime();
            return local.ToString("dddd, d MMMM yyyy 'at' h:mm:ss tt", CultureInfo.CurrentCulture) +
                " (UTC" + local.ToString("zzz", CultureInfo.InvariantCulture) + ")";
        }

        private static string FormatFreshness(DateTime fetchedAtUtc)
        {
            TimeSpan age = DateTime.UtcNow - fetchedAtUtc;
            if (age.TotalSeconds < 10) return "updated now";
            if (age.TotalMinutes < 1) return string.Format(CultureInfo.InvariantCulture, "updated {0}s ago", Math.Max(1, (int)age.TotalSeconds));
            return string.Format(CultureInfo.InvariantCulture, "updated {0}m ago", Math.Max(1, (int)age.TotalMinutes));
        }

        private void UpdateToolTip()
        {
            string text;
            if (_snapshot == null)
            {
                text = _serviceError ?? ("Connecting to " + ProviderName + "...");
            }
            else
            {
                List<string> lines = new List<string>();
                lines.Add(ProviderName + " usage");
                if (_snapshot.SessionWindow != null) lines.Add(FormatDetailedWindow(_snapshot.SessionWindow));
                if (_snapshot.WeeklyWindow != null)
                {
                    lines.Add(FormatDetailedWindow(_snapshot.WeeklyWindow));
                }
                else
                {
                    lines.Add("Weekly limit: not currently reported by " + ProviderName);
                }
                if (_snapshot.ResetCredits > 0)
                {
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "Banked resets: {0}", _snapshot.ResetCredits));
                    int detailCount = _snapshot.ResetCreditDetails == null ? 0 : _snapshot.ResetCreditDetails.Count;
                    for (int index = 0; index < detailCount; index++)
                    {
                        ResetCredit credit = _snapshot.ResetCreditDetails[index];
                        lines.Add(string.Format(
                            CultureInfo.CurrentCulture,
                            "Reset {0} expires: {1}",
                            index + 1,
                            FormatLocalExpiry(credit.ExpiresAtUtc)));
                        lines.Add("  " + FormatCountdownDetailed(credit.ExpiresAtUtc - DateTime.UtcNow));
                    }
                    if (detailCount < _snapshot.ResetCredits)
                    {
                        lines.Add(string.Format(
                            CultureInfo.InvariantCulture,
                            "Expiry time unavailable for {0} reset(s)",
                            _snapshot.ResetCredits - detailCount));
                    }
                }
                if (!string.IsNullOrWhiteSpace(_snapshot.PlanType))
                {
                    lines.Add("Plan: " + _snapshot.PlanType);
                }
                if (_provider == UsageProvider.Claude)
                {
                    lines.Add("Source: Claude Code status line");
                    lines.Add("Last received: " + FormatLocalExpiry(_snapshot.FetchedAtUtc));
                    lines.Add("Updates when Claude Code reports usage. Refresh rereads local data.");
                    if (DateTime.UtcNow - _snapshot.FetchedAtUtc > TimeSpan.FromMinutes(3))
                        lines.Add("Older sample: continue Claude Code normally for a newer reading.");
                }
                else
                {
                    lines.Add("Source: Codex app-server · " + FormatFreshness(_snapshot.FetchedAtUtc));
                    lines.Add("Refresh: server push + 15-second fallback");
                }
                if (_serviceError != null)
                {
                    lines.Add("Status: " + _serviceError);
                }
                text = string.Join(Environment.NewLine, lines.ToArray());
            }

            _toolTip.SetToolTip(this, text);
            AccessibleDescription = text;
            AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            string trayText = BuildTrayText();
            _trayIcon.Text = trayText.Length <= 63 ? trayText : trayText.Substring(0, 63);
            UpdateTrayUsageIcon();
        }

        private static string FormatDetailedWindow(UsageWindow window)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1:0}% left · resets {2}",
                window.Label,
                window.RemainingPercent,
                window.ResetAtUtc.ToLocalTime().ToString("ddd d MMM, h:mm tt", CultureInfo.CurrentCulture));
        }

        private string BuildTrayText()
        {
            if (_snapshot == null)
            {
                return _serviceError == null ? ProviderName + " usage - connecting" : ProviderName + " usage unavailable";
            }
            if (DisplayWindow == null)
            {
                return ProviderName + " usage unavailable";
            }

            string text = ProviderName + ": " + Math.Round(DisplayWindow.RemainingPercent, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) +
                (DisplayWindow.DurationMinutes == 300 ? "% 5h left" : "% weekly left");
            ResetCredit credit;
            TimeSpan remaining;
            if (GetResetExpiryUrgency(out credit, out remaining) != ResetExpiryUrgency.None)
            {
                text += " · reset expires " + FormatCountdownCompact(remaining);
            }
            bool stale = _serviceError != null || DateTime.UtcNow - _snapshot.FetchedAtUtc > TimeSpan.FromMinutes(3);
            return text + (stale ? " (older data)" : string.Empty);
        }

        private void UpdateTrayUsageIcon()
        {
            UsageWindow window = DisplayWindow;
            if (window == null)
            {
                if (_trayUsageIcon != null)
                {
                    _trayIcon.Icon = _applicationIcon;
                    _trayUsageIcon.Dispose();
                    _trayUsageIcon = null;
                }
                _trayUsagePercent = int.MinValue;
                _trayUsageState = -1;
                return;
            }

            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(window.RemainingPercent, MidpointRounding.AwayFromZero)));
            bool stale = _serviceError != null || DateTime.UtcNow - _snapshot.FetchedAtUtc > TimeSpan.FromMinutes(3);
            int state = stale ? 3 : (percent <= 10 ? 2 : (percent <= 25 ? 1 : 0));
            if (_trayUsageIcon != null && _trayUsagePercent == percent && _trayUsageState == state)
            {
                return;
            }

            Icon nextIcon = CreatePercentageTrayIcon(percent, state, _provider == UsageProvider.Claude);
            Icon previousIcon = _trayUsageIcon;
            _trayUsageIcon = nextIcon;
            _trayUsagePercent = percent;
            _trayUsageState = state;
            _trayIcon.Icon = nextIcon;
            if (previousIcon != null)
            {
                previousIcon.Dispose();
            }
        }

        private static Icon CreatePercentageTrayIcon(int percent, int state, bool claude)
        {
            Color background = state == 3
                ? Color.FromArgb(190, 126, 27)
                : state == 2
                    ? Color.FromArgb(196, 65, 65)
                    : state == 1
                        ? Color.FromArgb(190, 126, 27)
                        : claude ? Color.FromArgb(174, 89, 56) : Color.FromArgb(52, 101, 230);

            const int iconSize = 20;
            float fontSize = percent >= 100 ? 9f : (percent >= 10 ? 12f : 14f);
            using (Bitmap bitmap = new Bitmap(iconSize, iconSize))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (SolidBrush backgroundBrush = new SolidBrush(background))
            using (Pen outlinePen = new Pen(Color.FromArgb(76, 0, 0, 0), 1f))
            using (Font numberFont = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (GraphicsPath badgePath = RoundedRectangle(new RectangleF(0.5f, 0.5f, 19f, 19f), 4.5f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                graphics.Clear(Color.Transparent);
                graphics.FillPath(backgroundBrush, badgePath);
                graphics.DrawPath(outlinePen, badgePath);

                TextFormatFlags numberFlags = TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine |
                    TextFormatFlags.NoPadding |
                    TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(
                    graphics,
                    percent.ToString(CultureInfo.InvariantCulture),
                    numberFont,
                    new Rectangle(0, -1, iconSize, iconSize + 1),
                    Color.White,
                    numberFlags);

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private Icon CreateApplicationIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (SolidBrush background = new SolidBrush(AccentColor))
                using (Pen ring = new Pen(Color.White, 2.7f))
                using (SolidBrush accent = new SolidBrush(Color.White))
                {
                    graphics.FillEllipse(background, 1, 1, 30, 30);
                    graphics.DrawArc(ring, 7, 7, 18, 18, -52, 278);
                    graphics.FillEllipse(accent, 21, 8, 3.5f, 3.5f);
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle rectangle, float radius)
        {
            return RoundedRectangle(new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), radius);
        }

        private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
        {
            float diameter = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ApplyRoundedRegion()
        {
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 10f))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideWidget();
                return;
            }

            _settings.Save(Location, TopMost, _darkMode);
            _refreshTimer.Stop();
            _clockTimer.Stop();
            _attentionTimer.Stop();
            _savePositionTimer.Stop();
            _trayIcon.Visible = false;
            _service.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_resourcesDisposed)
            {
                _resourcesDisposed = true;
                _service.Dispose();
                _brandFont.Dispose();
                _statusFont.Dispose();
                _labelFont.Dispose();
                _valueFont.Dispose();
                _valueCompactFont.Dispose();
                _refreshFont.Dispose();
                _toolTip.Dispose();
                _menu.Dispose();
                _trayIcon.Dispose();
                if (_trayUsageIcon != null) _trayUsageIcon.Dispose();
                _applicationIcon.Dispose();
                _refreshTimer.Dispose();
                _clockTimer.Dispose();
                _attentionTimer.Dispose();
                _savePositionTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AgentUsageBar";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Windows startup could not be changed: " + ex.Message;
                return false;
            }
        }
    }

    internal sealed class ThemeColorTable : ProfessionalColorTable
    {
        private readonly bool _darkMode;

        public ThemeColorTable(bool darkMode)
        {
            _darkMode = darkMode;
        }

        public override Color ToolStripDropDownBackground { get { return _darkMode ? Color.FromArgb(34, 34, 34) : Color.FromArgb(248, 247, 243); } }
        public override Color ImageMarginGradientBegin { get { return _darkMode ? Color.FromArgb(34, 34, 34) : Color.FromArgb(238, 237, 232); } }
        public override Color ImageMarginGradientMiddle { get { return _darkMode ? Color.FromArgb(34, 34, 34) : Color.FromArgb(238, 237, 232); } }
        public override Color ImageMarginGradientEnd { get { return _darkMode ? Color.FromArgb(34, 34, 34) : Color.FromArgb(238, 237, 232); } }
        public override Color MenuItemSelected { get { return _darkMode ? Color.FromArgb(42, 48, 62) : Color.FromArgb(230, 235, 247); } }
        public override Color MenuItemBorder { get { return _darkMode ? Color.FromArgb(88, 108, 164) : Color.FromArgb(164, 183, 230); } }
        public override Color SeparatorDark { get { return _darkMode ? Color.FromArgb(59, 59, 59) : Color.FromArgb(213, 211, 204); } }
        public override Color SeparatorLight { get { return _darkMode ? Color.FromArgb(59, 59, 59) : Color.FromArgb(213, 211, 204); } }
        public override Color CheckBackground { get { return _darkMode ? Color.FromArgb(54, 67, 102) : Color.FromArgb(205, 216, 244); } }
        public override Color CheckSelectedBackground { get { return _darkMode ? Color.FromArgb(54, 67, 102) : Color.FromArgb(205, 216, 244); } }
        public override Color CheckPressedBackground { get { return _darkMode ? Color.FromArgb(67, 82, 123) : Color.FromArgb(183, 201, 242); } }
    }

    internal static class NativeMethods
    {
        internal const int WmNcLButtonDown = 0x00A1;
        internal const int HtCaption = 0x0002;

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        internal static void TryEnableDpiAwareness()
        {
            try
            {
                SetProcessDpiAwareness(2);
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        internal static void TryUseRoundedCorners(IntPtr handle)
        {
            try
            {
                const int DwmWindowCornerPreference = 33;
                int preference = 2;
                DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref preference, sizeof(int));
            }
            catch
            {
                // The explicit window region still supplies rounded corners.
            }
        }
    }
}
