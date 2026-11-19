using System;
using System.Collections.Generic;

namespace Hark.Core
{
    public enum SessionStatus { Idle, Working, Waiting, Done, Stopped }

    public enum PendingKind { Question, Permission }

    /// <summary>Something a session waits for: an answer or a permission.</summary>
    public sealed class Pending
    {
        public PendingKind Kind { get; internal set; }

        /// <summary>WSL ts of the event that set it; only for ordering.</summary>
        public long Ts { get; internal set; }

        public DateTime SinceUtc { get; internal set; }
        public string AgentId { get; internal set; }
        public string Tool { get; internal set; }
        public string Summary { get; internal set; }
        public IReadOnlyList<AskQuestion> Questions { get; internal set; } = Array.Empty<AskQuestion>();

        /// <summary>The tool call a permission belongs to, when known.</summary>
        public string ToolUseId { get; internal set; }
    }

    internal sealed class OpenCall
    {
        public OpenCall(string toolUseId, string tool, long ts)
        {
            ToolUseId = toolUseId;
            Tool = tool;
            Ts = ts;
        }

        public string ToolUseId { get; }
        public string Tool { get; }
        public long Ts { get; }
    }

    public sealed class AgentInfo
    {
        public string AgentId { get; internal set; }
        public string AgentType { get; internal set; }
        public int Steps { get; internal set; }
        public string LastTool { get; internal set; }
        public DateTime StartedUtc { get; internal set; }
        public DateTime LastSeenUtc { get; internal set; }

        /// <summary>The main task: the description the agent was started with, else its type.</summary>
        public string Task { get; internal set; }

        /// <summary>Index into PetBook.Names.</summary>
        public int PetIndex { get; internal set; }

        public IReadOnlyList<TodoItem> Todos { get; internal set; } = Array.Empty<TodoItem>();

        internal readonly List<string> Recent = new List<string>();

        /// <summary>The task came from the agent's launch (or its earlier run), not the fallback to its type.</summary>
        internal bool TaskLinked;

        /// <summary>The last five step summaries, oldest first.</summary>
        public IReadOnlyList<string> RecentSteps => Recent;
    }

    public sealed class SessionInfo
    {
        internal readonly Dictionary<string, AgentInfo> AgentMap = new Dictionary<string, AgentInfo>();

        /// <summary>The last started, not yet finished tool call per agent ("" = main session).</summary>
        internal readonly Dictionary<string, OpenCall> OpenCalls = new Dictionary<string, OpenCall>();

        /// <summary>ts of the last prompt or stop: older prompts, stops and waiting items are ignored.</summary>
        internal long BoundaryTs;

        /// <summary>Agent launches not yet matched to a SubagentStart: (agent type, task).</summary>
        internal readonly List<KeyValuePair<string, string>> Launches = new List<KeyValuePair<string, string>>();

        /// <summary>Tasks of agents that handed back, newest last: a resumed agent (SendMessage) starts again under the same id.</summary>
        internal readonly List<KeyValuePair<string, string>> FinishedTasks = new List<KeyValuePair<string, string>>();

        public string SessionId { get; internal set; }
        public string Project { get; internal set; }
        public string Cwd { get; internal set; }
        public SourceKind Source { get; internal set; }
        public SessionStatus Status { get; internal set; }
        public Pending Pending { get; internal set; }
        public IReadOnlyDictionary<string, AgentInfo> Agents => AgentMap;
        public int AgentsDoneThisTurn { get; internal set; }
        public DateTime LastEventUtc { get; internal set; }
        public DateTime DoneUtc { get; internal set; }
        public string DoneText { get; internal set; }

        /// <summary>The main session's own to-do list for the current request.</summary>
        public IReadOnlyList<TodoItem> Todos { get; internal set; } = Array.Empty<TodoItem>();

        /// <summary>Tool calls of the main session in the current request.</summary>
        public int MainSteps { get; internal set; }

        /// <summary>When the current request started.</summary>
        public DateTime TaskStartedUtc { get; internal set; }

        internal readonly List<string> Recent = new List<string>();

        /// <summary>The main session's last five step summaries, oldest first.</summary>
        public IReadOnlyList<string> RecentSteps => Recent;
    }
}
