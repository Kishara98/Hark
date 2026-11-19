using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hark.Core
{
    // The Claude Code hooks for Claude Code running natively on Windows (HarkHook.exe). They do exactly what
    // hooks/record_event.py, answer_hook.py, agent_hook.py and install_hooks.py do for Claude Code in WSL;
    // a change to one side needs the same change on the other.

    /// <summary>Text shortening shared by the recorder and the answer hook (same as _cut and _first_line in Python).</summary>
    public static class HookText
    {
        public const int MaxText = 200;
        private static readonly Regex Space = new Regex(@"\s+");

        /// <summary>Spaces and line breaks collapsed to one space, at most 200 characters.</summary>
        public static string Cut(string text)
        {
            text = Space.Replace((text ?? "").Trim(), " ");
            if (text.Length <= MaxText)
            {
                return text;
            }
            var keep = MaxText - 3;
            if (char.IsHighSurrogate(text[keep - 1]))
            {
                keep--;
            }
            return text.Substring(0, keep) + "...";
        }

        // Likely secrets become *** before anything is written or shown. Same patterns as redact() in
        // hooks/record_event.py; tests/fixtures/redact_cases.json is the shared test table.
        private const string SecretWords = "(?:password|passwd|secret|token|api[_-]?key|apikey|access[_-]?key|authorization|credential)";

        private static readonly (Regex Pattern, MatchEvaluator Replace)[] Redactions =
        {
            (new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)"), m => "***"),
            (new Regex(@"(://[^/\s:@]+:)[^/\s@]+(@)"), m => m.Groups[1].Value + "***" + m.Groups[2].Value),
            (new Regex(@"(?i)([A-Za-z0-9_.-]*" + SecretWords + @"[A-Za-z0-9_.-]*[""']?\s*[:=]\s*)((?:bearer|basic|token)\s+)?(""[^""]*""|'[^']*'|[^\s""'&;|,}]+)"),
                m => m.Groups[1].Value + m.Groups[2].Value + "***"),
            (new Regex(@"(?i)(--?(?:password|passwd|token|secret|api-key|apikey)\s+)(""[^""]*""|'[^']*'|[^\s""']+)"), m => m.Groups[1].Value + "***"),
            (new Regex(@"(?i)\b(bearer|basic)\s+[A-Za-z0-9._~+/=-]{8,}"), m => m.Groups[1].Value + " ***"),
            (new Regex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16}"
                + @"|xox[baprs]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9_-]{20,}|AIza[0-9A-Za-z_-]{35}"
                + @"|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})"), m => "***"),
        };

        public static string Redact(string text)
        {
            text = text ?? "";
            foreach (var (pattern, replace) in Redactions)
            {
                text = pattern.Replace(text, replace);
            }
            return text;
        }

        /// <summary>Secrets hidden first, then cut: a token past the first 200 characters must not survive half cut.</summary>
        public static string Clean(string text) => Cut(Redact(text));

        /// <summary>
        /// True when one of the last 3 lines of the reply (code blocks left out) ends with a question to the user.
        /// Same rule as _asks in hooks/record_event.py; tests/fixtures/asks_cases.json is the shared table.
        /// </summary>
        public static bool Asks(string text)
        {
            var lines = new List<string>();
            var inCode = false;
            foreach (var raw in (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                var line = raw.Trim();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inCode = !inCode;
                }
                else if (line.Length > 0 && !inCode)
                {
                    lines.Add(line);
                }
            }
            return lines.Skip(Math.Max(0, lines.Count - 3)).Any(l => l.TrimEnd(')', ']', '}', '"', '\'', '*', '_', '`', ' ').EndsWith("?", StringComparison.Ordinal));
        }

        /// <summary>The first line of a reply that reads as a sentence: no headings, list marks, bold or backticks.</summary>
        public static string FirstLine(string text)
        {
            var lines = (text ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var prose = lines.Where(l => !l.StartsWith("#", StringComparison.Ordinal)).ToList();
            var line = prose.Count > 0 ? prose[0] : lines.Count > 0 ? lines[0] : "";
            line = line.TrimStart('#', '>', '-', '*', '+', ' ').Replace("**", "").Replace("`", "");
            return line.Trim();
        }
    }

    /// <summary>The async hook: turns a hook's JSON input into one small record in the Hark inbox.</summary>
    public static class HookRecorder
    {
        public static readonly TimeSpan AliveFor = TimeSpan.FromSeconds(120);
        internal static readonly string[] AgentTools = { "Agent", "Task" };
        private static readonly string[] FileTools = { "Read", "Write", "Edit", "MultiEdit", "NotebookEdit" };
        private static readonly string[] PatternTools = { "Grep", "Glob" };
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static string Summarize(string tool, JsonNode input)
        {
            var o = input as JsonObject;
            var command = Str(o, "command");
            if (tool == "Bash" && command.Length > 0)
            {
                return HookText.Clean("Bash: " + command);
            }
            var pattern = Str(o, "pattern");
            if (PatternTools.Contains(tool) && pattern.Length > 0)
            {
                return HookText.Clean($"{tool} \"{pattern}\"");
            }
            if (FileTools.Contains(tool))
            {
                var path = Str(o, "file_path");
                if (path.Length == 0)
                {
                    path = Str(o, "notebook_path");
                }
                if (path.Length > 0)
                {
                    return HookText.Clean(tool + " " + BaseName(path));
                }
            }
            var description = Str(o, "description");
            if (AgentTools.Contains(tool) && description.Length > 0)
            {
                return HookText.Clean(tool + ": " + description);
            }
            return HookText.Clean(tool ?? "");
        }

        public static JsonObject BuildRecord(JsonObject payload, IReadOnlyDictionary<string, string> env, long nowMs)
        {
            var cwd = Str(payload, "cwd");
            var tool = Str(payload, "tool_name");
            var input = payload["tool_input"];
            var record = new JsonObject { ["ts"] = nowMs };
            Add(record, "event", Str(payload, "hook_event_name"));
            Add(record, "session", Str(payload, "session_id"));
            // The folder Claude started in; cwd moves when Claude runs cd.
            var projectDir = Env(env, "CLAUDE_PROJECT_DIR");
            Add(record, "project", BaseName(projectDir.Length > 0 ? projectDir : cwd));
            Add(record, "cwd", cwd);
            Add(record, "source", Env(env, "CLAUDE_CODE_ENTRYPOINT") == "claude-vscode" ? "vscode" : "cli");
            Add(record, "agent_id", Str(payload, "agent_id"));
            Add(record, "agent_type", Str(payload, "agent_type"));
            Add(record, "agent_task", AgentTask(payload));
            Add(record, "tool", tool);
            Add(record, "tool_use_id", Str(payload, "tool_use_id"));
            if (tool.Length > 0)
            {
                Add(record, "summary", Summarize(tool, input));
            }
            Add(record, "notification_type", Str(payload, "notification_type"));
            Add(record, "message", HookText.Clean(Str(payload, "message")));
            if (tool == "AskUserQuestion")
            {
                AddArray(record, "questions", Questions(input as JsonObject));
            }
            if (tool == "TodoWrite")
            {
                AddArray(record, "todos", Todos(input as JsonObject));
            }
            if (AgentTools.Contains(tool) && input is JsonObject launch)
            {
                // No subagent_type means Claude Code starts a general-purpose agent.
                var type = Str(launch, "subagent_type");
                Add(record, "spawn_type", type.Length > 0 ? type : "general-purpose");
            }
            Add(record, "error", HookText.Clean(Str(payload, "error")));
            Add(record, "last_message", HookText.Clean(HookText.FirstLine(Str(payload, "last_assistant_message"))));
            // The reply ends with a question to the user: the card says "Asks you" instead of "Done".
            if (Str(payload, "hook_event_name") == "Stop" && HookText.Asks(Str(payload, "last_assistant_message")))
            {
                record["asks"] = true;
            }
            return record;
        }

        public static bool AppAlive(string dataDir, DateTime nowUtc)
        {
            var path = Path.Combine(dataDir, Heartbeat.FileName);
            return File.Exists(path) && nowUtc - File.GetLastWriteTimeUtc(path) <= AliveFor;
        }

        public static string Write(JsonObject record, string inbox)
        {
            Directory.CreateDirectory(inbox);
            var ts = record.TryGetPropertyValue("ts", out var t) ? t?.ToJsonString() : "0";
            var name = $"{ts}-{Process.GetCurrentProcess().Id}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.json";
            var final = Path.Combine(inbox, name);
            var tmp = final + ".tmp";
            File.WriteAllText(tmp, record.ToJsonString(), new UTF8Encoding(false));
            File.Move(tmp, final);
            return final;
        }

        /// <summary>The hook itself. It never fails and never prints anything.</summary>
        public static int Run(string[] args, TextReader stdin, IReadOnlyDictionary<string, string> env, Func<DateTime> clock)
        {
            // Stamp first: async hooks start a few ms apart, and the order of events matters to Hark.
            var nowMs = UnixMs(clock());
            try
            {
                var raw = stdin.ReadToEnd();
                var dataDir = DataDir(args);
                if (dataDir == null || IsScripted(env) || !AppAlive(dataDir, clock()))
                {
                    return 0;
                }
                if (JsonNode.Parse(raw) is JsonObject payload)
                {
                    Write(BuildRecord(payload, env, nowMs), Path.Combine(dataDir, "inbox"));
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        internal static long UnixMs(DateTime utc) => (long)(utc.ToUniversalTime() - Epoch).TotalMilliseconds;

        internal static string DataDir(string[] args)
        {
            var i = Array.IndexOf(args ?? Array.Empty<string>(), "--data-dir");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>Scripted runs (claude -p, Agent SDK): nobody is there to answer.</summary>
        internal static bool IsScripted(IReadOnlyDictionary<string, string> env) => Env(env, "CLAUDE_CODE_ENTRYPOINT").StartsWith("sdk", StringComparison.Ordinal);

        internal static bool IsPaused(string dataDir) => File.Exists(Path.Combine(dataDir, "paused"));

        internal static string Str(JsonObject o, string key) =>
            o != null && o.TryGetPropertyValue(key, out var node) && node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

        private static string Env(IReadOnlyDictionary<string, string> env, string key) =>
            env != null && env.TryGetValue(key, out var value) && value != null ? value : "";

        /// <summary>The last part of a path; Claude on Windows sends backslashes, in WSL forward slashes.</summary>
        private static string BaseName(string path)
        {
            path = (path ?? "").TrimEnd('/', '\\');
            var cut = path.LastIndexOfAny(new[] { '/', '\\' });
            return cut >= 0 ? path.Substring(cut + 1) : path;
        }

        private static void Add(JsonObject record, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                record[key] = value;
            }
        }

        private static void AddArray(JsonObject record, string key, JsonArray value)
        {
            if (value.Count > 0)
            {
                record[key] = value;
            }
        }

        private static JsonArray Questions(JsonObject input)
        {
            var result = new JsonArray();
            foreach (var q in (input?["questions"] as JsonArray) ?? new JsonArray())
            {
                if (!(q is JsonObject question))
                {
                    continue;
                }
                var options = ((question["options"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>().ToList();
                result.Add(new JsonObject
                {
                    ["question"] = HookText.Cut(Str(question, "question")),
                    ["header"] = HookText.Cut(Str(question, "header")),
                    ["options"] = new JsonArray(options.Select(o => (JsonNode)HookText.Cut(Str(o, "label"))).ToArray()),
                    ["descriptions"] = new JsonArray(options.Select(o => (JsonNode)HookText.Cut(Str(o, "description"))).ToArray()),
                    ["multi"] = Truthy(question["multiSelect"]),
                });
            }
            return result;
        }

        private static JsonArray Todos(JsonObject input)
        {
            var result = new JsonArray();
            foreach (var t in (input?["todos"] as JsonArray) ?? new JsonArray())
            {
                if (!(t is JsonObject todo))
                {
                    continue;
                }
                var status = Str(todo, "status");
                status = status.Length > 0 ? status : "pending";
                var active = Str(todo, "activeForm");
                var text = status == "in_progress" && active.Length > 0 ? active : Str(todo, "content");
                result.Add(new JsonObject { ["text"] = HookText.Cut(text), ["status"] = status });
            }
            return result;
        }

        internal static bool Truthy(JsonNode node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

        /// <summary>The agent's description from the meta file Claude Code keeps next to its transcript.</summary>
        private static string AgentTask(JsonObject payload)
        {
            var agentId = Str(payload, "agent_id");
            var transcript = Str(payload, "transcript_path");
            if (agentId.Length == 0 || transcript.Length == 0)
            {
                return null;
            }
            try
            {
                var meta = Path.GetFileName(transcript) == $"agent-{agentId}.jsonl"
                    ? Path.Combine(Path.GetDirectoryName(transcript), $"agent-{agentId}.meta.json")
                    : Path.Combine(Path.GetDirectoryName(transcript), Path.GetFileNameWithoutExtension(transcript), "subagents", $"agent-{agentId}.meta.json");
                var description = Str(JsonNode.Parse(File.ReadAllText(meta, Encoding.UTF8)) as JsonObject, "description");
                return description.Trim().Length > 0 ? HookText.Cut(description) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The sync AskUserQuestion hook: while Hark runs and is not paused it holds the question and waits for an answer
    /// from the Hark card (answers/&lt;tool_use_id&gt;.json); "Answer in VS Code", 30 minutes or Hark stopping let it go.
    /// </summary>
    public static class AnswerHook
    {
        public static readonly TimeSpan Hold = TimeSpan.FromSeconds(1800);
        public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
        private static readonly Regex SafeId = new Regex(@"^[A-Za-z0-9_-]{1,128}\z");

        public static int Run(string[] args, TextReader stdin, IReadOnlyDictionary<string, string> env, TextWriter stdout, Func<DateTime> clock, Action<TimeSpan> sleep)
        {
            try
            {
                if (!(JsonNode.Parse(stdin.ReadToEnd()) is JsonObject payload))
                {
                    return 0;
                }
                var dataDir = HookRecorder.DataDir(args);
                if (dataDir == null || HookRecorder.IsScripted(env))
                {
                    return 0;
                }
                if (HookRecorder.Str(payload, "tool_name") != "AskUserQuestion" || HookRecorder.Str(payload, "agent_id").Length > 0)
                {
                    return 0;
                }
                var id = HookRecorder.Str(payload, "tool_use_id");
                if (!SafeId.IsMatch(id) || !HookRecorder.AppAlive(dataDir, clock()) || HookRecorder.IsPaused(dataDir))
                {
                    return 0;
                }
                var held = Path.Combine(dataDir, "held", id + ".json");
                WriteAtomic(held, new JsonObject { ["session"] = HookRecorder.Str(payload, "session_id"), ["ts"] = HookRecorder.UnixMs(clock()) });
                JsonObject answer;
                try
                {
                    answer = WaitForAnswer(dataDir, id, clock, sleep);
                }
                finally
                {
                    TryDelete(held);
                }
                var output = Decide(answer, payload["tool_input"] as JsonObject ?? new JsonObject());
                if (output != null)
                {
                    stdout.Write(output.ToJsonString());
                    stdout.Flush();
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        public static JsonObject WaitForAnswer(string dataDir, string toolUseId, Func<DateTime> clock, Action<TimeSpan> sleep)
        {
            var path = Path.Combine(dataDir, "answers", toolUseId + ".json");
            var deadline = clock() + Hold;
            while (true)
            {
                if (File.Exists(path))
                {
                    var body = ReadAnswer(path);
                    if (body != null)
                    {
                        return body;
                    }
                }
                if (clock() >= deadline || !HookRecorder.AppAlive(dataDir, clock()))
                {
                    return null;
                }
                sleep(Poll);
            }
        }

        public static JsonObject Decide(JsonObject answer, JsonObject toolInput)
        {
            if (answer == null || HookRecorder.Str(answer, "action") != "answer" || !(answer["answers"] is JsonObject answers) || answers.Count == 0)
            {
                return null;
            }
            if (!answers.All(a => a.Value is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0))
            {
                return null;
            }
            var updated = (JsonObject)toolInput.DeepClone();
            updated["answers"] = Restore(answers, toolInput);
            return new JsonObject
            {
                ["hookSpecificOutput"] = new JsonObject
                {
                    ["hookEventName"] = "PreToolUse",
                    ["permissionDecision"] = "allow",
                    ["updatedInput"] = updated,
                },
            };
        }

        /// <summary>Keys back to the original question text, shortened labels back to the original labels.</summary>
        public static JsonObject Restore(JsonObject answers, JsonObject toolInput)
        {
            if (!(toolInput?["questions"] is JsonArray questions))
            {
                return (JsonObject)answers.DeepClone();
            }
            var result = new JsonObject();
            foreach (var question in questions.OfType<JsonObject>())
            {
                var text = HookRecorder.Str(question, "question");
                if (text.Length == 0)
                {
                    continue;
                }
                var value = HookRecorder.Str(answers, text);
                if (!answers.ContainsKey(text))
                {
                    value = answers.ContainsKey(HookText.Cut(text)) ? HookRecorder.Str(answers, HookText.Cut(text)) : null;
                }
                if (value == null)
                {
                    continue;
                }
                var labels = new Dictionary<string, string>();
                foreach (var option in ((question["options"] as JsonArray) ?? new JsonArray()).OfType<JsonObject>())
                {
                    var label = HookRecorder.Str(option, "label");
                    if (label.Length > 0)
                    {
                        labels[HookText.Cut(label)] = label;
                    }
                }
                var parts = value.Split(new[] { ", " }, StringSplitOptions.None);
                if (labels.TryGetValue(value, out var full))
                {
                    value = full;
                }
                else if (HookRecorder.Truthy(question["multiSelect"]) && parts.All(labels.ContainsKey))
                {
                    value = string.Join(", ", parts.Select(p => labels[p]));
                }
                result[text] = value;
            }
            return result.Count > 0 ? result : (JsonObject)answers.DeepClone();
        }

        private static JsonObject ReadAnswer(string path)
        {
            JsonNode body;
            try
            {
                body = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception)
            {
                return null; // still being written: try again on the next poll
            }
            TryDelete(path);
            return body as JsonObject ?? new JsonObject();
        }

        private static void WriteAtomic(string path, JsonObject body)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, body.ToJsonString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>SubagentStart and UserPromptSubmit hook: asks Claude to keep a short to-do list, so Hark can show real progress.</summary>
    public static class AgentHook
    {
        public const string PlanHint =
            "Hark, the user's progress viewer, shows your progress. If you have the TodoWrite tool, start by writing "
            + "a short to-do list for this task with it (3 to 7 items) and keep it updated: mark the item you work on "
            + "as in_progress and finished items as completed. If you do not have TodoWrite, ignore this note.";

        public const string TaskHint =
            "Hark, the user's progress viewer, shows your progress. When this request has several steps, keep a short "
            + "TodoWrite to-do list for it (3 to 7 items) and keep it updated as you work. For a quick single-step request, "
            + "skip the list.";

        public static int Run(string[] args, TextReader stdin, IReadOnlyDictionary<string, string> env, TextWriter stdout, Func<DateTime> clock)
        {
            try
            {
                if (!(JsonNode.Parse(stdin.ReadToEnd()) is JsonObject payload))
                {
                    return 0;
                }
                var dataDir = HookRecorder.DataDir(args);
                if (dataDir == null || HookRecorder.IsScripted(env))
                {
                    return 0;
                }
                var evt = HookRecorder.Str(payload, "hook_event_name");
                var hint = evt == "SubagentStart" ? PlanHint : evt == "UserPromptSubmit" ? TaskHint : null;
                if (hint == null || !HookRecorder.AppAlive(dataDir, clock()) || HookRecorder.IsPaused(dataDir))
                {
                    return 0;
                }
                stdout.Write(new JsonObject
                {
                    ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = evt, ["additionalContext"] = hint },
                }.ToJsonString());
                stdout.Flush();
            }
            catch (Exception)
            {
            }
            return 0;
        }
    }

    /// <summary>Adds or removes the Hark hooks in a Claude Code settings.json, keeping every other setting and hook.</summary>
    public static class HookInstaller
    {
        public const int AnswerTimeoutSeconds = 1860; // the answer hook holds a question up to 30 minutes

        public static readonly IReadOnlyList<string> Events = new[]
        {
            "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
            "PermissionRequest", "Notification", "SubagentStart", "SubagentStop", "Stop",
            "StopFailure", "SessionEnd",
        };

        private static readonly string[] Markers = { "HarkHook.exe", "/Hark/record_event.py", "/Hark/answer_hook.py", "/Hark/agent_hook.py" };

        private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Claude Code on Windows runs hook commands in Git Bash: forward slashes, quoted paths.</summary>
        public static string Command(string exePath, string verb, string dataDir) =>
            $"\"{exePath.Replace('\\', '/')}\" {verb} --data-dir \"{dataDir.Replace('\\', '/')}\"";

        public static JsonObject Remove(JsonObject settings)
        {
            var result = (JsonObject)settings.DeepClone();
            if (!(result["hooks"] is JsonObject hooks))
            {
                return result;
            }
            foreach (var evt in hooks.Select(h => h.Key).ToList())
            {
                var groups = new JsonArray();
                foreach (var group in ((hooks[evt] as JsonArray) ?? new JsonArray()).OfType<JsonObject>())
                {
                    var kept = ((group["hooks"] as JsonArray) ?? new JsonArray()).Where(h => !IsOurs(h)).Select(h => h?.DeepClone()).ToArray();
                    if (kept.Length > 0)
                    {
                        var copy = (JsonObject)group.DeepClone();
                        copy["hooks"] = new JsonArray(kept);
                        groups.Add(copy);
                    }
                }
                if (groups.Count > 0)
                {
                    hooks[evt] = groups;
                }
                else
                {
                    hooks.Remove(evt);
                }
            }
            if (hooks.Count == 0)
            {
                result.Remove("hooks");
            }
            return result;
        }

        public static JsonObject Merge(JsonObject settings, string command, string answerCommand, string agentCommand)
        {
            var result = Remove(settings);
            if (!(result["hooks"] is JsonObject hooks))
            {
                hooks = new JsonObject();
                result["hooks"] = hooks;
            }
            foreach (var evt in Events)
            {
                Groups(hooks, evt).Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["async"] = true }) });
            }
            if (answerCommand != null)
            {
                Groups(hooks, "PreToolUse").Add(new JsonObject
                {
                    ["matcher"] = "AskUserQuestion",
                    ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = answerCommand, ["timeout"] = AnswerTimeoutSeconds }),
                });
            }
            if (agentCommand != null)
            {
                foreach (var evt in new[] { "SubagentStart", "UserPromptSubmit" })
                {
                    Groups(hooks, evt).Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = agentCommand, ["timeout"] = 10 }) });
                }
            }
            return result;
        }

        /// <summary>Backs up settings.json, then writes it with the Hark hooks. Returns the backup path, or null when there was no file.</summary>
        public static string Install(string settingsPath, string command, string answerCommand, string agentCommand) =>
            Save(settingsPath, Merge(Load(settingsPath), command, answerCommand, agentCommand));

        public static string Uninstall(string settingsPath) =>
            File.Exists(settingsPath) ? Save(settingsPath, Remove(Load(settingsPath))) : null;

        private static bool IsOurs(JsonNode hook) =>
            hook is JsonObject o && Markers.Any(m => HookRecorder.Str(o, "command").IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);

        private static JsonArray Groups(JsonObject hooks, string evt)
        {
            if (!(hooks[evt] is JsonArray groups))
            {
                groups = new JsonArray();
                hooks[evt] = groups;
            }
            return groups;
        }

        private static JsonObject Load(string path)
        {
            if (!File.Exists(path))
            {
                return new JsonObject();
            }
            var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8), null, options) as JsonObject ?? new JsonObject();
        }

        private static string Save(string path, JsonObject settings)
        {
            string backup = null;
            if (File.Exists(path))
            {
                backup = Path.Combine(Path.GetDirectoryName(path), "settings.json.bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                File.Copy(path, backup, true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, settings.ToJsonString(Pretty) + "\n", new UTF8Encoding(false));
            return backup;
        }
    }
}
