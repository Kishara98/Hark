using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hark.Core
{
    /// <summary>Builds the answers map Claude Code expects: question text to label, labels joined with ", ", or own text.</summary>
    public static class AnswerBuilder
    {
        /// <returns>The answers, or null while a question has no answer.</returns>
        public static IReadOnlyDictionary<string, string> Build(
            IReadOnlyList<AskQuestion> questions, IReadOnlyList<ISet<string>> chosen, IReadOnlyList<string> ownText)
        {
            var answers = new Dictionary<string, string>();
            for (var i = 0; i < questions.Count; i++)
            {
                var own = i < ownText.Count ? (ownText[i] ?? "").Trim() : "";
                var picked = i < chosen.Count && chosen[i] != null
                    ? questions[i].Options.Where(chosen[i].Contains).ToList()
                    : new List<string>();
                var answer = own.Length > 0 ? own : string.Join(", ", picked);
                if (answer.Length == 0 || string.IsNullOrEmpty(questions[i].Text))
                {
                    return null;
                }
                answers[questions[i].Text] = answer;
            }
            return answers.Count > 0 ? answers : null;
        }
    }

    public enum PickResult { Stay, Next, Send }

    /// <summary>What the user picked or typed on a question card, one question (step) at a time.</summary>
    public sealed class AnswerDraft
    {
        private readonly List<ISet<string>> _chosen = new List<ISet<string>>();
        private readonly List<string> _own = new List<string>();

        public AnswerDraft(IReadOnlyList<AskQuestion> questions)
        {
            Questions = questions;
            foreach (var unused in questions)
            {
                _chosen.Add(new HashSet<string>());
                _own.Add("");
            }
        }

        public IReadOnlyList<AskQuestion> Questions { get; }
        public IReadOnlyList<ISet<string>> Chosen => _chosen;
        public IReadOnlyList<string> Own => _own;
        public int Step { get; private set; }
        public bool IsLast => Step >= Questions.Count - 1;
        public bool Expanded { get; set; }

        /// <summary>Set when the own answer box was just opened, so the card can give it the keyboard.</summary>
        public bool FocusOwn { get; set; }

        /// <summary>Questions whose own answer box is open.</summary>
        public ISet<int> OwnOpen { get; } = new HashSet<int>();

        /// <summary>The finished answers, or null while a question is still open.</summary>
        public IReadOnlyDictionary<string, string> Answers => AnswerBuilder.Build(Questions, _chosen, _own);

        /// <summary>Single choice: picks the label, then moves on (or sends on the last step). Multi choice: toggles.</summary>
        public PickResult Pick(int index, string label)
        {
            var set = _chosen[index];
            if (Questions[index].Multi)
            {
                if (!set.Remove(label))
                {
                    set.Add(label);
                }
                return PickResult.Stay;
            }
            set.Clear();
            set.Add(label);
            if (index < Questions.Count - 1)
            {
                Step = index + 1;
                return PickResult.Next;
            }
            return Answers != null ? PickResult.Send : PickResult.Stay;
        }

        public void SetOwn(int index, string text) => _own[index] = text ?? "";

        public bool StepAnswered(int index) => _own[index].Trim().Length > 0 || _chosen[index].Count > 0;

        public void Next() => Step = Math.Min(Step + 1, Questions.Count - 1);

        public void Back() => Step = Math.Max(Step - 1, 0);
    }

    /// <summary>Talks to hooks/answer_hook.py through files in the Hark data folder.</summary>
    public sealed class AnswerChannel
    {
        private static readonly Regex SafeId = new Regex("^[A-Za-z0-9_-]{1,128}$");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // local file only, keeps answers readable
        };

        private readonly string _dataDir;

        public AnswerChannel(string dataDir)
        {
            _dataDir = dataDir;
        }

        /// <summary>True while the answer hook holds this question and waits for the card.</summary>
        public bool IsHeld(string toolUseId) =>
            IsSafe(toolUseId) && File.Exists(Path.Combine(_dataDir, "held", toolUseId + ".json"));

        public IReadOnlyList<string> HeldIds()
        {
            var dir = Path.Combine(_dataDir, "held");
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json").Select(Path.GetFileNameWithoutExtension).ToList()
                : new List<string>();
        }

        public void Send(string toolUseId, IReadOnlyDictionary<string, string> answers) =>
            Write(toolUseId, new Dictionary<string, object> { ["action"] = "answer", ["answers"] = answers });

        /// <summary>Lets the hook go: Claude Code shows its own question dialog.</summary>
        public void Release(string toolUseId) =>
            Write(toolUseId, new Dictionary<string, object> { ["action"] = "vscode" });

        public void SetPaused(bool paused)
        {
            var path = Path.Combine(_dataDir, "paused");
            if (paused)
            {
                Directory.CreateDirectory(_dataDir);
                File.WriteAllText(path, "");
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public void PurgeOld(DateTime nowUtc, TimeSpan maxAge)
        {
            foreach (var name in new[] { "answers", "held" })
            {
                var dir = Path.Combine(_dataDir, name);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                foreach (var path in Directory.GetFiles(dir).Where(p => nowUtc - File.GetLastWriteTimeUtc(p) > maxAge))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private void Write(string toolUseId, object body)
        {
            if (!IsSafe(toolUseId))
            {
                return;
            }
            var dir = Path.Combine(_dataDir, "answers");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, toolUseId + ".json");
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(body, JsonOptions));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        private static bool IsSafe(string id) => id != null && SafeId.IsMatch(id);
    }
}
