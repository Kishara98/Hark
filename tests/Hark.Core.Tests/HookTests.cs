using System.Text.Json.Nodes;

namespace Hark.Core.Tests;

// The C# hooks (HarkHook.exe, for Claude Code running natively on Windows) must write exactly what the
// Python hooks in hooks/ write for WSL. These tests mirror hooks/test_*.py, plus Windows paths.
public class HookRecorderTests
{
    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    private static readonly Dictionary<string, string> NoEnv = new();

    private const string Ask = """
        {"session_id": "s-ask", "transcript_path": "/home/dev/.claude/projects/x/s-ask.jsonl", "cwd": "/home/dev/projects/WebShop",
         "permission_mode": "default", "hook_event_name": "PreToolUse", "tool_name": "AskUserQuestion", "tool_use_id": "toolu_ask",
         "tool_input": {"questions": [{"question": "Which parent branch should the new feature branch come from?", "header": "Parent",
           "multiSelect": false, "options": [{"label": "release/2.0-rc", "description": "Release branch"}, {"label": "main", "description": "Main branch"}]}]}}
        """;

    private const string AgentStep = """
        {"session_id": "s-agent", "cwd": "/home/dev/projects", "hook_event_name": "PreToolUse", "tool_name": "Grep",
         "tool_input": {"pattern": "pageSize"}, "agent_id": "agent-abc", "agent_type": "Explore"}
        """;

    private static string Stop(string message) =>
        new JsonObject { ["session_id"] = "s-stop", ["cwd"] = "/home/dev/projects/Notes", ["hook_event_name"] = "Stop", ["last_assistant_message"] = message }.ToJsonString();

    private static string Summary(string tool, string input) => HookRecorder.Summarize(tool, input == null ? null : JsonNode.Parse(input));

    [Fact]
    public void Summary_formats()
    {
        Assert.Equal("Bash: mvn -q test", Summary("Bash", """{"command": "mvn -q test", "description": "x"}"""));
        Assert.Equal("Grep \"pageSize\"", Summary("Grep", """{"pattern": "pageSize"}"""));
        Assert.Equal("Glob \"**/*.cs\"", Summary("Glob", """{"pattern": "**/*.cs"}"""));
        Assert.Equal("Read SplitConfig.java", Summary("Read", """{"file_path": "/a/b/SplitConfig.java"}"""));
        Assert.Equal("Edit Main.cs", Summary("Edit", """{"file_path": "/a/b/Main.cs", "old_string": "a", "new_string": "b"}"""));
        Assert.Equal("Agent: find split logic", Summary("Agent", """{"description": "find split logic", "prompt": "long"}"""));
        Assert.Equal("WebFetch", Summary("WebFetch", """{"url": "https://x.y"}"""));
    }

    [Fact]
    public void Summary_takes_the_file_name_from_a_windows_path()
    {
        Assert.Equal("Read Main.cs", Summary("Read", """{"file_path": "C:\\Users\\dev\\projects\\Main.cs"}"""));
    }

    [Fact]
    public void Summary_never_keeps_write_content_and_handles_missing_input()
    {
        Assert.Equal("Write f.txt", Summary("Write", "{\"file_path\": \"/a/f.txt\", \"content\": \"" + new string('z', 10000) + "\"}"));
        Assert.Equal("Bash", Summary("Bash", "{}"));
        Assert.Equal("Read", Summary("Read", null));
    }

    [Fact]
    public void Cut_collapses_spaces_and_stops_at_200()
    {
        Assert.Equal(200, Summary("Bash", "{\"command\": \"" + new string('x', 500) + "\"}").Length);
        Assert.Equal("a b c", HookText.Cut("  a  b\nc "));
        Assert.EndsWith("...", HookText.Cut(new string('x', 500)));
    }

    [Fact]
    public void Question_record()
    {
        var r = HookRecorder.BuildRecord(Obj(Ask), new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "claude-vscode" }, 1000);
        Assert.Equal(1000, (long)r["ts"]!);
        Assert.Equal("PreToolUse", (string)r["event"]!);
        Assert.Equal("s-ask", (string)r["session"]!);
        Assert.Equal("WebShop", (string)r["project"]!);
        Assert.Equal("vscode", (string)r["source"]!);
        Assert.Equal("AskUserQuestion", (string)r["tool"]!);
        var q = r["questions"]![0]!;
        Assert.Equal("Which parent branch should the new feature branch come from?", (string)q["question"]!);
        Assert.Equal("Parent", (string)q["header"]!);
        Assert.Equal(new[] { "release/2.0-rc", "main" }, q["options"]!.AsArray().Select(o => (string)o!));
        Assert.Equal(new[] { "Release branch", "Main branch" }, q["descriptions"]!.AsArray().Select(o => (string)o!));
        Assert.False((bool)q["multi"]!);
    }

    [Fact]
    public void Project_is_the_folder_claude_started_in_then_cwd()
    {
        var moved = Obj(Ask);
        moved["cwd"] = "/home/dev/projects/Hark";
        var r = HookRecorder.BuildRecord(moved, new Dictionary<string, string> { ["CLAUDE_PROJECT_DIR"] = "/home/dev/projects/" }, 1);
        Assert.Equal("projects", (string)r["project"]!);
        Assert.Equal("/home/dev/projects/Hark", (string)r["cwd"]!);
        Assert.Equal("WebShop", (string)HookRecorder.BuildRecord(Obj(Ask), NoEnv, 1)["project"]!);
    }

    [Fact]
    public void Project_from_a_windows_folder()
    {
        var r = HookRecorder.BuildRecord(Obj(Ask), new Dictionary<string, string> { ["CLAUDE_PROJECT_DIR"] = "C:\\Users\\dev\\projects\\WebShop\\" }, 1);
        Assert.Equal("WebShop", (string)r["project"]!);
    }

    [Fact]
    public void Todo_list_is_kept()
    {
        var r = HookRecorder.BuildRecord(Obj("""
            {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "TodoWrite", "agent_id": "a1",
             "tool_input": {"todos": [{"content": "Fetch the release branch", "status": "completed", "activeForm": "Fetching"},
               {"content": "Run the tests", "status": "in_progress", "activeForm": "Running the tests"},
               {"content": "Write a summary", "status": "pending", "activeForm": "Writing"}]}}
            """), NoEnv, 1);
        Assert.Equal("""[{"text":"Fetch the release branch","status":"completed"},{"text":"Running the tests","status":"in_progress"},{"text":"Write a summary","status":"pending"}]""",
            r["todos"]!.ToJsonString());
    }

    [Fact]
    public void Agent_launch_keeps_type_and_task_but_not_the_prompt()
    {
        var r = HookRecorder.BuildRecord(Obj("""
            {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_use_id": "t1",
             "tool_input": {"description": "Find split logic", "subagent_type": "Explore", "prompt": "long secret prompt"}}
            """), NoEnv, 1);
        Assert.Equal("Explore", (string)r["spawn_type"]!);
        Assert.Equal("Agent: Find split logic", (string)r["summary"]!);
        Assert.DoesNotContain("long secret prompt", r.ToJsonString());
        var untyped = HookRecorder.BuildRecord(Obj("""
            {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_input": {"description": "Developer agent", "prompt": "p"}}
            """), NoEnv, 1);
        Assert.Equal("general-purpose", (string)untyped["spawn_type"]!);
    }

    [Fact]
    public void Agent_event_carries_the_task_from_its_meta_file()
    {
        using var t = new TempDir();
        var folder = Path.Combine(t.Path, "proj", "sess", "subagents");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "agent-a11.meta.json"), """{"agentType": "general-purpose", "description": "Developer agent: Task 3"}""");
        JsonObject Payload(string transcript)
        {
            var p = Obj(AgentStep);
            p["agent_id"] = "a11";
            p["transcript_path"] = transcript;
            return p;
        }
        Assert.Equal("Developer agent: Task 3", (string)HookRecorder.BuildRecord(Payload(Path.Combine(t.Path, "proj", "sess.jsonl")), NoEnv, 1)["agent_task"]!);
        Assert.Equal("Developer agent: Task 3", (string)HookRecorder.BuildRecord(Payload(Path.Combine(folder, "agent-a11.jsonl")), NoEnv, 1)["agent_task"]!);
        Assert.False(HookRecorder.BuildRecord(Payload(Path.Combine(t.Path, "other", "sess.jsonl")), NoEnv, 1).ContainsKey("agent_task"));
        Assert.False(HookRecorder.BuildRecord(Obj(Ask), NoEnv, 1).ContainsKey("agent_task"));
    }

    [Fact]
    public void Source_defaults_to_cli_and_agent_fields_are_kept()
    {
        Assert.Equal("cli", (string)HookRecorder.BuildRecord(Obj(Ask), NoEnv, 1)["source"]!);
        var r = HookRecorder.BuildRecord(Obj(AgentStep), NoEnv, 1);
        Assert.Equal("agent-abc", (string)r["agent_id"]!);
        Assert.Equal("Explore", (string)r["agent_type"]!);
        Assert.Equal("Grep \"pageSize\"", (string)r["summary"]!);
    }

    [Fact]
    public void Last_message_is_the_first_real_line_cut_to_200()
    {
        var long_ = HookRecorder.BuildRecord(Obj(Stop("Morning report sent. " + new string('x', 1000))), NoEnv, 1);
        Assert.Equal(200, ((string)long_["last_message"]!).Length);
        Assert.StartsWith("Morning report sent.", (string)long_["last_message"]!);
        var md = HookRecorder.BuildRecord(Obj(Stop("## Summary\n\n**All 9 tasks are done.** Nothing is `pushed`.\n- item one\n- item two")), NoEnv, 1);
        Assert.Equal("All 9 tasks are done. Nothing is pushed.", (string)md["last_message"]!);
        Assert.Equal("Done", (string)HookRecorder.BuildRecord(Obj(Stop("\n## Done\n")), NoEnv, 1)["last_message"]!);
    }

    [Fact]
    public void Empty_values_and_prompts_are_left_out()
    {
        var r = HookRecorder.BuildRecord(Obj(Stop("ok")), NoEnv, 1);
        Assert.False(r.ContainsKey("tool"));
        Assert.False(r.ContainsKey("agent_id"));
        Assert.False(r.ContainsKey("questions"));
        var prompt = HookRecorder.BuildRecord(Obj("""{"session_id": "s", "cwd": "/p", "hook_event_name": "UserPromptSubmit", "prompt": "secret plan"}"""), NoEnv, 1);
        Assert.DoesNotContain("secret plan", prompt.ToJsonString());
    }

    [Fact]
    public void Record_is_written_as_one_json_file_the_app_reads()
    {
        using var t = new TempDir();
        Heartbeat.Touch(t.Path);
        var rc = HookRecorder.Run(new[] { "--data-dir", t.Path }, new StringReader(Stop("Morning report sent.")), NoEnv, () => DateTime.UtcNow);
        Assert.Equal(0, rc);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(t.Path, "inbox"), "*.json"));
        Assert.True(HookEventParser.TryParse(File.ReadAllText(file), DateTime.UtcNow, out var e));
        Assert.Equal("Stop", e.Event);
        Assert.Equal("Notes", e.Project);
        Assert.Equal("Morning report sent.", e.LastMessage);
    }

    [Fact]
    public void Nothing_is_written_when_hark_is_not_running_or_for_scripted_runs()
    {
        using var t = new TempDir();
        var inbox = Path.Combine(t.Path, "inbox");
        HookRecorder.Run(new[] { "--data-dir", t.Path }, new StringReader(Stop("x")), NoEnv, () => DateTime.UtcNow);
        Heartbeat.Touch(t.Path);
        File.SetLastWriteTimeUtc(Path.Combine(t.Path, Heartbeat.FileName), DateTime.UtcNow.AddMinutes(-5));
        HookRecorder.Run(new[] { "--data-dir", t.Path }, new StringReader(Stop("x")), NoEnv, () => DateTime.UtcNow);
        Heartbeat.Touch(t.Path);
        HookRecorder.Run(new[] { "--data-dir", t.Path }, new StringReader(Stop("x")), new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "sdk-cli" }, () => DateTime.UtcNow);
        Assert.False(Directory.Exists(inbox) && Directory.GetFiles(inbox).Length > 0);
    }

    [Fact]
    public void Time_is_taken_before_reading_the_input()
    {
        using var t = new TempDir();
        Heartbeat.Touch(t.Path);
        var start = DateTime.UtcNow;
        var now = start;
        var stdin = new SlowReader(Stop("x"), () => now = now.AddSeconds(10));
        HookRecorder.Run(new[] { "--data-dir", t.Path }, stdin, NoEnv, () => now);
        var record = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(Path.Combine(t.Path, "inbox"), "*.json").Single()))!;
        Assert.Equal(new DateTimeOffset(start).ToUnixTimeMilliseconds(), (long)record["ts"]!);
    }

    [Fact]
    public void Bad_input_never_fails()
    {
        using var t = new TempDir();
        Heartbeat.Touch(t.Path);
        Assert.Equal(0, HookRecorder.Run(new[] { "--data-dir", t.Path }, new StringReader("not json"), NoEnv, () => DateTime.UtcNow));
        Assert.Equal(0, HookRecorder.Run(Array.Empty<string>(), new StringReader(Stop("x")), NoEnv, () => DateTime.UtcNow));
        Assert.False(Directory.Exists(Path.Combine(t.Path, "inbox")) && Directory.GetFiles(Path.Combine(t.Path, "inbox")).Length > 0);
    }

    private sealed class SlowReader : StringReader
    {
        private readonly Action _onRead;

        public SlowReader(string s, Action onRead) : base(s) => _onRead = onRead;

        public override string ReadToEnd()
        {
            _onRead();
            return base.ReadToEnd();
        }
    }
}

public class RedactTests
{
    // Same table as hooks/test_record_event.py: both hooks must hide exactly the same things.
    public static IEnumerable<object[]> Cases() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "redact_cases.json")))!.AsArray()
            .Select(c => new object[] { (string)c!["in"]!, (string)c["out"]! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Table(string input, string expected) => Assert.Equal(expected, HookText.Redact(input));

    [Fact]
    public void Command_summary_never_keeps_the_token()
    {
        var r = HookRecorder.BuildRecord(JsonNode.Parse("""
            {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Bash",
             "tool_input": {"command": "curl -H \"Authorization: Bearer abc123def456ghi789\" https://api.example.com"}}
            """)!.AsObject(), new Dictionary<string, string>(), 1);
        Assert.Equal("Bash: curl -H \"Authorization: Bearer ***\" https://api.example.com", (string)r["summary"]!);
        Assert.DoesNotContain("abc123def456ghi789", r.ToJsonString());
    }

    [Fact]
    public void Token_past_200_characters_is_hidden_before_the_cut()
    {
        var command = "echo " + new string('x', 180) + " ghp_abcdefghijklmnopqrstuvwxyz123456";
        var r = HookRecorder.BuildRecord(new JsonObject { ["session_id"] = "s", ["cwd"] = "/p", ["hook_event_name"] = "PreToolUse", ["tool_name"] = "Bash",
            ["tool_input"] = new JsonObject { ["command"] = command } }, new Dictionary<string, string>(), 1);
        Assert.DoesNotContain("ghp_abcdefghij", (string)r["summary"]!);
    }

    [Fact]
    public void Messages_and_replies_are_cleaned_too()
    {
        var r = HookRecorder.BuildRecord(new JsonObject { ["session_id"] = "s", ["cwd"] = "/p", ["hook_event_name"] = "Stop",
            ["last_assistant_message"] = "Set GITHUB_TOKEN=ghp_abcdefghijklmnopqrstuvwxyz123456 and run it." }, new Dictionary<string, string>(), 1);
        Assert.Equal("Set GITHUB_TOKEN=*** and run it.", (string)r["last_message"]!);
    }
}

public class AnswerHookTests
{
    private const string Questions = """
        [{"question": "Which parent branch?", "header": "Parent", "multiSelect": false,
          "options": [{"label": "main", "description": "Main"}, {"label": "release/2.0-rc", "description": "Release"}]}]
        """;

    private static string Payload(string toolInputQuestions = Questions, string tool = "AskUserQuestion", string id = "toolu_ask1", string agent = null)
    {
        var p = new JsonObject
        {
            ["session_id"] = "s1", ["cwd"] = "/home/dev/projects/WebShop", ["hook_event_name"] = "PreToolUse", ["tool_name"] = tool,
            ["tool_use_id"] = id, ["tool_input"] = new JsonObject { ["questions"] = JsonNode.Parse(toolInputQuestions) },
        };
        if (agent != null)
        {
            p["agent_id"] = agent;
        }
        return p.ToJsonString();
    }

    private sealed class FakeTime
    {
        public DateTime Now = DateTime.UtcNow;
        public Action<FakeTime> OnSleep;

        public void Sleep(TimeSpan t)
        {
            Now += t;
            OnSleep?.Invoke(this);
        }
    }

    private readonly TempDir _t = new();

    public AnswerHookTests() => Heartbeat.Touch(_t.Path);

    private string AnswerFile => Path.Combine(_t.Path, "answers", "toolu_ask1.json");

    private void Answer(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AnswerFile)!);
        File.WriteAllText(AnswerFile, json);
    }

    private string Run(string payload = null, FakeTime fake = null, Dictionary<string, string> env = null)
    {
        fake ??= new FakeTime();
        var output = new StringWriter();
        var rc = AnswerHook.Run(new[] { "--data-dir", _t.Path }, new StringReader(payload ?? Payload()), env ?? new Dictionary<string, string>(), output,
            () => fake.Now, fake.Sleep);
        Assert.Equal(0, rc);
        return output.ToString();
    }

    [Fact]
    public void Answer_returns_allow_with_the_answers_and_the_original_questions()
    {
        Answer("""{"action": "answer", "answers": {"Which parent branch?": "main"}}""");
        var hso = JsonNode.Parse(Run())!["hookSpecificOutput"]!;
        Assert.Equal("PreToolUse", (string)hso["hookEventName"]!);
        Assert.Equal("allow", (string)hso["permissionDecision"]!);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Questions), hso["updatedInput"]!["questions"]));
        Assert.Equal("""{"Which parent branch?":"main"}""", hso["updatedInput"]!["answers"]!.ToJsonString());
        Assert.False(File.Exists(AnswerFile));
    }

    [Fact]
    public void Answer_arriving_later_is_used_and_the_held_marker_is_removed()
    {
        var held = Path.Combine(_t.Path, "held", "toolu_ask1.json");
        var seenHeld = false;
        var fake = new FakeTime();
        var start = fake.Now;
        fake.OnSleep = f =>
        {
            seenHeld |= File.Exists(held);
            if (f.Now - start > TimeSpan.FromSeconds(3) && !File.Exists(AnswerFile))
            {
                Answer("""{"action": "answer", "answers": {"Which parent branch?": "release/2.0-rc"}}""");
            }
        };
        var answers = JsonNode.Parse(Run(fake: fake))!["hookSpecificOutput"]!["updatedInput"]!["answers"]!;
        Assert.Equal("release/2.0-rc", (string)answers["Which parent branch?"]!);
        Assert.True(seenHeld);
        Assert.False(File.Exists(held));
    }

    [Fact]
    public void Answer_in_vscode_or_empty_answers_print_nothing()
    {
        Answer("""{"action": "vscode"}""");
        Assert.Equal("", Run());
        Answer("""{"action": "answer", "answers": {}}""");
        Assert.Equal("", Run());
    }

    [Fact]
    public void Gives_up_after_30_minutes()
    {
        var fake = new FakeTime();
        var start = fake.Now;
        fake.OnSleep = f => File.SetLastWriteTimeUtc(Path.Combine(_t.Path, Heartbeat.FileName), f.Now);
        Assert.Equal("", Run(fake: fake));
        Assert.InRange((fake.Now - start).TotalSeconds, 1800, 1810);
    }

    [Fact]
    public void Lets_go_when_hark_stops()
    {
        var fake = new FakeTime();
        var start = fake.Now;
        Assert.Equal("", Run(fake: fake));
        Assert.True((fake.Now - start).TotalSeconds < 200);
    }

    [Fact]
    public void Not_running_or_paused_prints_nothing_at_once()
    {
        File.SetLastWriteTimeUtc(Path.Combine(_t.Path, Heartbeat.FileName), DateTime.UtcNow.AddMinutes(-5));
        var fake = new FakeTime();
        var start = fake.Now;
        Assert.Equal("", Run(fake: fake));
        Assert.Equal(start, fake.Now);
        Heartbeat.Touch(_t.Path);
        File.WriteAllText(Path.Combine(_t.Path, "paused"), "");
        Assert.Equal("", Run(fake: fake));
        Assert.Equal(start, fake.Now);
    }

    [Fact]
    public void Ignores_other_tools_agents_scripts_and_bad_ids()
    {
        Answer("""{"action": "answer", "answers": {"Which parent branch?": "main"}}""");
        Assert.Equal("", Run(Payload(tool: "Bash")));
        Assert.Equal("", Run(Payload(agent: "agent-1")));
        Assert.Equal("", Run(env: new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "sdk-cli" }));
        Assert.Equal("", Run(Payload(id: "../evil")));
        Assert.True(File.Exists(AnswerFile));
    }

    [Fact]
    public void Long_question_and_label_map_back_to_the_original_text_and_own_text_stays()
    {
        var question = "Push 1 commit to gitlab.com/example/orders-service, branch fix/PROJ-2491 (MR !68)?\n951686e test: check the partial update. "
            + string.Concat(Enumerable.Repeat("Lint, types and all 94 tests pass. ", 8));
        var label = "Yes, push and reply " + new string('x', 250);
        var questions = new JsonArray
        {
            new JsonObject { ["question"] = question, ["header"] = "Push", ["multiSelect"] = false,
                ["options"] = new JsonArray(new JsonObject { ["label"] = label, ["description"] = "d" }, new JsonObject { ["label"] = "No,  don't push", ["description"] = "d" }) },
            new JsonObject { ["question"] = "Which checks?", ["header"] = "Checks", ["multiSelect"] = true,
                ["options"] = new JsonArray(new JsonObject { ["label"] = "Unit", ["description"] = "" }, new JsonObject { ["label"] = "Lint", ["description"] = "" }) },
        }.ToJsonString();
        Answer(new JsonObject { ["action"] = "answer", ["answers"] = new JsonObject { [HookText.Cut(question)] = HookText.Cut(label), ["Which checks?"] = "Unit, Lint" } }.ToJsonString());
        var answers = JsonNode.Parse(Run(Payload(questions)))!["hookSpecificOutput"]!["updatedInput"]!["answers"]!;
        Assert.Equal(label, (string)answers[question]!);
        Assert.Equal("Unit, Lint", (string)answers["Which checks?"]!);
        Answer(new JsonObject { ["action"] = "answer", ["answers"] = new JsonObject { [HookText.Cut(question)] = "No, don't push", ["Which checks?"] = "only the unit tests" } }.ToJsonString());
        answers = JsonNode.Parse(Run(Payload(questions)))!["hookSpecificOutput"]!["updatedInput"]!["answers"]!;
        Assert.Equal("No,  don't push", (string)answers[question]!);
        Assert.Equal("only the unit tests", (string)answers["Which checks?"]!);
    }

    [Fact]
    public void Bad_input_prints_nothing()
    {
        var output = new StringWriter();
        Assert.Equal(0, AnswerHook.Run(new[] { "--data-dir", _t.Path }, new StringReader("not json"), new Dictionary<string, string>(), output, () => DateTime.UtcNow, _ => { }));
        Assert.Equal("", output.ToString());
    }
}

public class AgentHookTests
{
    private static string Run(string dataDir, string evt, Dictionary<string, string> env = null)
    {
        var output = new StringWriter();
        var payload = new JsonObject { ["session_id"] = "s", ["hook_event_name"] = evt }.ToJsonString();
        Assert.Equal(0, AgentHook.Run(new[] { "--data-dir", dataDir }, new StringReader(payload), env ?? new Dictionary<string, string>(), output, () => DateTime.UtcNow));
        return output.ToString();
    }

    [Fact]
    public void Asks_for_a_todo_list_on_agent_start_and_on_each_prompt()
    {
        using var t = new TempDir();
        Heartbeat.Touch(t.Path);
        var start = JsonNode.Parse(Run(t.Path, "SubagentStart"))!["hookSpecificOutput"]!;
        Assert.Equal("SubagentStart", (string)start["hookEventName"]!);
        Assert.Contains("TodoWrite", (string)start["additionalContext"]!);
        var prompt = JsonNode.Parse(Run(t.Path, "UserPromptSubmit"))!["hookSpecificOutput"]!;
        Assert.Equal("UserPromptSubmit", (string)prompt["hookEventName"]!);
        Assert.Contains("several steps", (string)prompt["additionalContext"]!);
    }

    [Fact]
    public void Says_nothing_when_hark_is_off_paused_scripted_or_for_other_events()
    {
        using var t = new TempDir();
        Assert.Equal("", Run(t.Path, "SubagentStart"));
        Heartbeat.Touch(t.Path);
        Assert.Equal("", Run(t.Path, "Stop"));
        Assert.Equal("", Run(t.Path, "SubagentStart", new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "sdk-cli" }));
        File.WriteAllText(Path.Combine(t.Path, "paused"), "");
        Assert.Equal("", Run(t.Path, "UserPromptSubmit"));
    }
}

public class HookInstallerTests
{
    private const string Cmd = "\"C:/Users/dev/AppData/Local/Programs/Hark/HarkHook.exe\" record --data-dir \"C:/Users/dev/AppData/Local/Hark\"";
    private const string AnswerCmd = "\"C:/Users/dev/AppData/Local/Programs/Hark/HarkHook.exe\" answer --data-dir \"C:/Users/dev/AppData/Local/Hark\"";
    private const string AgentCmd = "\"C:/Users/dev/AppData/Local/Programs/Hark/HarkHook.exe\" agent --data-dir \"C:/Users/dev/AppData/Local/Hark\"";
    private const string Other = """{"hooks": [{"type": "command", "command": "/opt/plugin/session-start.sh"}]}""";

    private static JsonObject Obj(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Command_uses_forward_slashes_and_quotes_for_git_bash()
    {
        // Claude Code on Windows runs hook commands in Git Bash: backslashes would be eaten, spaces need quotes.
        Assert.Equal("\"C:/Users/A B/AppData/Local/Programs/Hark/HarkHook.exe\" record --data-dir \"C:/Users/A B/AppData/Local/Hark\"",
            HookInstaller.Command("C:\\Users\\A B\\AppData\\Local\\Programs\\Hark\\HarkHook.exe", "record", "C:\\Users\\A B\\AppData\\Local\\Hark"));
    }

    [Fact]
    public void Merge_adds_every_event_the_answer_hook_and_the_agent_hint_and_keeps_other_hooks()
    {
        var s = HookInstaller.Merge(Obj("{\"model\": \"x\", \"hooks\": {\"SessionStart\": [" + Other + "]}}"), Cmd, AnswerCmd, AgentCmd);
        Assert.Equal("x", (string)s["model"]!);
        var hooks = s["hooks"]!.AsObject();
        Assert.Equal(HookInstaller.Events.OrderBy(e => e), hooks.Select(h => h.Key).OrderBy(e => e));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Other), hooks["SessionStart"]![0]));
        var stop = Assert.Single(hooks["Stop"]!.AsArray())!["hooks"]![0]!;
        Assert.Equal("command", (string)stop["type"]!);
        Assert.Equal(Cmd, (string)stop["command"]!);
        Assert.True((bool)stop["async"]!);
        var ask = hooks["PreToolUse"]!.AsArray().Last()!;
        Assert.Equal("AskUserQuestion", (string)ask["matcher"]!);
        Assert.Equal(1860, (int)ask["hooks"]![0]!["timeout"]!);
        Assert.Equal(AgentCmd, (string)hooks["SubagentStart"]!.AsArray().Last()!["hooks"]![0]!["command"]!);
        Assert.Equal(AgentCmd, (string)hooks["UserPromptSubmit"]!.AsArray().Last()!["hooks"]![0]!["command"]!);
    }

    [Fact]
    public void Merge_twice_is_the_same_and_replaces_an_old_command()
    {
        var once = HookInstaller.Merge(new JsonObject(), Cmd, AnswerCmd, AgentCmd);
        Assert.True(JsonNode.DeepEquals(once, HookInstaller.Merge(once, Cmd, AnswerCmd, AgentCmd)));
        var old = HookInstaller.Merge(new JsonObject(), Cmd.Replace("Programs/Hark", "Old/Hark"), AnswerCmd, AgentCmd);
        Assert.Single(HookInstaller.Merge(old, Cmd, AnswerCmd, AgentCmd)["hooks"]!["Stop"]!.AsArray());
    }

    [Fact]
    public void Remove_drops_only_hark_and_leaves_no_empty_hooks_key()
    {
        var merged = HookInstaller.Merge(Obj("{\"hooks\": {\"SessionStart\": [" + Other + "]}}"), Cmd, AnswerCmd, AgentCmd);
        Assert.True(JsonNode.DeepEquals(Obj("{\"hooks\": {\"SessionStart\": [" + Other + "]}}"), HookInstaller.Remove(merged)));
        Assert.True(JsonNode.DeepEquals(Obj("""{"model": "x"}"""), HookInstaller.Remove(HookInstaller.Merge(Obj("""{"model": "x"}"""), Cmd, AnswerCmd, AgentCmd))));
        Assert.True(JsonNode.DeepEquals(Obj("""{"model": "x"}"""), HookInstaller.Remove(Obj("""{"model": "x"}"""))));
    }

    [Fact]
    public void Install_backs_up_and_writes_settings_and_uninstall_removes_them()
    {
        using var t = new TempDir();
        var settings = Path.Combine(t.Path, ".claude", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
        File.WriteAllText(settings, """{"model": "x"}""");
        HookInstaller.Install(settings, Cmd, AnswerCmd, AgentCmd);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(settings)!, "settings.json.bak-*"));
        var written = Obj(File.ReadAllText(settings));
        Assert.Equal("x", (string)written["model"]!);
        Assert.Equal(12, written["hooks"]!.AsObject().Count);
        HookInstaller.Uninstall(settings);
        Assert.True(JsonNode.DeepEquals(Obj("""{"model": "x"}"""), Obj(File.ReadAllText(settings))));
    }

    [Fact]
    public void Install_creates_settings_when_there_are_none()
    {
        using var t = new TempDir();
        var settings = Path.Combine(t.Path, ".claude", "settings.json");
        HookInstaller.Install(settings, Cmd, AnswerCmd, AgentCmd);
        Assert.Equal(12, Obj(File.ReadAllText(settings))["hooks"]!.AsObject().Count);
    }
}
