using System;
using System.Collections.Generic;
using System.Linq;

namespace Hark.Core
{
    /// <summary>Folds hook events into per session state. Rules: plan Task 4 table, plus the review fixes below.</summary>
    /// <remarks>
    /// Async hooks stamp ts when their process starts, so events can be a few ms out of order and can land in
    /// different read batches. Guards: a call id ties a permission card to its own tool call; a per session
    /// boundary (last prompt or stop) drops older "set" events; ended sessions are remembered for 10 minutes.
    /// </remarks>
    public sealed class WatchState
    {
        private const string AskTool = "AskUserQuestion";
        private const long SameCallMs = 2000;
        private static readonly TimeSpan WaitingMaxIdle = TimeSpan.FromHours(12);
        private static readonly TimeSpan AgentMaxIdle = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan EndedMemory = TimeSpan.FromMinutes(10);

        private const int KeepLaunches = 10;
        private const int KeepFinishedTasks = 20;
        private const int KeepRecent = 5;

        private readonly List<SessionInfo> _sessions = new List<SessionInfo>();
        private readonly PetBook _pets = new PetBook();

        public WatchState(StepStats stats = null)
        {
            Stats = stats ?? new StepStats();
        }

        /// <summary>Typical step counts per agent type, learned from finished agents.</summary>
        public StepStats Stats { get; }
        private readonly Dictionary<string, KeyValuePair<long, DateTime>> _ended = new Dictionary<string, KeyValuePair<long, DateTime>>();

        public IReadOnlyList<SessionInfo> Sessions => _sessions;

        public void ApplyBatch(IEnumerable<HookEvent> batch)
        {
            foreach (var e in batch.OrderBy(e => e.Ts))
            {
                Apply(e);
            }
        }

        public void Apply(HookEvent e)
        {
            if (e == null || string.IsNullOrEmpty(e.SessionId))
            {
                return;
            }
            if (_ended.TryGetValue(e.SessionId, out var end))
            {
                if (e.Ts <= end.Key)
                {
                    return; // late event of a session that already ended
                }
                _ended.Remove(e.SessionId);
            }
            var s = _sessions.FirstOrDefault(x => x.SessionId == e.SessionId);
            if (e.Event == "SessionEnd")
            {
                if (s != null)
                {
                    ReleasePets(s.AgentMap.Values);
                    _sessions.Remove(s);
                }
                _ended[e.SessionId] = new KeyValuePair<long, DateTime>(e.Ts, e.ReceivedUtc);
                return;
            }
            if (s == null)
            {
                s = new SessionInfo { SessionId = e.SessionId, Status = SessionStatus.Idle };
                _sessions.Add(s);
            }
            s.LastEventUtc = e.ReceivedUtc;
            s.Source = e.Source;
            // Claude's cwd moves when it runs cd; the session keeps the folder it started in.
            if (string.IsNullOrEmpty(s.Project) && !string.IsNullOrEmpty(e.Project))
            {
                s.Project = e.Project;
            }
            if (!string.IsNullOrEmpty(e.Cwd))
            {
                s.Cwd = e.Cwd;
            }

            switch (e.Event)
            {
                case "SessionStart":
                    s.Status = SessionStatus.Idle;
                    break;
                case "UserPromptSubmit":
                    if (e.Ts < s.BoundaryTs)
                    {
                        break;
                    }
                    s.BoundaryTs = e.Ts;
                    s.Pending = null;
                    s.Status = SessionStatus.Working;
                    s.AgentsDoneThisTurn = 0;
                    s.DoneText = null;
                    s.DoneAsks = false;
                    s.Todos = Array.Empty<TodoItem>();
                    s.Recent.Clear();
                    s.MainSteps = 0;
                    s.TaskStartedUtc = e.ReceivedUtc;
                    break;
                case "PreToolUse":
                    PreToolUse(s, e);
                    break;
                case "PostToolUse":
                case "PostToolUseFailure":
                    PostToolUse(s, e);
                    break;
                case "PermissionRequest":
                    PermissionRequest(s, e);
                    break;
                case "Notification":
                    if (s.Pending == null && e.NotificationType == "permission_prompt")
                    {
                        SetPending(s, e, PendingKind.Permission, e.Message, agentAware: false);
                    }
                    else if (s.Pending == null && e.NotificationType == "elicitation_dialog")
                    {
                        SetPending(s, e, PendingKind.Question, e.Message, agentAware: false);
                    }
                    break;
                case "SubagentStart":
                    if (!string.IsNullOrEmpty(e.AgentId))
                    {
                        StartAgent(s, e);
                    }
                    break;
                case "SubagentStop":
                    if (!string.IsNullOrEmpty(e.AgentId) && s.AgentMap.TryGetValue(e.AgentId, out var finished))
                    {
                        Stats.Record(finished.AgentType ?? e.AgentType, finished.Steps);
                        _pets.Release(e.AgentId);
                        s.AgentMap.Remove(e.AgentId);
                        if (finished.TaskLinked)
                        {
                            s.FinishedTasks.RemoveAll(t => t.Key == e.AgentId);
                            s.FinishedTasks.Add(new KeyValuePair<string, string>(e.AgentId, finished.Task));
                            if (s.FinishedTasks.Count > KeepFinishedTasks)
                            {
                                s.FinishedTasks.RemoveAt(0);
                            }
                        }
                        // Only agents Hark saw start or work: Claude Code also stops hidden helper agents it never started.
                        s.AgentsDoneThisTurn++;
                    }
                    break;
                case "Stop":
                case "StopFailure":
                    if (e.Ts < s.BoundaryTs)
                    {
                        break;
                    }
                    s.BoundaryTs = e.Ts;
                    // Only turns the pill showed (working 5 s or more) teach the usual length; quick replies would pull it down.
                    if (s.MainSteps > 0 && s.TaskStartedUtc != default && e.ReceivedUtc - s.TaskStartedUtc >= ViewPlanner.ShowWorkingAfter)
                    {
                        Stats.Record(ViewPlanner.MainTaskType, s.MainSteps);
                    }
                    if (e.Event == "Stop")
                    {
                        Finish(s, e, SessionStatus.Done, e.LastMessage);
                        s.DoneAsks = s.Status == SessionStatus.Done && e.Asks;
                    }
                    else
                    {
                        Finish(s, e, SessionStatus.Stopped, e.LastMessage ?? e.Error);
                    }
                    break;
            }
        }

        public void DropStale(DateTime nowUtc) => DropStale(nowUtc, TimeSpan.FromHours(2));

        /// <summary>Drops quiet sessions (waiting ones after 12 h), agents with no step for 30 minutes and old end marks.</summary>
        public void DropStale(DateTime nowUtc, TimeSpan maxIdle)
        {
            var waitingMax = maxIdle > WaitingMaxIdle ? maxIdle : WaitingMaxIdle;
            foreach (var gone in _sessions.Where(s => nowUtc - s.LastEventUtc > (s.Pending != null ? waitingMax : maxIdle)).ToList())
            {
                ReleasePets(gone.AgentMap.Values);
                _sessions.Remove(gone);
            }
            foreach (var s in _sessions)
            {
                foreach (var id in s.AgentMap.Values.Where(a => nowUtc - a.LastSeenUtc > AgentMaxIdle).Select(a => a.AgentId).ToList())
                {
                    _pets.Release(id);
                    s.AgentMap.Remove(id);
                }
            }
            foreach (var id in _ended.Where(x => nowUtc - x.Value.Value > EndedMemory).Select(x => x.Key).ToList())
            {
                _ended.Remove(id);
            }
        }

        private static string Key(HookEvent e) => e.AgentId ?? "";

        private AgentInfo NewAgent(HookEvent e, string task) => new AgentInfo
        {
            AgentId = e.AgentId,
            AgentType = e.AgentType,
            StartedUtc = e.ReceivedUtc,
            LastSeenUtc = e.ReceivedUtc,
            Task = task ?? e.AgentType,
            PetIndex = _pets.Assign(e.AgentId),
        };

        private void ReleasePets(IEnumerable<AgentInfo> agents)
        {
            foreach (var a in agents)
            {
                _pets.Release(a.AgentId);
            }
        }

        /// <summary>
        /// A start for an agent already on the panel keeps its steps (its start was read after its first step, or came twice).
        /// A resumed agent gets back the task it had; only a new agent takes a launch.
        /// </summary>
        private void StartAgent(SessionInfo s, HookEvent e)
        {
            if (!s.AgentMap.TryGetValue(e.AgentId, out var agent))
            {
                agent = NewAgent(e, null);
                s.AgentMap[e.AgentId] = agent;
            }
            if (!string.IsNullOrEmpty(e.AgentTask))
            {
                // The real description: the launch it came from is used up, so it can't name another agent.
                var own = s.Launches.FindIndex(l => l.Key == e.AgentType && l.Value == e.AgentTask);
                if (own >= 0)
                {
                    s.Launches.RemoveAt(own);
                }
                Name(agent, e.AgentTask);
                return;
            }
            if (agent.TaskLinked)
            {
                return;
            }
            var earlier = s.FinishedTasks.FindLastIndex(t => t.Key == e.AgentId);
            var task = earlier >= 0 ? s.FinishedTasks[earlier].Value : TakeLaunch(s, e.AgentType);
            if (task != null)
            {
                Name(agent, task);
            }
        }

        private static void Name(AgentInfo agent, string task)
        {
            agent.Task = task;
            agent.TaskLinked = true;
        }

        private static string TakeLaunch(SessionInfo s, string agentType)
        {
            var i = s.Launches.FindIndex(l => l.Key == agentType);
            if (i < 0)
            {
                return null;
            }
            var task = s.Launches[i].Value;
            s.Launches.RemoveAt(i);
            return task;
        }

        private void PreToolUse(SessionInfo s, HookEvent e)
        {
            var key = Key(e);
            if (key.Length == 0)
            {
                s.MainSteps++;
                if (s.TaskStartedUtc == default)
                {
                    s.TaskStartedUtc = e.ReceivedUtc; // Hark started mid task and missed the prompt
                }
                s.Recent.Add(e.Summary);
                if (s.Recent.Count > KeepRecent)
                {
                    s.Recent.RemoveAt(0);
                }
                if (e.Tool == "TodoWrite" && e.Todos.Count > 0)
                {
                    s.Todos = e.Todos;
                }
            }
            if (key.Length == 0 && !string.IsNullOrEmpty(e.SpawnType))
            {
                var summary = e.Summary ?? "";
                var colon = summary.IndexOf(": ", StringComparison.Ordinal);
                s.Launches.Add(new KeyValuePair<string, string>(e.SpawnType, colon >= 0 ? summary.Substring(colon + 2) : e.SpawnType));
                if (s.Launches.Count > KeepLaunches)
                {
                    s.Launches.RemoveAt(0);
                }
            }
            if (key.Length == 0 && e.Tool == AskTool)
            {
                if (SetPending(s, e, PendingKind.Question, e.Questions.FirstOrDefault()?.Text))
                {
                    s.Pending.Questions = e.Questions;
                    s.Pending.ToolUseId = e.ToolUseId; // the answer hook holds the question under this id
                }
                s.OpenCalls[key] = new OpenCall(e.ToolUseId, e.Tool, e.Ts);
                return;
            }
            if (key.Length > 0)
            {
                if (!s.AgentMap.TryGetValue(e.AgentId, out var agent))
                {
                    agent = NewAgent(e, null);
                    s.AgentMap[e.AgentId] = agent;
                }
                if (!string.IsNullOrEmpty(e.AgentTask))
                {
                    Name(agent, e.AgentTask); // the agent's own description beats a launch matched by type
                }
                agent.Steps++;
                agent.LastTool = e.Summary;
                agent.LastSeenUtc = e.ReceivedUtc;
                agent.Recent.Add(e.Summary);
                if (agent.Recent.Count > KeepRecent)
                {
                    agent.Recent.RemoveAt(0);
                }
                if (e.Tool == "TodoWrite" && e.Todos.Count > 0)
                {
                    agent.Todos = e.Todos;
                }
            }
            var p = s.Pending;
            if (p != null && (p.AgentId ?? "") == key)
            {
                if (p.ToolUseId != null && p.ToolUseId == e.ToolUseId)
                {
                    // the same call: still waiting for the permission
                }
                else if (p.ToolUseId == null && p.Kind == PendingKind.Permission && p.Tool == e.Tool && Math.Abs(e.Ts - p.Ts) <= SameCallMs)
                {
                    p.ToolUseId = e.ToolUseId; // the PreToolUse of this call was stamped after its PermissionRequest
                }
                else
                {
                    ClearIfNewer(s, e);
                }
            }
            else if (p == null && key.Length == 0)
            {
                s.Status = SessionStatus.Working;
            }
            s.OpenCalls[key] = new OpenCall(e.ToolUseId, e.Tool, e.Ts);
        }

        private static void PostToolUse(SessionInfo s, HookEvent e)
        {
            var key = Key(e);
            if (s.OpenCalls.TryGetValue(key, out var open) && open.ToolUseId == e.ToolUseId)
            {
                s.OpenCalls.Remove(key);
            }
            var p = s.Pending;
            if (p == null)
            {
                return;
            }
            if (p.Kind == PendingKind.Question && key.Length == 0 && e.Tool == AskTool)
            {
                ClearIfNewer(s, e);
            }
            else if (p.Kind == PendingKind.Permission && (p.AgentId ?? "") == key)
            {
                if (p.ToolUseId != null)
                {
                    if (p.ToolUseId == e.ToolUseId)
                    {
                        Clear(s);
                    }
                }
                else if (p.Tool == e.Tool)
                {
                    ClearIfNewer(s, e);
                }
            }
        }

        private static void PermissionRequest(SessionInfo s, HookEvent e)
        {
            var key = Key(e);
            if (key.Length == 0 && e.Tool == AskTool)
            {
                // VS Code sends a permission request for every question: it is still a question.
                if (s.Pending?.Kind != PendingKind.Question && SetPending(s, e, PendingKind.Question, e.Questions.FirstOrDefault()?.Text))
                {
                    s.Pending.Questions = e.Questions;
                    if (s.OpenCalls.TryGetValue(key, out var ask) && ask.Tool == AskTool)
                    {
                        s.Pending.ToolUseId = ask.ToolUseId;
                    }
                }
                return;
            }
            if (SetPending(s, e, PendingKind.Permission, e.Summary)
                && s.OpenCalls.TryGetValue(key, out var open)
                && open.Tool == e.Tool
                && Math.Abs(e.Ts - open.Ts) <= SameCallMs)
            {
                s.Pending.ToolUseId = open.ToolUseId;
            }
        }

        private static bool SetPending(SessionInfo s, HookEvent e, PendingKind kind, string summary, bool agentAware = true)
        {
            if (e.Ts < s.BoundaryTs)
            {
                return false; // older than the last prompt or stop: already answered or gone
            }
            s.Pending = new Pending
            {
                Kind = kind,
                Ts = e.Ts,
                SinceUtc = e.ReceivedUtc,
                AgentId = agentAware ? e.AgentId : null,
                Tool = e.Tool,
                Summary = summary,
            };
            s.Status = SessionStatus.Waiting;
            return true;
        }

        private static void ClearIfNewer(SessionInfo s, HookEvent e)
        {
            if (s.Pending != null && e.Ts >= s.Pending.Ts)
            {
                Clear(s);
            }
        }

        private static void Clear(SessionInfo s)
        {
            s.Pending = null;
            s.Status = SessionStatus.Working;
        }

        private static void Finish(SessionInfo s, HookEvent e, SessionStatus status, string text)
        {
            ClearIfNewer(s, e);
            if (s.Pending != null)
            {
                return;
            }
            s.Status = status;
            s.DoneUtc = e.ReceivedUtc;
            s.DoneText = text;
        }
    }
}
