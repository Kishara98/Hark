namespace Hark.Core.Tests;

public class HookEventParserTests
{
    public static IEnumerable<object[]> AllFixtures() => Fixtures.AllNames().Select(n => new object[] { n });

    [Theory, MemberData(nameof(AllFixtures))]
    public void Parse_each_fixture_has_event_and_session(string name)
    {
        Assert.True(HookEventParser.TryParse(Fixtures.Text(name), Fixtures.Now, out var e));
        Assert.False(string.IsNullOrEmpty(e.Event));
        Assert.False(string.IsNullOrEmpty(e.SessionId));
    }

    [Fact]
    public void Parse_question_reads_options()
    {
        var e = Fixtures.Event("ask_pre.json");
        Assert.Equal("PreToolUse", e.Event);
        Assert.Equal("AskUserQuestion", e.Tool);
        Assert.Equal("WebShop", e.Project);
        var q = Assert.Single(e.Questions);
        Assert.Equal("Which parent branch should the new feature branch come from?", q.Text);
        Assert.Equal("Parent", q.Header);
        Assert.Equal(new[] { "release/2.0-rc", "main", "Epic branch" }, q.Options);
        Assert.False(q.Multi);
    }

    [Fact]
    public void Parse_agent_fields()
    {
        var e = Fixtures.Event("agent_pre_tool.json");
        Assert.Equal(1794823038515L, e.Ts);
        Assert.Equal("a4dbabcc94daf1c3a", e.AgentId);
        Assert.Equal("Explore", e.AgentType);
        Assert.Equal("Bash", e.Tool);
        Assert.Equal("toolu_01CqfhkQBwZtCLVGFZmK5KxY", e.ToolUseId);
        Assert.Equal("Bash: ls -A1 /home/dev/projects", e.Summary);
    }

    [Fact]
    public void Parse_vscode_source()
    {
        Assert.True(HookEventParser.TryParse("{\"event\":\"Stop\",\"session\":\"s\",\"source\":\"vscode\"}", Fixtures.Now, out var vs));
        Assert.Equal(SourceKind.VsCode, vs.Source);
        Assert.True(HookEventParser.TryParse("{\"event\":\"Stop\",\"session\":\"s\"}", Fixtures.Now, out var cli));
        Assert.Equal(SourceKind.Cli, cli.Source);
    }

    [Fact]
    public void Parse_missing_fields_are_null_and_questions_empty()
    {
        var e = Fixtures.Event("stop.json");
        Assert.Null(e.Tool);
        Assert.Null(e.AgentId);
        Assert.NotNull(e.Questions);
        Assert.Empty(e.Questions);
        Assert.StartsWith("The folder has", e.LastMessage);
    }

    [Fact]
    public void Parse_reads_notification_and_error_fields()
    {
        var n = Fixtures.Event("notification_permission.json");
        Assert.Equal("permission_prompt", n.NotificationType);
        Assert.Equal("Claude needs your permission to use Write", n.Message);
        var f = Fixtures.Event("stop_failure.json");
        Assert.Equal("rate_limit", f.Error);
        Assert.Equal("API Error: Rate limit reached", f.LastMessage);
    }

    [Theory]
    [InlineData("{oops")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{\"ts\":1}")]
    public void Parse_bad_input_returns_false(string json)
    {
        Assert.False(HookEventParser.TryParse(json, Fixtures.Now, out _));
    }

    [Fact]
    public void Parse_sets_received_time()
    {
        var at = new DateTime(2026, 11, 16, 11, 22, 33, DateTimeKind.Utc);
        Assert.Equal(at, Fixtures.Event("stop.json", received: at).ReceivedUtc);
    }
}
