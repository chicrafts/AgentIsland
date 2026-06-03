using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace AgentIsland
{
    public sealed class StatusCoordinator
    {
        private static readonly StatusSnapshot[] EmptyConversations = new StatusSnapshot[0];
        private readonly Action<StatusSnapshot> publish;
        private readonly AppSettings settings;
        private readonly DispatcherTimer staleTimer;
        private readonly Dictionary<string, StatusSnapshot> conversationSessions = new Dictionary<string, StatusSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> conversationNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> conversationNamePriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private StatusSnapshot current;
        private int demoIndex;

        private readonly StatusSnapshot[] demoStates =
        {
            new StatusSnapshot(IslandMode.Thinking, "CODEX", "思考中", "正在拆解任务和读取上下文", "reasoning", DateTimeOffset.Now),
            new StatusSnapshot(IslandMode.Editing, "CODEX", "修改中", "apply_patch -> MainWindow", "apply_patch", DateTimeOffset.Now),
            new StatusSnapshot(IslandMode.Running, "CODEX", "执行中", "csc.exe /target:winexe", "shell", DateTimeOffset.Now),
            new StatusSnapshot(IslandMode.Waiting, "CLAUDE CODE", "等待授权", "需要你确认一次文件写入", "PermissionRequest", DateTimeOffset.Now),
            new StatusSnapshot(IslandMode.Compacting, "CODEX", "整理上下文", "正在压缩长会话", "PreCompact", DateTimeOffset.Now),
            new StatusSnapshot(IslandMode.Done, "CODEX", "已完成", "任务结束，等待你的下一句", "Stop", DateTimeOffset.Now)
        };

        public StatusCoordinator(Action<StatusSnapshot> publish, AppSettings settings)
        {
            this.publish = publish;
            this.settings = settings;
            current = StatusSnapshot.Idle();
            staleTimer = new DispatcherTimer();
            staleTimer.Interval = TimeSpan.FromSeconds(8);
            staleTimer.Tick += delegate { CheckStale(); };
            staleTimer.Start();
            Publish(StatusSnapshot.Idle());
        }

        public void HandleLine(string line)
        {
            var snapshot = HookNormalizer.TryNormalize(line);
            if (snapshot == null)
            {
                return;
            }

            PruneConversationSessions();

            var rawEvent = FindJsonString(line, "event");
            var source = FindJsonString(line, "source");
            var explicitSessionKey = FirstNonEmpty(
                FindJsonString(line, "session_id"),
                FindJsonString(line, "conversation_id"),
                FindJsonString(line, "transcript_path"),
                FindJsonString(line, "turn_id"));
            var sessionKey = NormalizeSessionKey(explicitSessionKey, snapshot.Agent);
            var conversationName = ExtractConversationName(line, rawEvent, source);
            if (!string.IsNullOrWhiteSpace(conversationName))
            {
                conversationNames[sessionKey] = conversationName;
            }
            else
            {
                conversationNames.TryGetValue(sessionKey, out conversationName);
            }

            var keyedSnapshot = snapshot.WithConversationName(conversationName).WithSessionContext(sessionKey, 0, EmptyConversations);

            StatusSnapshot previous;
            if (conversationSessions.TryGetValue(sessionKey, out previous) &&
                previous.Mode == IslandMode.Done &&
                IsFastProbe(source) &&
                IsPostCompletionProbe(rawEvent) &&
                DateTimeOffset.Now - previous.Timestamp < TimeSpan.FromSeconds(10))
            {
                return;
            }

            if (conversationSessions.TryGetValue(sessionKey, out previous) &&
                previous.Mode == IslandMode.Waiting &&
                IsFastProbe(source) &&
                snapshot.Mode != IslandMode.Done &&
                snapshot.Mode != IslandMode.Error &&
                DateTimeOffset.Now - previous.Timestamp < TimeSpan.FromSeconds(75))
            {
                return;
            }

            if (conversationSessions.TryGetValue(sessionKey, out previous) &&
                previous.Mode == IslandMode.Done &&
                snapshot.Mode != IslandMode.Done &&
                snapshot.Mode != IslandMode.Error &&
                snapshot.Mode != IslandMode.Idle &&
                IsPostStopTailEvent(rawEvent) &&
                DateTimeOffset.Now - previous.Timestamp < TimeSpan.FromSeconds(45))
            {
                return;
            }

            if (IsConversationMode(snapshot.Mode))
            {
                conversationSessions[sessionKey] = keyedSnapshot;
                if (snapshot.Mode == IslandMode.Done)
                {
                    PruneSupersededActiveSessions(keyedSnapshot.Timestamp);
                }
            }
            else if (snapshot.Mode == IslandMode.Idle)
            {
                conversationSessions.Remove(sessionKey);
                conversationNames.Remove(sessionKey);
                conversationNamePriorities.Remove(sessionKey);
            }

            PruneConversationSessions();
            var conversations = BuildConversationList();

            StatusSnapshot display;
            var topConversation = conversations.Length > 0 ? conversations[0] : null;
            if (topConversation != null &&
                IsConversationMode(topConversation.Mode) &&
                ConversationDisplayPriority(topConversation.Mode) < ConversationDisplayPriority(snapshot.Mode))
            {
                display = topConversation;
            }
            else if (IsActiveMode(snapshot.Mode) || snapshot.Mode == IslandMode.Error || snapshot.Mode == IslandMode.Done)
            {
                display = keyedSnapshot;
            }
            else if (conversations.Length > 0)
            {
                display = conversations[0];
            }
            else
            {
                display = keyedSnapshot;
            }

            PublishWithConversationList(display, conversations);
        }

        public void Reset()
        {
            conversationSessions.Clear();
            conversationNames.Clear();
            conversationNamePriorities.Clear();
            Publish(StatusSnapshot.Idle());
        }

        public void ClearConversation(string sessionKey)
        {
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                return;
            }

            conversationSessions.Remove(sessionKey);
            conversationNames.Remove(sessionKey);
            conversationNamePriorities.Remove(sessionKey);

            var conversations = BuildConversationList();
            if (conversations.Length == 0)
            {
                Publish(StatusSnapshot.Idle());
                return;
            }

            StatusSnapshot display = null;
            if (current != null &&
                !string.IsNullOrWhiteSpace(current.SessionKey) &&
                conversationSessions.TryGetValue(current.SessionKey, out display))
            {
                PublishWithConversationList(display, conversations);
                return;
            }

            PublishWithConversationList(conversations[0], conversations);
        }

        public void Demo()
        {
            conversationSessions.Clear();
            conversationNames.Clear();
            conversationNamePriorities.Clear();
            var now = DateTimeOffset.Now;
            var first = new StatusSnapshot(IslandMode.Thinking, "CODEX", "思考中", "正在拆解任务和读取上下文", "reasoning", now).WithSessionContext("demo-a", 0, EmptyConversations);
            var second = new StatusSnapshot(IslandMode.Editing, "CLAUDE CODE", "修改中", "正在调整前端动画", "edit", now.AddMilliseconds(-180)).WithSessionContext("demo-b", 0, EmptyConversations);
            var third = new StatusSnapshot(IslandMode.Running, "CODEX", "执行中", "正在跑验证命令", "shell", now.AddMilliseconds(-360)).WithSessionContext("demo-c", 0, EmptyConversations);
            first = first.WithConversationName("优化悬浮窗状态岛动画");
            second = second.WithConversationName("同步 Claude Code 修改状态");
            third = third.WithConversationName("打包 Windows 安装程序");
            conversationNames[first.SessionKey] = first.ConversationName;
            conversationNames[second.SessionKey] = second.ConversationName;
            conversationNames[third.SessionKey] = third.ConversationName;
            conversationNamePriorities[first.SessionKey] = 100;
            conversationNamePriorities[second.SessionKey] = 100;
            conversationNamePriorities[third.SessionKey] = 100;
            conversationSessions[first.SessionKey] = first;
            conversationSessions[second.SessionKey] = second;
            conversationSessions[third.SessionKey] = third;

            var template = demoStates[demoIndex++ % demoStates.Length].WithConversationName(first.ConversationName).WithSessionContext("demo-a", 0, EmptyConversations);
            conversationSessions[template.SessionKey] = template;
            PublishWithConversationList(template, BuildConversationList());
        }

        private static bool IsFastProbe(string source)
        {
            return string.Equals(source, "session-watch", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPostCompletionProbe(string rawEvent)
        {
            if (string.IsNullOrWhiteSpace(rawEvent))
            {
                return false;
            }

            return rawEvent.IndexOf("PostToolUse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawEvent.IndexOf("PreToolUse", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPostStopTailEvent(string rawEvent)
        {
            if (string.IsNullOrWhiteSpace(rawEvent))
            {
                return false;
            }

            return rawEvent.IndexOf("Notification", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawEvent.IndexOf("PostToolUse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawEvent.IndexOf("PostToolBatch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawEvent.IndexOf("SubagentStop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawEvent.IndexOf("TaskCompleted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rawEvent.IndexOf("item_completed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PruneSupersededActiveSessions(DateTimeOffset completedAt)
        {
            var expired = new List<string>();
            foreach (var pair in conversationSessions)
            {
                if (IsActiveMode(pair.Value.Mode) && completedAt - pair.Value.Timestamp > TimeSpan.FromSeconds(35))
                {
                    expired.Add(pair.Key);
                }
            }

            for (var i = 0; i < expired.Count; i++)
            {
                conversationSessions.Remove(expired[i]);
                conversationNames.Remove(expired[i]);
                conversationNamePriorities.Remove(expired[i]);
            }
        }
        private static bool IsConversationMode(IslandMode mode)
        {
            return IsActiveMode(mode) || mode == IslandMode.Done || mode == IslandMode.Error;
        }

        private static bool IsActiveMode(IslandMode mode)
        {
            return mode == IslandMode.Thinking ||
                   mode == IslandMode.Editing ||
                   mode == IslandMode.Running ||
                   mode == IslandMode.Waiting ||
                   mode == IslandMode.Compacting;
        }

        private StatusSnapshot[] BuildConversationList()
        {
            var list = new List<StatusSnapshot>(conversationSessions.Values);
            list.Sort(delegate(StatusSnapshot left, StatusSnapshot right)
            {
                var leftPriority = ConversationDisplayPriority(left.Mode);
                var rightPriority = ConversationDisplayPriority(right.Mode);
                if (leftPriority != rightPriority)
                {
                    return leftPriority.CompareTo(rightPriority);
                }

                return right.Timestamp.CompareTo(left.Timestamp);
            });
            return list.ToArray();
        }

        private static int ConversationDisplayPriority(IslandMode mode)
        {
            if (mode == IslandMode.Waiting)
            {
                return 0;
            }
            if (mode == IslandMode.Error)
            {
                return 1;
            }
            if (IsActiveMode(mode))
            {
                return 2;
            }
            if (mode == IslandMode.Done)
            {
                return 3;
            }
            return 4;
        }

        private void PublishWithConversationList(StatusSnapshot display, StatusSnapshot[] conversations)
        {
            var visibleConversations = settings.ShowMultiConversationCount ? conversations : EmptyConversations;
            var count = settings.ShowMultiConversationCount ? visibleConversations.Length : 0;
            Publish(display.WithSessionContext(display.SessionKey, count, visibleConversations));
        }

        private static string NormalizeSessionKey(string explicitKey, string agent)
        {
            var value = explicitKey;
            if (string.IsNullOrWhiteSpace(value))
            {
                return "local:" + (agent ?? "agent").Trim().ToLowerInvariant();
            }

            value = value.Trim();
            var uuid = Regex.Match(value, "([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:\\.jsonl)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (uuid.Success)
            {
                return uuid.Groups[1].Value.ToLowerInvariant();
            }

            try
            {
                if (value.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileNameWithoutExtension(value).ToLowerInvariant();
                }
            }
            catch
            {
            }

            return value.ToLowerInvariant();
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

        private static string ExtractConversationName(string line, string rawEvent, string source)
        {
            var explicitName = CleanConversationName(FirstNonEmpty(
                FindJsonString(line, "conversation_name"),
                FindJsonString(line, "conversationName"),
                FindJsonString(line, "session_title"),
                FindJsonString(line, "sessionTitle")));
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }

            if (string.IsNullOrWhiteSpace(rawEvent) ||
                rawEvent.IndexOf("UserPromptSubmit", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var prompt = FindJsonString(line, "prompt");
            if (string.IsNullOrWhiteSpace(prompt) && !IsFastProbe(source))
            {
                prompt = FindJsonString(line, "message");
            }

            return CleanConversationName(prompt);
        }

        private static string CleanConversationName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = Regex.Replace(value, "\\s+", " ").Trim();
            if (value.Length == 0 ||
                LooksLikeMojibake(value) ||
                LooksLikeSyntheticConversationText(value) ||
                value.IndexOf("检测到", StringComparison.OrdinalIgnoreCase) >= 0)
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

        private void PruneConversationSessions()
        {
            var now = DateTimeOffset.Now;
            var expired = new List<string>();
            foreach (var pair in conversationSessions)
            {
                var age = now - pair.Value.Timestamp;
                if ((pair.Value.Mode == IslandMode.Done && !settings.KeepCompletedConversations && age > TimeSpan.FromSeconds(18)) ||
                    (pair.Value.Mode == IslandMode.Error && age > TimeSpan.FromSeconds(120)) ||
                    (pair.Value.Mode == IslandMode.Waiting && age > TimeSpan.FromSeconds(300)) ||
                    (pair.Value.Mode != IslandMode.Waiting && IsActiveMode(pair.Value.Mode) && age > TimeSpan.FromSeconds(90)))
                {
                    expired.Add(pair.Key);
                }
            }

            for (var i = 0; i < expired.Count; i++)
            {
                conversationSessions.Remove(expired[i]);
                conversationNames.Remove(expired[i]);
            }
        }

        private void Publish(StatusSnapshot snapshot)
        {
            current = snapshot;
            publish(snapshot);
        }

        private void CheckStale()
        {
            PruneConversationSessions();
            var conversations = BuildConversationList();
            if (conversations.Length > 0)
            {
                if (current == null || !conversationSessions.ContainsKey(current.SessionKey))
                {
                    PublishWithConversationList(conversations[0], conversations);
                }
                return;
            }

            var age = DateTimeOffset.Now - current.Timestamp;
            if (current.Mode == IslandMode.Done && !settings.KeepCompletedConversations && age > TimeSpan.FromSeconds(18))
            {
                Publish(StatusSnapshot.Idle());
                return;
            }

            if (current.Mode == IslandMode.Waiting && age > TimeSpan.FromSeconds(600))
            {
                conversationSessions.Clear();
                conversationNames.Clear();
                Publish(StatusSnapshot.Idle());
                return;
            }

            if (((current.Mode != IslandMode.Waiting && IsActiveMode(current.Mode)) || current.Mode == IslandMode.Error) && age > TimeSpan.FromSeconds(180))
            {
                conversationSessions.Clear();
                conversationNames.Clear();
                Publish(StatusSnapshot.Idle());
            }
        }
    }
}
