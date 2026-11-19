using System.Text.Json;

namespace Hark.Core.Tests;

public class AnswerBuilderTests
{
    private static readonly AskQuestion Single = new AskQuestion { Text = "Which parent branch?", Options = new[] { "main", "release/2.0-rc", "Epic branch" } };
    private static readonly AskQuestion Multi = new AskQuestion { Text = "Which checks?", Options = new[] { "Unit", "Lint", "E2E" }, Multi = true };

    [Fact]
    public void Single_choice_gives_its_label()
    {
        var a = AnswerBuilder.Build(new[] { Single }, new[] { new HashSet<string> { "main" } }, new[] { "" });
        Assert.Equal(new Dictionary<string, string> { ["Which parent branch?"] = "main" }, a);
    }

    [Fact]
    public void Multi_choice_joins_labels_in_option_order()
    {
        var a = AnswerBuilder.Build(new[] { Multi }, new[] { new HashSet<string> { "E2E", "Unit" } }, new[] { "" });
        Assert.Equal("Unit, E2E", a!["Which checks?"]);
    }

    [Fact]
    public void Own_text_wins_over_chosen_options()
    {
        var a = AnswerBuilder.Build(new[] { Single }, new[] { new HashSet<string> { "main" } }, new[] { "  use feat/PROJ-2070  " });
        Assert.Equal("use feat/PROJ-2070", a!["Which parent branch?"]);
    }

    [Fact]
    public void Every_question_needs_an_answer()
    {
        Assert.Null(AnswerBuilder.Build(new[] { Single, Multi }, new[] { new HashSet<string> { "main" }, new HashSet<string>() }, new[] { "", " " }));
        Assert.NotNull(AnswerBuilder.Build(new[] { Single, Multi }, new[] { new HashSet<string> { "main" }, new HashSet<string>() }, new[] { "", "none" }));
    }

    [Fact]
    public void Labels_that_are_not_options_are_ignored()
    {
        Assert.Null(AnswerBuilder.Build(new[] { Single }, new[] { new HashSet<string> { "develop" } }, new[] { "" }));
    }
}

public class AnswerChannelTests
{
    [Fact]
    public void Held_question_is_seen_and_answer_written()
    {
        using var t = new TempDir();
        var channel = new AnswerChannel(t.Path);
        Assert.False(channel.IsHeld("toolu_ask1"));
        t.File("held/toolu_ask1.json", "{}");
        Assert.True(channel.IsHeld("toolu_ask1"));
        channel.Send("toolu_ask1", new Dictionary<string, string> { ["Which parent branch?"] = "main" });
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(t.Path, "answers", "toolu_ask1.json")));
        Assert.Equal("answer", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal("main", doc.RootElement.GetProperty("answers").GetProperty("Which parent branch?").GetString());
        Assert.Empty(Directory.GetFiles(Path.Combine(t.Path, "answers"), "*.tmp"));
    }

    [Fact]
    public void Release_tells_the_hook_to_use_vscode()
    {
        using var t = new TempDir();
        new AnswerChannel(t.Path).Release("toolu_ask1");
        Assert.Contains("\"vscode\"", File.ReadAllText(Path.Combine(t.Path, "answers", "toolu_ask1.json")));
    }

    [Fact]
    public void Paused_marker_follows_the_setting()
    {
        using var t = new TempDir();
        var channel = new AnswerChannel(t.Path);
        channel.SetPaused(true);
        Assert.True(File.Exists(Path.Combine(t.Path, "paused")));
        channel.SetPaused(false);
        Assert.False(File.Exists(Path.Combine(t.Path, "paused")));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsafe_ids_are_never_used_as_file_names(string id)
    {
        using var t = new TempDir();
        var channel = new AnswerChannel(t.Path);
        Assert.False(channel.IsHeld(id));
        channel.Send(id, new Dictionary<string, string> { ["q"] = "a" });
        channel.Release(id);
        Assert.False(Directory.Exists(Path.Combine(t.Path, "answers")));
    }

    [Fact]
    public void Purge_removes_old_answers_and_held_marks()
    {
        using var t = new TempDir();
        var old = t.File("answers/old.json", "{}");
        var held = t.File("held/old.json", "{}");
        var fresh = t.File("answers/new.json", "{}");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(held, DateTime.UtcNow.AddHours(-1));
        new AnswerChannel(t.Path).PurgeOld(DateTime.UtcNow, TimeSpan.FromMinutes(31));
        Assert.False(File.Exists(old));
        Assert.False(File.Exists(held));
        Assert.True(File.Exists(fresh));
    }
}

public class AnswerCardTests
{
    [Fact]
    public void Question_card_carries_call_id_questions_and_descriptions()
    {
        var e = Fixtures.Event("ask_pre.json", 100);
        e.Questions = new[]
        {
            new AskQuestion { Text = "Q1?", Options = new[] { "a", "b" }, Descriptions = new[] { "first", "second" } },
            new AskQuestion { Text = "Q2?", Options = new[] { "c" }, Multi = true },
        };
        var s = new WatchState();
        s.Apply(e);
        var card = Assert.Single(ViewPlanner.Cards(s, new SnoozeBook(), Fixtures.Now));
        Assert.Equal("toolu_ask1", card.AnswerId);
        Assert.Equal(2, card.Questions.Count);
        Assert.Equal(new[] { "first", "second" }, card.Questions[0].Descriptions);
    }

    [Fact]
    public void Parser_reads_option_descriptions()
    {
        Assert.True(HookEventParser.TryParse(
            "{\"event\":\"PreToolUse\",\"session\":\"s\",\"tool\":\"AskUserQuestion\",\"questions\":[{\"question\":\"Q?\",\"options\":[\"a\",\"b\"],\"descriptions\":[\"da\",\"db\"],\"multi\":true}]}",
            Fixtures.Now, out var e));
        Assert.Equal(new[] { "da", "db" }, e.Questions[0].Descriptions);
        Assert.True(e.Questions[0].Multi);
    }
}
