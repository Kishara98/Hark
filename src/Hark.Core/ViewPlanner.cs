using System;
using System.Collections.Generic;
using System.Linq;

namespace Hark.Core
{
    /// <summary>Turns the session state into cards and pill text. Copy: plan Global Constraints.</summary>
    public static class ViewPlanner
    {
        private const int MaxBody = 120;

        /// <summary>The StepStats key for a session's own requests.</summary>
        public const string MainTaskType = "main task";

        internal static readonly TimeSpan ShowWorkingAfter = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan HideQuietAfter = TimeSpan.FromMinutes(15);

        /// <summary>Every card that exists now, hidden ones included. Used to forget old snoozes.</summary>
        public static IReadOnlyList<Card> AllCards(WatchState state)
        {
            var waiting = new List<Card>();
            var finished = new List<Card>();
            foreach (var s in state.Sessions)
            {
                if (s.Pending != null)
                {
                    waiting.Add(PendingCard(s));
                }
                else if (s.Status == SessionStatus.Done || s.Status == SessionStatus.Stopped)
                {
                    finished.Add(FinishedCard(s));
                }
            }
            return waiting.OrderBy(c => c.SinceUtc).Concat(finished.OrderByDescending(c => c.SinceUtc)).ToList();
        }

        public static IReadOnlyList<Card> Cards(WatchState state, SnoozeBook book, DateTime nowUtc) =>
            AllCards(state).Where(c => !book.IsHidden(c.Id, nowUtc)).ToList();

        public static PillModel Pill(WatchState state) => Pill(state, null);

        /// <summary>With a time, sessions working without a to-do list count too (after 5 seconds), and quiet ones drop out.</summary>
        public static PillModel Pill(WatchState state, DateTime? nowUtc)
        {
            var pending = state.Sessions.Where(s => s.Pending != null).OrderBy(s => s.Pending.SinceUtc).ToList();
            var agents = state.Sessions.Sum(s => s.Agents.Count);
            var tasks = state.Sessions.Where(s => HasTask(s, nowUtc)).ToList();
            var pill = new PillModel
            {
                Visible = pending.Count > 0 || agents > 0 || tasks.Count > 0,
                TaskText = tasks.Count == 0 ? ""
                    : tasks.Count == 1 ? $"{Name(tasks[0])} · {(tasks[0].Todos.Count > 0 ? AgentProgress.ForTodos(tasks[0].Todos).Label : "working")}"
                    : $"{tasks.Count} tasks running",
                ShowOpen = pending.Count > 0,
                TargetSessionId = pending.FirstOrDefault()?.SessionId,
                AgentsText = agents == 0 ? "" : agents == 1 ? "1 agent running" : $"{agents} agents running",
                CloseKey = string.Join(",", pending.Select(s => $"{s.SessionId}:{s.Pending.Kind}:{s.Pending.SinceUtc.Ticks}")) + "|" + (agents > 0)
                    + "|" + string.Join(",", tasks.Select(s => s.SessionId + ":" + s.TaskStartedUtc.Ticks)),
            };
            if (pending.Count == 1)
            {
                var s = pending[0];
                pill.Text = Name(s) + (s.Pending.Kind == PendingKind.Question ? " needs your answer" : " needs your permission");
            }
            else if (pending.Count > 1)
            {
                pill.Text = $"{pending.Count} sessions need you";
            }
            else if (agents > 0)
            {
                pill.Text = "Agents working";
            }
            return pill;
        }

        /// <summary>Running agents of all sessions, oldest first, for the agent panel.</summary>
        public static IReadOnlyList<AgentRow> Agents(WatchState state, DateTime nowUtc) =>
            state.Sessions
                .SelectMany(s => s.Agents.Values.Select(a => new AgentRow
                {
                    AgentId = a.AgentId,
                    SessionId = s.SessionId,
                    Project = Name(s),
                    PetIndex = a.PetIndex,
                    PetName = PetBook.Names[a.PetIndex % PetBook.Names.Count],
                    AgentType = a.AgentType ?? "agent",
                    Task = a.Task ?? a.AgentType ?? "agent",
                    Elapsed = Elapsed(a.StartedUtc, nowUtc),
                    Progress = AgentProgress.For(a, state.Stats),
                    Todos = a.Todos,
                    RecentSteps = a.RecentSteps.ToList(),
                }))
                .OrderBy(r => state.Sessions.SelectMany(s => s.Agents.Values).First(a => a.AgentId == r.AgentId).StartedUtc)
                .ToList();

        /// <summary>The agent panel: per session its own running task (when it keeps a to-do list), then its agents.</summary>
        public static IReadOnlyList<AgentRow> PanelRows(WatchState state, DateTime nowUtc)
        {
            var rows = new List<AgentRow>();
            var agents = Agents(state, nowUtc);
            foreach (var s in state.Sessions)
            {
                if (HasTask(s, nowUtc))
                {
                    rows.Add(new AgentRow
                    {
                        AgentId = "task:" + s.SessionId,
                        SessionId = s.SessionId,
                        Project = Name(s),
                        PetIndex = -1,
                        PetName = Name(s),
                        AgentType = "main task",
                        Task = s.Todos.Count > 0 ? CurrentItem(s.Todos) : s.RecentSteps.LastOrDefault() ?? "Thinking",
                        Elapsed = Elapsed(s.TaskStartedUtc == default ? s.LastEventUtc : s.TaskStartedUtc, nowUtc),
                        Progress = s.Todos.Count > 0 ? AgentProgress.ForTodos(s.Todos) : AgentProgress.Estimate(s.MainSteps, state.Stats.Typical(MainTaskType)),
                        Todos = s.Todos,
                        RecentSteps = s.RecentSteps.ToList(),
                        IsMainTask = true,
                    });
                }
                rows.AddRange(agents.Where(a => a.SessionId == s.SessionId));
            }
            return rows;
        }

        private static bool HasTask(SessionInfo s, DateTime? nowUtc)
        {
            if (s.Status != SessionStatus.Working && s.Status != SessionStatus.Waiting)
            {
                return false;
            }
            if (nowUtc.HasValue && nowUtc.Value - s.LastEventUtc > HideQuietAfter)
            {
                return false; // no event for 15 minutes: the session is gone or stuck, not working
            }
            if (s.Todos.Count > 0)
            {
                return true;
            }
            return nowUtc.HasValue && s.TaskStartedUtc != default && nowUtc.Value - s.TaskStartedUtc >= ShowWorkingAfter;
        }

        private static string CurrentItem(IReadOnlyList<TodoItem> todos) =>
            (todos.FirstOrDefault(t => t.Status == "in_progress")
             ?? todos.FirstOrDefault(t => t.Status == "pending")
             ?? todos.Last()).Text;

        public static string Elapsed(DateTime sinceUtc, DateTime nowUtc)
        {
            var s = (int)Math.Max(0, (nowUtc - sinceUtc).TotalSeconds);
            if (s < 60)
            {
                return $"{s}s";
            }
            if (s < 3600)
            {
                return $"{s / 60}m {s % 60:00}s";
            }
            return $"{s / 3600}h {(s % 3600) / 60:00}m";
        }

        public static string Ago(DateTime sinceUtc, DateTime nowUtc)
        {
            var seconds = Math.Max(0, (nowUtc - sinceUtc).TotalSeconds);
            if (seconds < 60)
            {
                return "now";
            }
            if (seconds < 3600)
            {
                var minutes = (int)(seconds / 60);
                return minutes == 1 ? "1 min ago" : $"{minutes} min ago";
            }
            return $"{(int)(seconds / 3600)} h ago";
        }

        private static Card PendingCard(SessionInfo s)
        {
            var p = s.Pending;
            var card = Base(s, p.Kind == PendingKind.Question ? CardKind.Question : CardKind.Permission, p.SinceUtc);
            if (p.Kind == PendingKind.Question)
            {
                card.Chip = "Question";
                var first = p.Questions.FirstOrDefault();
                card.Body = first?.Text ?? p.Summary ?? "Claude has a question";
                if (p.Questions.Count > 1)
                {
                    card.Body += $" (+{p.Questions.Count - 1} more)";
                }
                card.Options = first?.Options ?? Array.Empty<string>();
                card.AnswerId = p.ToolUseId;
                card.Questions = p.Questions;
            }
            else
            {
                card.Chip = "Permission";
                card.Body = p.Summary ?? "Claude needs your permission";
            }
            return card;
        }

        private static Card FinishedCard(SessionInfo s)
        {
            var stopped = s.Status == SessionStatus.Stopped;
            var card = Base(s, stopped ? CardKind.Stopped : CardKind.Done, s.DoneUtc);
            card.Asks = !stopped && s.DoneAsks;
            card.Chip = stopped ? "Stopped" : card.Asks ? "Asks you" : "Done";
            card.Body = FirstLine(s.DoneText) ?? (stopped ? "Stopped" : "Finished");
            if (!stopped && s.AgentsDoneThisTurn > 0)
            {
                card.Meta += s.AgentsDoneThisTurn == 1 ? " · 1 agent done" : $" · {s.AgentsDoneThisTurn} agents done";
            }
            return card;
        }

        private static Card Base(SessionInfo s, CardKind kind, DateTime since) => new Card
        {
            Id = $"{s.SessionId}:{kind}:{since.Ticks}",
            Kind = kind,
            SessionId = s.SessionId,
            Source = s.Source,
            Project = Name(s),
            SinceUtc = since,
            Meta = Name(s) + (s.Source == SourceKind.VsCode ? " · VS Code" : " · Claude CLI"),
            OpenLabel = s.Source == SourceKind.VsCode ? "Open VS Code" : "Open terminal",
        };

        private static string Name(SessionInfo s) => string.IsNullOrEmpty(s.Project) ? "Claude" : s.Project;

        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            var line = text.Split('\n')[0].Trim();
            return line.Length > MaxBody ? line.Substring(0, MaxBody - 3) + "..." : line;
        }
    }
}
