using System;

namespace AgentIsland
{
    public enum IslandMode
    {
        Idle,
        Thinking,
        Editing,
        Running,
        Waiting,
        Done,
        Error,
        Compacting
    }

    public sealed class StatusSnapshot
    {
        public StatusSnapshot(IslandMode mode, string agent, string title, string detail, string tool, DateTimeOffset timestamp)
            : this(mode, agent, title, detail, tool, timestamp, null, null, 0, null)
        {
        }

        private StatusSnapshot(
            IslandMode mode,
            string agent,
            string title,
            string detail,
            string tool,
            DateTimeOffset timestamp,
            string conversationName,
            string sessionKey,
            int activeConversationCount,
            StatusSnapshot[] conversations)
        {
            Mode = mode;
            Agent = agent;
            Title = title;
            Detail = detail;
            Tool = tool;
            Timestamp = timestamp;
            ConversationName = conversationName;
            SessionKey = sessionKey;
            ActiveConversationCount = activeConversationCount;
            Conversations = conversations ?? new StatusSnapshot[0];
        }

        public IslandMode Mode { get; private set; }
        public string Agent { get; private set; }
        public string Title { get; private set; }
        public string Detail { get; private set; }
        public string Tool { get; private set; }
        public DateTimeOffset Timestamp { get; private set; }
        public string ConversationName { get; private set; }
        public string SessionKey { get; private set; }
        public int ActiveConversationCount { get; private set; }
        public StatusSnapshot[] Conversations { get; private set; }

        public bool HasMultipleConversations
        {
            get { return ActiveConversationCount > 1; }
        }

        public StatusSnapshot WithSessionContext(string sessionKey, int activeConversationCount, StatusSnapshot[] conversations)
        {
            return new StatusSnapshot(
                Mode,
                Agent,
                Title,
                Detail,
                Tool,
                Timestamp,
                ConversationName,
                sessionKey,
                activeConversationCount,
                conversations);
        }

        public StatusSnapshot WithConversationName(string conversationName)
        {
            return new StatusSnapshot(
                Mode,
                Agent,
                Title,
                Detail,
                Tool,
                Timestamp,
                conversationName,
                SessionKey,
                ActiveConversationCount,
                Conversations);
        }

        public static StatusSnapshot Idle()
        {
            return new StatusSnapshot(
                IslandMode.Idle,
                "AGENT",
                "待命",
                "等待 Codex / Claude Code 的下一次任务",
                null,
                DateTimeOffset.Now);
        }
    }
}
