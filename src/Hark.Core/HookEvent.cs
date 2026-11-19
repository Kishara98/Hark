using System;
using System.Collections.Generic;

namespace Hark.Core
{
    public enum SourceKind { Cli, VsCode }

    public sealed class AskQuestion
    {
        public string Text { get; set; }
        public string Header { get; set; }
        public IReadOnlyList<string> Options { get; set; } = Array.Empty<string>();

        /// <summary>One description per option (may be empty or shorter than Options).</summary>
        public IReadOnlyList<string> Descriptions { get; set; } = Array.Empty<string>();

        public bool Multi { get; set; }
    }

    public sealed class TodoItem
    {
        public string Text { get; set; }

        /// <summary>pending, in_progress or completed.</summary>
        public string Status { get; set; }
    }

    /// <summary>One event written by the hook recorder (hooks/record_event.py).</summary>
    public sealed class HookEvent
    {
        /// <summary>Epoch ms from the WSL clock. Only used to order events.</summary>
        public long Ts { get; set; }

        /// <summary>When Hark read the event (Windows clock). Used for all shown times.</summary>
        public DateTime ReceivedUtc { get; set; }

        public string Event { get; set; }
        public string SessionId { get; set; }
        public string Project { get; set; }
        public string Cwd { get; set; }
        public SourceKind Source { get; set; }
        public string AgentId { get; set; }
        public string AgentType { get; set; }

        /// <summary>The agent's description, read by the recorder from the meta file next to the agent's transcript.</summary>
        public string AgentTask { get; set; }
        public string Tool { get; set; }
        public string ToolUseId { get; set; }
        public string Summary { get; set; }
        public string NotificationType { get; set; }
        public string Message { get; set; }
        public IReadOnlyList<AskQuestion> Questions { get; set; } = Array.Empty<AskQuestion>();
        public string Error { get; set; }
        public string LastMessage { get; set; }

        /// <summary>Stop only: the reply ends with a question to the user.</summary>
        public bool Asks { get; set; }

        /// <summary>Agent launches (main PreToolUse of Agent): the agent type asked for.</summary>
        public string SpawnType { get; set; }

        /// <summary>TodoWrite calls: the to-do list.</summary>
        public IReadOnlyList<TodoItem> Todos { get; set; } = Array.Empty<TodoItem>();
    }
}
