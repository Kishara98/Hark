using System;
using System.Collections.Generic;

namespace Hark.Core
{
    public enum CardKind { Question, Permission, Done, Stopped }

    /// <summary>One popup card, already worded for the screen.</summary>
    public sealed class Card
    {
        public string Id { get; internal set; }
        public CardKind Kind { get; internal set; }
        public string SessionId { get; internal set; }
        public SourceKind Source { get; internal set; }
        public string Project { get; internal set; }
        public DateTime SinceUtc { get; internal set; }
        public string Chip { get; internal set; }
        public string Meta { get; internal set; }
        public string Body { get; internal set; }
        public IReadOnlyList<string> Options { get; internal set; } = Array.Empty<string>();
        public string OpenLabel { get; internal set; }

        /// <summary>Question cards: the tool call id the answer hook holds the question under.</summary>
        public string AnswerId { get; internal set; }

        /// <summary>Question cards: every question with its options and descriptions.</summary>
        public IReadOnlyList<AskQuestion> Questions { get; internal set; } = Array.Empty<AskQuestion>();
    }

    /// <summary>What the top pill shows.</summary>
    public sealed class PillModel
    {
        public bool Visible { get; internal set; }
        public bool ShowOpen { get; internal set; }
        public string Text { get; internal set; } = "";
        public string AgentsText { get; internal set; } = "";

        /// <summary>Running main tasks with a to-do list: "projects · 2 of 5 done" or "2 tasks running".</summary>
        public string TaskText { get; internal set; } = "";
        public string TargetSessionId { get; internal set; }

        /// <summary>Same while nothing new happens: the waiting items and whether agents run.</summary>
        public string CloseKey { get; internal set; } = "";
    }
}
