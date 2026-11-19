namespace Hark.Core.Tests;

public class ViewPlannerTests
{
    private static readonly DateTime Now = Fixtures.Now;

    private static HookEvent E(string fixture, long ts, int minute = 0, string session = null, string project = null)
    {
        var e = Fixtures.Event(fixture, ts, Now.AddMinutes(minute));
        if (session != null)
        {
            e.SessionId = session;
        }
        if (project != null)
        {
            e.Project = project;
        }
        return e;
    }

    private static WatchState State(params HookEvent[] events)
    {
        var s = new WatchState();
        foreach (var e in events)
        {
            s.Apply(e);
        }
        return s;
    }

    private static IReadOnlyList<Card> Cards(WatchState s, SnoozeBook b = null, int minute = 10) =>
        ViewPlanner.Cards(s, b ?? new SnoozeBook(), Now.AddMinutes(minute));

    [Fact]
    public void Cards_order_questions_before_done()
    {
        var s = State(
            E("stop.json", 50, 0, "s-notes", "Notes"),
            E("ask_pre.json", 100, 1),
            E("permission_request.json", 200, 2));
        Assert.Equal(new[] { CardKind.Question, CardKind.Permission, CardKind.Done }, Cards(s).Select(c => c.Kind));
    }

    [Fact]
    public void Question_card_copy()
    {
        var c = Assert.Single(Cards(State(E("ask_pre.json", 100))));
        Assert.Equal("Question", c.Chip);
        Assert.Equal("WebShop · VS Code", c.Meta);
        Assert.Equal("Which parent branch should the new feature branch come from?", c.Body);
        Assert.Equal(new[] { "release/2.0-rc", "main", "Epic branch" }, c.Options);
        Assert.Equal("Open VS Code", c.OpenLabel);
        Assert.Equal("s-webshop", c.SessionId);
        Assert.Equal(SourceKind.VsCode, c.Source);
        Assert.Equal("WebShop", c.Project);
    }

    [Fact]
    public void Question_card_counts_more_questions()
    {
        var e = E("ask_pre.json", 100);
        e.Questions = new[] { e.Questions[0], new AskQuestion { Text = "Second?", Options = new[] { "a" } } };
        Assert.Equal("Which parent branch should the new feature branch come from? (+1 more)", Assert.Single(Cards(State(e))).Body);
    }

    [Fact]
    public void Permission_card_copy()
    {
        var c = Assert.Single(Cards(State(E("permission_request.json", 200))));
        Assert.Equal("Permission", c.Chip);
        Assert.Equal("Write note.txt", c.Body);
        Assert.Equal("projects · Claude CLI", c.Meta);
        Assert.Equal("Open terminal", c.OpenLabel);
        Assert.Empty(c.Options);
    }

    private static HookEvent Agent(string fixture, long ts, string agentId, string agentType = "Explore")
    {
        var e = E(fixture, ts, 0, "s-notes", "Notes");
        e.AgentId = agentId;
        e.AgentType = agentType;
        return e;
    }

    [Fact]
    public void Done_card_counts_agents()
    {
        var s = State(
            Agent("subagent_start.json", 100, "a1"),
            Agent("subagent_start.json", 101, "a2"),
            Agent("subagent_stop.json", 110, "a1"),
            Agent("subagent_stop.json", 120, "a2"),
            E("stop.json", 130, 0, "s-notes", "Notes"));
        var c = Assert.Single(Cards(s));
        Assert.Equal("Done", c.Chip);
        Assert.Equal("Notes · Claude CLI · 2 agents done", c.Meta);
        Assert.StartsWith("The folder has", c.Body);
    }

    [Fact]
    public void Done_card_says_asks_you_when_the_reply_asks_a_question()
    {
        // Live 19/11: Claude ended with a plain text question and the card said "Done".
        var stop = E("stop.json", 130, 0, "s-notes", "Notes");
        stop.Asks = true;
        var c = Assert.Single(Cards(State(E("user_prompt.json", 90, 0, "s-notes", "Notes"), stop)));
        Assert.Equal("Asks you", c.Chip);
        Assert.True(c.Asks);
        Assert.Equal(CardKind.Done, c.Kind);
        Assert.Empty(c.Options);
    }

    [Fact]
    public void Next_prompt_clears_asks()
    {
        var stop = E("stop.json", 130, 0, "s-notes", "Notes");
        stop.Asks = true;
        var s = State(stop, E("user_prompt.json", 140, 0, "s-notes", "Notes"), E("stop.json", 150, 0, "s-notes", "Notes"));
        Assert.Equal("Done", Assert.Single(Cards(s)).Chip);
    }

    [Fact]
    public void Hidden_helper_agents_are_not_counted_as_done()
    {
        // Live 19/11: a plain chat with no agents showed "1 agent done". Claude Code stops small helper agents
        // (no SubagentStart, no agent type) in the background; only agents Hark saw start or work count.
        var s = State(
            E("user_prompt.json", 90, 0, "s-notes", "Notes"),
            Agent("subagent_stop.json", 110, "helper", agentType: null),
            E("stop.json", 130, 0, "s-notes", "Notes"));
        Assert.Equal("Notes · Claude CLI", Assert.Single(Cards(s)).Meta);
    }

    [Fact]
    public void Done_card_body_is_first_line_cut_to_120()
    {
        var stop = E("stop.json", 100);
        stop.LastMessage = new string('a', 150) + "\nsecond line";
        Assert.Equal(new string('a', 117) + "...", Assert.Single(Cards(State(stop))).Body);
        stop = E("stop.json", 100);
        stop.LastMessage = "Report sent.\nMore detail";
        Assert.Equal("Report sent.", Assert.Single(Cards(State(stop))).Body);
    }

    [Fact]
    public void Done_card_without_text_says_finished()
    {
        var stop = E("stop.json", 100);
        stop.LastMessage = null;
        Assert.Equal("Finished", Assert.Single(Cards(State(stop))).Body);
    }

    [Fact]
    public void Stopped_card_copy()
    {
        var c = Assert.Single(Cards(State(E("stop_failure.json", 100))));
        Assert.Equal(CardKind.Stopped, c.Kind);
        Assert.Equal("Stopped", c.Chip);
        Assert.Equal("API Error: Rate limit reached", c.Body);
    }

    [Fact]
    public void Snoozed_card_hidden_until_time_then_back()
    {
        var s = State(E("ask_pre.json", 100));
        var book = new SnoozeBook();
        var id = Cards(s, book).Single().Id;
        book.Snooze(id, Now.AddMinutes(15));
        Assert.Empty(Cards(s, book, 14));
        Assert.Single(Cards(s, book, 15));
    }

    [Fact]
    public void Dismissed_card_stays_hidden_while_same_id()
    {
        var s = State(E("ask_pre.json", 100));
        var book = new SnoozeBook();
        book.Dismiss(Cards(s, book).Single().Id);
        Assert.Empty(Cards(s, book, 500));
    }

    [Fact]
    public void New_pending_gets_new_id_after_dismiss()
    {
        var s = State(E("ask_pre.json", 100));
        var book = new SnoozeBook();
        book.Dismiss(Cards(s, book).Single().Id);
        s.Apply(E("ask_post.json", 200, 3));
        s.Apply(E("ask_pre.json", 300, 5));
        Assert.Single(Cards(s, book));
    }

    [Fact]
    public void Forget_drops_ids_that_are_gone()
    {
        var book = new SnoozeBook();
        book.Dismiss("a");
        book.Snooze("b", Now.AddHours(1));
        book.Forget(new[] { "b" });
        Assert.False(book.IsHidden("a", Now));
        Assert.True(book.IsHidden("b", Now));
    }

    [Theory]
    [InlineData(59, "now")]
    [InlineData(60, "1 min ago")]
    [InlineData(125, "2 min ago")]
    [InlineData(3599, "59 min ago")]
    [InlineData(7200, "2 h ago")]
    public void Ago_strings(int seconds, string expected)
    {
        Assert.Equal(expected, ViewPlanner.Ago(Now, Now.AddSeconds(seconds)));
    }

    [Fact]
    public void Ago_never_negative()
    {
        Assert.Equal("now", ViewPlanner.Ago(Now, Now.AddSeconds(-30)));
    }

    [Fact]
    public void Pill_one_question()
    {
        var p = ViewPlanner.Pill(State(E("ask_pre.json", 100)));
        Assert.True(p.Visible);
        Assert.True(p.ShowOpen);
        Assert.Equal("WebShop needs your answer", p.Text);
        Assert.Equal("", p.AgentsText);
        Assert.Equal("s-webshop", p.TargetSessionId);
    }

    [Fact]
    public void Pill_one_permission()
    {
        Assert.Equal("projects needs your permission", ViewPlanner.Pill(State(E("permission_request.json", 100))).Text);
    }

    [Fact]
    public void Pill_two_sessions()
    {
        var p = ViewPlanner.Pill(State(E("ask_pre.json", 100, 1), E("permission_request.json", 200, 0)));
        Assert.Equal("2 sessions need you", p.Text);
        Assert.NotEqual("s-webshop", p.TargetSessionId);
    }

    [Fact]
    public void Pill_agents_only()
    {
        var two = E("subagent_start.json", 110);
        two.AgentId = "agent-two";
        var p = ViewPlanner.Pill(State(E("subagent_start.json", 100), two));
        Assert.True(p.Visible);
        Assert.False(p.ShowOpen);
        Assert.Equal("Agents working", p.Text);
        Assert.Equal("2 agents running", p.AgentsText);
    }

    [Fact]
    public void Pill_question_with_one_agent()
    {
        var agent = E("subagent_start.json", 110, 0, "s-webshop");
        var p = ViewPlanner.Pill(State(E("ask_pre.json", 100), agent));
        Assert.Equal("WebShop needs your answer", p.Text);
        Assert.Equal("1 agent running", p.AgentsText);
    }

    [Fact]
    public void Pill_close_hides_until_something_new()
    {
        var book = new SnoozeBook();
        var s = State(E("subagent_start.json", 100));
        book.ClosePill(ViewPlanner.Pill(s));
        Assert.True(book.IsPillClosed(ViewPlanner.Pill(s)));

        var second = E("subagent_start.json", 110);
        second.AgentId = "agent-two";
        s.Apply(second);
        Assert.True(book.IsPillClosed(ViewPlanner.Pill(s)));

        s.Apply(E("ask_pre.json", 120));
        Assert.False(book.IsPillClosed(ViewPlanner.Pill(s)));
    }

    [Fact]
    public void Pill_close_ends_when_agents_finish()
    {
        var book = new SnoozeBook();
        var s = State(E("subagent_start.json", 100));
        book.ClosePill(ViewPlanner.Pill(s));
        s.Apply(E("subagent_stop.json", 200));
        Assert.False(ViewPlanner.Pill(s).Visible);
        Assert.False(book.IsPillClosed(ViewPlanner.Pill(s)));
        s.Apply(E("subagent_start.json", 300));
        Assert.False(book.IsPillClosed(ViewPlanner.Pill(s)));
    }

    [Fact]
    public void Pill_hidden_when_idle()
    {
        Assert.False(ViewPlanner.Pill(State(E("session_start.json", 100))).Visible);
    }
}
