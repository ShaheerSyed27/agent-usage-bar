using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace CodexUsageBar
{
    internal static class ClaudeUsageData
    {
        internal const int MaximumInputLength = 65536;
        internal static string CachePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgentUsageBar", "claude-usage.json"); }
        }

        internal static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumInputLength, RecursionLimit = 24 };
        }

        internal static long UnixSeconds(DateTime time)
        {
            return (long)(time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal static Dictionary<string, object> Sanitize(string input, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(input) || input.Length > MaximumInputLength)
                throw new ArgumentException("Missing or oversized status-line input.");
            Dictionary<string, object> root = Serializer().DeserializeObject(input) as Dictionary<string, object>;
            if (root == null) throw new ArgumentException("Expected status-line JSON.");
            object value;
            Dictionary<string, object> limits = root.TryGetValue("rate_limits", out value)
                ? value as Dictionary<string, object> : null;
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "schema_version", 1 },
                { "captured_at", UnixSeconds(now) }
            };
            foreach (string key in new[] { "five_hour", "seven_day" })
            {
                Dictionary<string, object> window = ReadWindow(limits, key, now);
                if (window != null) result[key] = window;
            }
            return result;
        }

        internal static Dictionary<string, object> ReadWindow(Dictionary<string, object> limits, string key, DateTime now)
        {
            object value;
            if (limits == null || !limits.TryGetValue(key, out value)) return null;
            Dictionary<string, object> window = value as Dictionary<string, object>;
            object usedValue;
            object resetValue;
            if (window == null || !window.TryGetValue("used_percentage", out usedValue) ||
                !window.TryGetValue("resets_at", out resetValue)) return null;
            double used;
            long resetsAt;
            if (usedValue == null || resetValue == null ||
                !double.TryParse(Convert.ToString(usedValue, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out used) ||
                double.IsNaN(used) || double.IsInfinity(used) || used < 0 || used > 100 ||
                !long.TryParse(Convert.ToString(resetValue, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out resetsAt) ||
                resetsAt <= UnixSeconds(now) || resetsAt >= 4102444800L) return null;
            return new Dictionary<string, object> { { "used_percentage", used }, { "resets_at", resetsAt } };
        }
    }
}
