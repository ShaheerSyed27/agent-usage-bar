using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CodexUsageBar
{
    internal static class ClaudeUsageBridge
    {
        private static int Main(string[] args)
        {
            try
            {
                bool check = false;
                bool desktop = false;
                foreach (string arg in args)
                {
                    if (arg == "--check" && !check) check = true;
                    else if (arg == "--desktop" && !desktop) desktop = true;
                    else return 2;
                }
                StringBuilder input = new StringBuilder();
                char[] buffer = new char[2048];
                int read;
                while ((read = Console.In.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (input.Length + read > ClaudeUsageData.MaximumInputLength) return 2;
                    input.Append(buffer, 0, read);
                }
                Dictionary<string, object> sample = ClaudeUsageData.Sanitize(input.ToString(), DateTime.UtcNow);
                if (desktop) sample["source_kind"] = 2;
                string json = ClaudeUsageData.Serializer().Serialize(sample);
                if (check)
                {
                    Console.WriteLine(json);
                    return 0;
                }
                // Only the allowlisted numeric sample reaches disk. Raw stdin is never logged.
                if (sample.ContainsKey("five_hour") || sample.ContainsKey("seven_day"))
                    WriteCache(json, ClaudeUsageData.CachePath);
                List<string> parts = new List<string>();
                foreach (string key in new[] { "five_hour", "seven_day" })
                {
                    object value;
                    if (!sample.TryGetValue(key, out value)) continue;
                    Dictionary<string, object> window = (Dictionary<string, object>)value;
                    double remaining = 100 - Convert.ToDouble(window["used_percentage"], CultureInfo.InvariantCulture);
                    parts.Add((key == "five_hour" ? "5h " : "weekly ") + remaining.ToString("0", CultureInfo.InvariantCulture) + "% left");
                }
                Console.WriteLine(parts.Count == 0 ? "Claude usage: waiting for subscription limits" : "Claude: " + string.Join(" | ", parts.ToArray()));
                return 0;
            }
            catch
            {
                Console.WriteLine("Claude usage: data unavailable");
                return 1;
            }
        }

        internal static void WriteCache(string json, string path)
        {
            using (Mutex mutex = new Mutex(false, "Local\\AgentUsageBarClaudeCache"))
            {
                bool owns = false;
                string temporary = null;
                try
                {
                    try { owns = mutex.WaitOne(1000); }
                    catch (AbandonedMutexException) { owns = true; }
                    if (!owns) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporary, json, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    temporary = null;
                }
                finally
                {
                    if (temporary != null) { try { File.Delete(temporary); } catch { } }
                    if (owns) mutex.ReleaseMutex();
                }
            }
        }
    }
}
