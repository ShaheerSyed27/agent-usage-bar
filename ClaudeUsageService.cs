using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CodexUsageBar
{
    internal sealed class ClaudeUsageService : IUsageService
    {
        private readonly string _path;
        private DateTime _lastWriteUtc;
        private DateTime _nextResetUtc = DateTime.MaxValue;
        private bool _disposed;
        public event Action<UsageSnapshot> SnapshotReceived;
        public event Action<string> ServiceError;

        public ClaudeUsageService() : this(ClaudeUsageData.CachePath) { }
        internal ClaudeUsageService(string path) { _path = path; }
        public void Start() { RequestUsage(); }

        public void RequestUsage()
        {
            if (_disposed) return;
            try
            {
                FileInfo file = new FileInfo(_path);
                if (!file.Exists)
                {
                    _lastWriteUtc = DateTime.MinValue;
                    RaiseError("Connect Claude Code with scripts\\configure-claude.ps1, then use Claude normally. " +
                        "Subscription limits arrive through its status line after a response. No login tokens are read.");
                    return;
                }
                if (file.Length > 8192) { RaiseError("Claude usage data is invalid. Awaiting a fresh status-line update."); return; }
                if (file.LastWriteTimeUtc == _lastWriteUtc && DateTime.UtcNow < _nextResetUtc) return;
                UsageSnapshot snapshot = ParseCache(File.ReadAllText(_path), DateTime.UtcNow);
                if (snapshot == null) { RaiseError("Claude usage data is invalid. Awaiting a fresh status-line update."); return; }
                _lastWriteUtc = file.LastWriteTimeUtc;
                _nextResetUtc = DateTime.MaxValue;
                if (snapshot.SessionWindow != null) _nextResetUtc = snapshot.SessionWindow.ResetAtUtc;
                if (snapshot.WeeklyWindow != null && snapshot.WeeklyWindow.ResetAtUtc < _nextResetUtc)
                    _nextResetUtc = snapshot.WeeklyWindow.ResetAtUtc;
                if (SnapshotReceived != null) SnapshotReceived(snapshot);
            }
            catch (IOException) { RaiseError("Claude's local sample is temporarily unavailable. Retrying."); }
            catch (UnauthorizedAccessException) { RaiseError("Cannot read the local Claude usage sample."); }
            catch (ArgumentException) { RaiseError("Waiting for valid Claude status-line data."); }
            catch (InvalidOperationException) { RaiseError("Waiting for valid Claude status-line data."); }
        }

        internal static UsageSnapshot ParseCache(string input, DateTime now)
        {
            if (input.Length > 8192) return null;
            Dictionary<string, object> root = ClaudeUsageData.Serializer().DeserializeObject(input) as Dictionary<string, object>;
            object capturedValue;
            object schema;
            long captured;
            if (root == null || !root.TryGetValue("schema_version", out schema) || Convert.ToString(schema, CultureInfo.InvariantCulture) != "1" ||
                !root.TryGetValue("captured_at", out capturedValue) ||
                !long.TryParse(Convert.ToString(capturedValue, CultureInfo.InvariantCulture), out captured) ||
                captured <= 0 || captured > ClaudeUsageData.UnixSeconds(now.AddMinutes(1))) return null;
            return new UsageSnapshot
            {
                FetchedAtUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(captured),
                SessionWindow = ConvertWindow(ClaudeUsageData.ReadWindow(root, "five_hour", now), 300),
                WeeklyWindow = ConvertWindow(ClaudeUsageData.ReadWindow(root, "seven_day", now), 10080),
                ResetCreditDetails = new List<ResetCredit>(),
                OrdinaryUsageAllowed = true
            };
        }

        private static UsageWindow ConvertWindow(Dictionary<string, object> window, int duration)
        {
            if (window == null) return null;
            return new UsageWindow
            {
                UsedPercent = Convert.ToDouble(window["used_percentage"], CultureInfo.InvariantCulture),
                DurationMinutes = duration,
                ResetAtUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToInt64(window["resets_at"], CultureInfo.InvariantCulture))
            };
        }

        private void RaiseError(string error) { if (ServiceError != null) ServiceError(error); }
        public void Dispose() { _disposed = true; }
    }
}
