using System;
using System.Text.RegularExpressions;

namespace AgentIsland
{
    public static class HookNormalizer
    {
        public static StatusSnapshot TryNormalize(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            var agent = FindJsonString(line, "agent");
            var eventName = FindJsonString(line, "event");
            var tool = FirstNonEmpty(
                FindJsonString(line, "tool_name"),
                FindJsonString(line, "toolName"),
                FindJsonString(line, "tool"),
                FindJsonString(line, "name"));
            var command = FirstNonEmpty(
                FindJsonString(line, "command"),
                FindJsonString(line, "cmd"),
                FindJsonString(line, "script"),
                FindJsonString(line, "pattern"),
                FindJsonString(line, "file_path"),
                FindJsonString(line, "path"));
            var message = FirstNonEmpty(
                FindJsonString(line, "message"),
                FindJsonString(line, "title"),
                FindJsonString(line, "reason"),
                FindJsonString(line, "notification"),
                FindJsonString(line, "description"),
                FindJsonString(line, "prompt"),
                FindJsonString(line, "summary"));
            var notificationType = FindJsonString(line, "notification_type");

            if (string.IsNullOrWhiteSpace(agent))
            {
                agent = "agent";
            }
            if (string.IsNullOrWhiteSpace(eventName))
            {
                eventName = "event";
            }

            var lowerEvent = eventName.ToLowerInvariant();
            var lowerNotification = string.IsNullOrWhiteSpace(notificationType) ? string.Empty : notificationType.ToLowerInvariant();
            var haystack = (eventName + " " + tool + " " + command + " " + line).ToLowerInvariant();
            var ts = DateTimeOffset.Now;

            if (lowerEvent.IndexOf("permissiondenied") >= 0 || lowerEvent.IndexOf("denied") >= 0)
            {
                return Snapshot(IslandMode.Error, agent, "已拒绝", DescribeTool(tool, command, FirstNonEmpty(message, "权限请求已被拒绝")), tool, ts);
            }

            if (lowerEvent.IndexOf("permissionrequest") >= 0 ||
                lowerEvent.IndexOf("permission_request") >= 0 ||
                lowerNotification == "permission_prompt")
            {
                return Snapshot(IslandMode.Waiting, agent, "等你确认", DescribeAttention(tool, command, message, "终端里有权限请求，需要确认或拒绝"), tool, ts);
            }

            if (lowerEvent == "elicitation" || lowerNotification == "elicitation_dialog")
            {
                return Snapshot(IslandMode.Waiting, agent, "等待填写", FirstNonEmpty(message, "有工具表单或输入框需要你处理"), tool, ts);
            }

            if (lowerEvent == "elicitationresult" ||
                lowerNotification == "elicitation_complete" ||
                lowerNotification == "elicitation_response")
            {
                return Snapshot(IslandMode.Thinking, agent, "继续处理", FirstNonEmpty(message, "已收到你的输入，继续执行"), tool, ts);
            }

            if (lowerEvent.IndexOf("notification") >= 0)
            {
                if (lowerNotification == "idle_prompt")
                {
                    return Snapshot(IslandMode.Waiting, agent, "等待回复", FirstNonEmpty(message, "Agent 已停下来，等你的下一句"), tool, ts);
                }

                if (lowerNotification == "auth_success")
                {
                    return Snapshot(IslandMode.Idle, agent, "已连接", FirstNonEmpty(message, "认证完成，正在监听状态"), tool, ts);
                }

                return Snapshot(IslandMode.Waiting, agent, "需要注意", FirstNonEmpty(message, "Agent 正在等你处理提示或输入"), tool, ts);
            }

            if (lowerEvent.IndexOf("failure") >= 0 || lowerEvent.IndexOf("error") >= 0)
            {
                return Snapshot(IslandMode.Error, agent, "出错了", FirstNonEmpty(message, "任务遇到错误，回到终端查看详情"), tool, ts);
            }

            if (lowerEvent.IndexOf("precompact") >= 0)
            {
                return Snapshot(IslandMode.Compacting, agent, "整理上下文", "正在压缩和整理会话记忆", tool, ts);
            }

            if (lowerEvent.IndexOf("postcompact") >= 0)
            {
                return Snapshot(IslandMode.Thinking, agent, "恢复思考", "上下文整理完成，继续推进任务", tool, ts);
            }

            if (lowerEvent == "stop" || lowerEvent == "sessionend" || lowerEvent.IndexOf("turn_completed") >= 0)
            {
                return Snapshot(IslandMode.Done, agent, "已完成", "任务结束，等待你的下一句", tool, ts);
            }

            if (lowerEvent.IndexOf("sessionstart") >= 0)
            {
                return Snapshot(IslandMode.Idle, agent, "已连接", "正在监听这个 Agent 的状态", tool, ts);
            }

            if (lowerEvent.IndexOf("userpromptsubmit") >= 0 || lowerEvent.IndexOf("turn_started") >= 0)
            {
                return Snapshot(IslandMode.Thinking, agent, "思考中", "正在理解需求并规划下一步", tool, ts);
            }

            if (lowerEvent.IndexOf("pretooluse") >= 0 || lowerEvent.IndexOf("item_started") >= 0)
            {
                if (LooksLikeEdit(haystack))
                {
                    return Snapshot(IslandMode.Editing, agent, "修改中", DescribeTool(tool, command, "正在写入或调整文件"), tool, ts);
                }

                if (LooksLikeCommand(haystack))
                {
                    return Snapshot(IslandMode.Running, agent, "执行中", DescribeTool(tool, command, "正在运行命令或脚本"), tool, ts);
                }

                return Snapshot(IslandMode.Thinking, agent, "检索中", DescribeTool(tool, command, "正在读取上下文或调用工具"), tool, ts);
            }

            if (lowerEvent.IndexOf("posttooluse") >= 0 || lowerEvent.IndexOf("posttoolbatch") >= 0 || lowerEvent.IndexOf("item_completed") >= 0)
            {
                return Snapshot(IslandMode.Thinking, agent, "处理结果", DescribeTool(tool, command, "正在吸收工具输出"), tool, ts);
            }

            if (lowerEvent.IndexOf("subagentstart") >= 0 || lowerEvent.IndexOf("taskcreated") >= 0)
            {
                return Snapshot(IslandMode.Thinking, agent, "分身工作中", FirstNonEmpty(message, "子任务已经启动"), tool, ts);
            }

            if (lowerEvent.IndexOf("subagentstop") >= 0 || lowerEvent.IndexOf("taskcompleted") >= 0)
            {
                return Snapshot(IslandMode.Thinking, agent, "合并结果", FirstNonEmpty(message, "子任务已完成，正在汇总"), tool, ts);
            }

            return Snapshot(IslandMode.Thinking, agent, "工作中", FirstNonEmpty(message, eventName), tool, ts);
        }

        private static StatusSnapshot Snapshot(IslandMode mode, string agent, string title, string detail, string tool, DateTimeOffset ts)
        {
            var displayAgent = DisplayAgentName(agent);
            return new StatusSnapshot(mode, displayAgent, title, detail, tool, ts);
        }

        private static string DisplayAgentName(string agent)
        {
            if (string.IsNullOrWhiteSpace(agent))
            {
                return "AGENT";
            }

            var normalized = agent.Trim().Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
            if (normalized == "codex")
            {
                return "CODEX";
            }
            if (normalized == "claude" || normalized == "claudecode")
            {
                return "CLAUDE CODE";
            }

            var displayAgent = agent.Trim().ToUpperInvariant();
            return displayAgent.Length > 12 ? displayAgent.Substring(0, 12) : displayAgent;
        }

        private static bool LooksLikeEdit(string value)
        {
            return value.IndexOf("edit") >= 0 ||
                   value.IndexOf("write") >= 0 ||
                   value.IndexOf("multiedit") >= 0 ||
                   value.IndexOf("apply_patch") >= 0 ||
                   value.IndexOf("filechange") >= 0 ||
                   value.IndexOf("patch") >= 0 ||
                   value.IndexOf("notebookedit") >= 0;
        }

        private static bool LooksLikeCommand(string value)
        {
            return value.IndexOf("bash") >= 0 ||
                   value.IndexOf("shell") >= 0 ||
                   value.IndexOf("command") >= 0 ||
                   value.IndexOf("powershell") >= 0 ||
                   value.IndexOf("terminal") >= 0 ||
                   value.IndexOf("exec") >= 0;
        }

        private static string DescribeTool(string tool, string command, string fallback)
        {
            var label = FirstNonEmpty(tool, command);
            if (string.IsNullOrWhiteSpace(label))
            {
                return fallback;
            }

            label = label.Replace("\r", " ").Replace("\n", " ").Trim();
            if (label.Length > 52)
            {
                label = label.Substring(0, 49) + "...";
            }

            return label;
        }

        private static string DescribeAttention(string tool, string command, string message, string fallback)
        {
            var detail = FirstNonEmpty(message, DescribeTool(tool, command, null), fallback);
            if (string.IsNullOrWhiteSpace(detail))
            {
                return fallback;
            }

            detail = detail.Replace("\r", " ").Replace("\n", " ").Trim();
            if (detail.Length > 58)
            {
                detail = detail.Substring(0, 55) + "...";
            }

            return detail;
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
    }
}
