using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace AgentIsland
{
    public sealed class CodexActivityWatcher : IDisposable
    {
        private readonly AppSettings settings;
        private readonly Action<string> onLine;
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Dictionary<string, long> offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> lastPublishedUtcBySession = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new object();
        private int queued;
        private bool disposed;

        public CodexActivityWatcher(AppSettings settings, Action<string> onLine)
        {
            this.settings = settings;
            this.onLine = onLine;
        }

        public void Start()
        {
            var sessions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            if (!Directory.Exists(sessions))
            {
                return;
            }

            RememberExistingSessionOffsets(sessions);
            AddWatcher(sessions, "*.jsonl", true);
        }

        private void RememberExistingSessionOffsets(string sessions)
        {
            try
            {
                foreach (var file in Directory.GetFiles(sessions, "*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        offsets[file] = new FileInfo(file).Length;
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

        private void AddWatcher(string directory, string filter, bool includeSubdirectories)
        {
            var watcher = new FileSystemWatcher(directory, filter);
            watcher.IncludeSubdirectories = includeSubdirectories;
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (disposed || !settings.FastStatusProbeEnabled || !LooksLikeSessionTranscript(e.FullPath))
            {
                return;
            }

            lock (gate)
            {
                pendingPaths.Add(e.FullPath);
            }

            if (Interlocked.Exchange(ref queued, 1) == 1)
            {
                return;
            }

            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(120);
                Interlocked.Exchange(ref queued, 0);
                DrainPendingPaths();
            });
        }

        private void DrainPendingPaths()
        {
            string[] paths;
            lock (gate)
            {
                paths = new string[pendingPaths.Count];
                pendingPaths.CopyTo(paths);
                pendingPaths.Clear();
            }

            for (var i = 0; i < paths.Length; i++)
            {
                var text = ReadAppendedText(paths[i]);
                var activity = DetectActivityEvent(text);
                if (activity != null && activity.EventName != null)
                {
                    PublishActivity(activity.EventName, paths[i], activity.ConversationName);
                }
            }
        }

        private string ReadAppendedText(string path)
        {
            if (disposed || !File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long offset;
                    lock (gate)
                    {
                        if (!offsets.TryGetValue(path, out offset))
                        {
                            offset = 0;
                        }
                    }

                    if (stream.Length < offset)
                    {
                        offset = 0;
                    }

                    if (stream.Length == offset)
                    {
                        return string.Empty;
                    }

                    var length = stream.Length - offset;
                    if (length > 65536)
                    {
                        offset = stream.Length - 65536;
                        length = 65536;
                    }

                    stream.Position = offset;
                    var buffer = new byte[(int)length];
                    var read = stream.Read(buffer, 0, buffer.Length);

                    lock (gate)
                    {
                        offsets[path] = stream.Position;
                    }

                    return read > 0 ? Encoding.UTF8.GetString(buffer, 0, read) : string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private ActivityDetection DetectActivityEvent(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string detected = null;
            string conversationName = null;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (Contains(line, "\"type\":\"user_message\"") ||
                    (Contains(line, "\"type\":\"message\"") && Contains(line, "\"role\":\"user\"")))
                {
                    detected = "UserPromptSubmit";
                    conversationName = CleanConversationName(FirstNonEmpty(
                        FindJsonString(line, "message"),
                        FindJsonString(line, "text")));
                    continue;
                }

                if (Contains(line, "\"type\":\"function_call\""))
                {
                    detected = "PreToolUse";
                    continue;
                }

                if (Contains(line, "\"type\":\"reasoning\""))
                {
                    detected = "UserPromptSubmit";
                    continue;
                }

                if (Contains(line, "\"type\":\"agent_message\"") &&
                    (Contains(line, "\"phase\":\"final_answer\"") || Contains(line, "\"phase\":\"final\"")))
                {
                    detected = "Stop";
                    continue;
                }

                if (detected == "Stop")
                {
                    continue;
                }

                if (Contains(line, "\"type\":\"agent_message\"") ||
                    (Contains(line, "\"type\":\"message\"") && Contains(line, "\"role\":\"assistant\"")))
                {
                    detected = "PostToolUse";
                }
            }

            return detected == null ? null : new ActivityDetection(detected, conversationName);
        }

        private void PublishActivity(string eventName, string transcriptPath, string conversationName)
        {
            if (disposed || !settings.FastStatusProbeEnabled)
            {
                return;
            }

            var sessionId = ExtractSessionId(transcriptPath);
            lock (gate)
            {
                var now = DateTime.UtcNow;
                DateTime lastPublishedUtc;
                if (eventName != "Stop" &&
                    lastPublishedUtcBySession.TryGetValue(sessionId, out lastPublishedUtc) &&
                    (now - lastPublishedUtc) < TimeSpan.FromMilliseconds(520))
                {
                    return;
                }

                lastPublishedUtcBySession[sessionId] = now;
            }

            var message = eventName == "PreToolUse"
                ? "检测到 Codex 工具调用"
                : (eventName == "PostToolUse" ? "检测到 Codex 正在回复" : (eventName == "Stop" ? "检测到 Codex 回合完成" : "检测到新的 Codex 回合"));
            var namePart = string.IsNullOrWhiteSpace(conversationName)
                ? string.Empty
                : ",\"conversation_name\":\"" + JsonEscape(conversationName) + "\"";
            onLine("{\"v\":1,\"agent\":\"codex\",\"event\":\"" + eventName + "\",\"session_id\":\"" + JsonEscape(sessionId) + "\",\"transcript_path\":\"" + JsonEscape(transcriptPath) + "\",\"message\":\"" + JsonEscape(message) + "\",\"source\":\"session-watch\"" + namePart + "}");
        }

        private sealed class ActivityDetection
        {
            public ActivityDetection(string eventName, string conversationName)
            {
                EventName = eventName;
                ConversationName = conversationName;
            }

            public string EventName { get; private set; }
            public string ConversationName { get; private set; }
        }

        private static string ExtractSessionId(string path)
        {
            var fileName = Path.GetFileName(path) ?? path;
            var match = Regex.Match(fileName, "([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\\.jsonl$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }

            return fileName.ToLowerInvariant();
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string FindJsonString(string line, string name)
        {
            var pattern = "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"])*)\"";
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success && line.IndexOf("\\\"", StringComparison.Ordinal) >= 0)
            {
                match = Regex.Match(line.Replace("\\\"", "\""), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            if (!match.Success)
            {
                return null;
            }

            return JsonUnescape(match.Groups["v"].Value);
        }

        private static string JsonUnescape(string value)
        {
            return DecodeJsonUnicodeEscapes(value)
                .Replace("\\r", " ")
                .Replace("\\n", " ")
                .Replace("\\t", " ")
                .Replace("\\\"", "\"")
                .Replace("\\/", "/")
                .Replace("\\\\", "\\");
        }

        private static string DecodeJsonUnicodeEscapes(string value)
        {
            return Regex.Replace(value, "\\\\u(?<h>[0-9a-fA-F]{4})", delegate(Match match)
            {
                return ((char)Convert.ToInt32(match.Groups["h"].Value, 16)).ToString();
            });
        }

        private static string CleanConversationName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = Regex.Replace(value, "\\s+", " ").Trim();
            if (value.Length == 0 || LooksLikeMojibake(value) || LooksLikeSyntheticConversationText(value))
            {
                return null;
            }

            return value.Length > 68 ? value.Substring(0, 65) + "..." : value;
        }

        private static bool LooksLikeSyntheticConversationText(string value)
        {
            var markers = new[]
            {
                "# AGENTS.md instructions",
                "AGENTS.md instructions",
                "<environment_context>",
                "<INSTRUCTIONS>",
                "CODEGRAPH_START",
                "Knowledge cutoff:",
                "You are Codex",
                "sandbox_mode",
                "approval_policy",
                "developer_instructions",
                "current_date",
                "<permissions instructions>",
                "<skills_instructions>",
                "<plugins_instructions>",
                "<collaboration_mode>",
                "<apps_instructions>"
            };

            for (var i = 0; i < markers.Length; i++)
            {
                if (value.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
        private static bool LooksLikeMojibake(string value)
        {
            var markers = new[]
            {
                "锛", "鐨", "鏄", "杩", "瀹", "殑", "涓", "竴", "傜", "绋", "戜", "浣",
                "姝", "娴", "嬭", "厭", "楀", "悕", "绉", "扮", "粰", "鎴", "閫",
                "犲", "搧", "绫", "娌", "鎸", "囧", "畾", "鍩", "競"
            };
            var score = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] >= '\uE000' && value[i] <= '\uF8FF')
                {
                    return true;
                }
            }

            for (var i = 0; i < markers.Length; i++)
            {
                if (value.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score++;
                }
            }

            return score >= 2;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return null;
        }

        private static bool LooksLikeSessionTranscript(string path)
        {
            return string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase) &&
                   path.IndexOf(Path.DirectorySeparatorChar + "sessions" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Contains(string value, string part)
        {
            return value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose()
        {
            disposed = true;
            for (var i = 0; i < watchers.Count; i++)
            {
                watchers[i].Dispose();
            }
        }
    }
}
