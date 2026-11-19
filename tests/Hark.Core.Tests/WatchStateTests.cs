namespace Hark.Core.Tests;

public class WatchStateTests
{
    private const string Cli = "0c1d2e3f-4a5b-4c6d-8e7f-a0b1c2d3e4f5";

    private static HookEvent E(string fixture, long ts, string session = null, DateTime? received = null)
    {
        var e = Fixtures.Event(fixture, ts, received);
        if (session != null)
        {
            e.SessionId = session;
        }
        return e;
    }

    private static HookEvent MainTool(string session, string tool, long ts, string ev = "PreToolUse")
    {
        var e = E("agent_pre_tool.json", ts, session);
        e.AgentId = null;
        e.AgentType = null;
        e.Tool = tool;
        e.Event = ev;
        return e;
    }

    private static SessionInfo Only(WatchState s) => Assert.Single(s.Sessions);

    [Fact]
    public void Question_sets_waiting()
    {
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        var x = Only(s);
        Assert.Equal("WebShop", x.Project);
        Assert.Equal(SourceKind.VsCode, x.Source);
        Assert.Equal(SessionStatus.Waiting, x.Status);
        Assert.Equal(PendingKind.Question, x.Pending.Kind);
        Assert.Equal(new[] { "release/2.0-rc", "main", "Epic branch" }, x.Pending.Questions[0].Options);
    }

    [Fact]
    public void Permission_request_for_a_question_keeps_the_question()
    {
        // Seen for real in VS Code: PreToolUse AskUserQuestion, then PermissionRequest for AskUserQuestion.
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        var ask = E("ask_pre.json", 110);
        ask.Event = "PermissionRequest";
        ask.Summary = "AskUserQuestion";
        s.Apply(ask);
        Assert.Equal(PendingKind.Question, Only(s).Pending.Kind);
        Assert.Equal(3, Only(s).Pending.Questions[0].Options.Count);
        Assert.Equal(SessionStatus.Waiting, Only(s).Status);
    }

    [Fact]
    public void Question_clears_on_post()
    {
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        s.Apply(E("ask_post.json", 200));
        Assert.Null(Only(s).Pending);
        Assert.Equal(SessionStatus.Working, Only(s).Status);
    }

    [Fact]
    public void Question_clears_on_stop()
    {
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        s.Apply(E("stop.json", 200, "s-webshop"));
        Assert.Null(Only(s).Pending);
        Assert.Equal(SessionStatus.Done, Only(s).Status);
    }

    [Fact]
    public void Permission_sets_waiting_and_clears_on_post_tool()
    {
        var s = new WatchState();
        var req = E("permission_request.json", 200);
        s.Apply(req);
        Assert.Equal(SessionStatus.Waiting, Only(s).Status);
        Assert.Equal(PendingKind.Permission, Only(s).Pending.Kind);
        Assert.Equal("Write note.txt", Only(s).Pending.Summary);
        s.Apply(MainTool(req.SessionId, "Write", 300, "PostToolUse"));
        Assert.Null(Only(s).Pending);
        Assert.Equal(SessionStatus.Working, Only(s).Status);
    }

    [Fact]
    public void Permission_clears_on_next_tool_call()
    {
        var s = new WatchState();
        var req = E("permission_request.json", 200);
        s.Apply(req);
        s.Apply(MainTool(req.SessionId, "Bash", 300));
        Assert.Null(Only(s).Pending);
    }

    [Fact]
    public void Notification_permission_only_when_no_pending()
    {
        var s = new WatchState();
        s.Apply(E("notification_permission.json", 100));
        Assert.Equal(PendingKind.Permission, Only(s).Pending.Kind);
        Assert.Equal("Claude needs your permission to use Write", Only(s).Pending.Summary);

        var t = new WatchState();
        t.Apply(E("permission_request.json", 200));
        t.Apply(E("notification_permission.json", 250));
        Assert.Equal("Write note.txt", Only(t).Pending.Summary);
    }

    [Fact]
    public void Subagent_tool_calls_count_steps()
    {
        var s = new WatchState();
        s.Apply(E("permission_request.json", 90, Cli));
        s.Apply(E("subagent_start.json", 100));
        s.Apply(E("agent_pre_tool.json", 110));
        s.Apply(E("agent_pre_tool.json", 120));
        var last = E("agent_pre_tool.json", 130);
        last.Summary = "Grep \"pageSize\"";
        s.Apply(last);
        var x = Only(s);
        var agent = Assert.Single(x.Agents.Values);
        Assert.Equal("Explore", agent.AgentType);
        Assert.Equal(3, agent.Steps);
        Assert.Equal("Grep \"pageSize\"", agent.LastTool);
        Assert.NotNull(x.Pending);
    }

    [Fact]
    public void Subagent_stop_counts_done_this_turn()
    {
        var s = new WatchState();
        s.Apply(E("subagent_start.json", 100));
        s.Apply(E("subagent_stop.json", 200));
        Assert.Empty(Only(s).Agents);
        Assert.Equal(1, Only(s).AgentsDoneThisTurn);
        s.Apply(E("user_prompt.json", 300));
        Assert.Equal(0, Only(s).AgentsDoneThisTurn);
    }

    [Fact]
    public void Stop_sets_done_text()
    {
        var s = new WatchState();
        var at = Fixtures.Now.AddMinutes(3);
        s.Apply(E("stop.json", 100, received: at));
        Assert.Equal(SessionStatus.Done, Only(s).Status);
        Assert.StartsWith("The folder has", Only(s).DoneText);
        Assert.Equal(at, Only(s).DoneUtc);
    }

    [Fact]
    public void Stop_failure_sets_stopped()
    {
        var s = new WatchState();
        s.Apply(E("stop_failure.json", 100));
        Assert.Equal(SessionStatus.Stopped, Only(s).Status);
        Assert.Equal("API Error: Rate limit reached", Only(s).DoneText);
    }

    [Fact]
    public void User_prompt_starts_working_and_clears_done()
    {
        var s = new WatchState();
        s.Apply(E("stop.json", 100));
        s.Apply(E("user_prompt.json", 200));
        Assert.Equal(SessionStatus.Working, Only(s).Status);
        Assert.Null(Only(s).DoneText);
    }

    [Fact]
    public void Session_end_removes_session()
    {
        var s = new WatchState();
        s.Apply(E("session_start.json", 100));
        s.Apply(E("session_end.json", 200));
        Assert.Empty(s.Sessions);
    }

    [Fact]
    public void Two_sessions_same_project_stay_separate()
    {
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        s.Apply(E("ask_pre.json", 110, "s-two"));
        Assert.Equal(2, s.Sessions.Count);
        Assert.All(s.Sessions, x => Assert.Equal(SessionStatus.Waiting, x.Status));
    }

    [Fact]
    public void Late_older_event_does_not_clear_newer_pending()
    {
        var s = new WatchState();
        var req = E("permission_request.json", 200);
        s.Apply(req);
        s.Apply(MainTool(req.SessionId, "Write", 150));
        Assert.NotNull(Only(s).Pending);
    }

    [Fact]
    public void Since_uses_received_time_not_ts()
    {
        var s = new WatchState();
        var e = E("ask_pre.json", 1794823100000 + 3_600_000);
        s.Apply(e);
        Assert.Equal(Fixtures.Now, Only(s).Pending.SinceUtc);
    }

    [Fact]
    public void Apply_batch_sorts_by_ts()
    {
        var s = new WatchState();
        s.ApplyBatch(new[] { E("ask_post.json", 200), E("ask_pre.json", 100) });
        Assert.Null(Only(s).Pending);
    }

    private static HookEvent Call(string session, string ev, string tool, string toolUseId, long ts)
    {
        var e = MainTool(session, tool, ts, ev);
        e.ToolUseId = toolUseId;
        e.Summary = tool + " x";
        return e;
    }

    [Fact]
    public void Same_call_pretool_with_later_ts_keeps_permission()
    {
        // Review Important 1: the async recorder can stamp the PreToolUse of the same call after the PermissionRequest.
        var s = new WatchState();
        var req = E("permission_request.json", 200);
        s.Apply(req);
        s.Apply(Call(req.SessionId, "PreToolUse", "Write", "t1", 210));
        Assert.NotNull(Only(s).Pending);
        s.Apply(Call(req.SessionId, "PostToolUse", "Write", "t1", 300));
        Assert.Null(Only(s).Pending);
    }

    [Fact]
    public void Previous_call_post_does_not_clear_linked_permission()
    {
        var s = new WatchState();
        var req = E("permission_request.json", 110);
        req.Tool = "Bash";
        s.Apply(Call(req.SessionId, "PreToolUse", "Bash", "t0", 50));
        s.Apply(Call(req.SessionId, "PreToolUse", "Bash", "t1", 100));
        s.Apply(req);
        s.Apply(Call(req.SessionId, "PostToolUse", "Bash", "t0", 120));
        Assert.NotNull(Only(s).Pending);
        s.Apply(Call(req.SessionId, "PostToolUse", "Bash", "t1", 130));
        Assert.Null(Only(s).Pending);
    }

    [Fact]
    public void Late_pending_after_stop_is_ignored()
    {
        var s = new WatchState();
        s.Apply(E("stop.json", 300, "s-x"));
        s.Apply(E("permission_request.json", 200, "s-x"));
        Assert.Null(Only(s).Pending);
        Assert.Equal(SessionStatus.Done, Only(s).Status);
    }

    [Fact]
    public void Late_stop_after_new_prompt_is_ignored()
    {
        var s = new WatchState();
        s.Apply(E("user_prompt.json", 400));
        s.Apply(E("stop.json", 300));
        Assert.Equal(SessionStatus.Working, Only(s).Status);
    }

    [Fact]
    public void Late_event_after_session_end_does_not_create_session()
    {
        var s = new WatchState();
        s.Apply(E("session_end.json", 300, "s-x"));
        s.Apply(E("permission_request.json", 290, "s-x"));
        s.Apply(E("subagent_start.json", 295, "s-x"));
        Assert.Empty(s.Sessions);
        s.Apply(E("session_start.json", 500, "s-x"));
        Assert.Single(s.Sessions);
    }

    [Fact]
    public void Drop_stale_removes_idle_agents()
    {
        var s = new WatchState();
        s.Apply(E("subagent_start.json", 100));
        s.Apply(E("user_prompt.json", 200, received: Fixtures.Now.AddMinutes(40)));
        s.DropStale(Fixtures.Now.AddMinutes(29));
        Assert.Single(Only(s).Agents);
        s.DropStale(Fixtures.Now.AddMinutes(31));
        Assert.Empty(Only(s).Agents);
    }

    [Fact]
    public void Drop_stale_keeps_waiting_sessions_12_hours()
    {
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        s.DropStale(Fixtures.Now.AddHours(11));
        Assert.Single(s.Sessions);
        s.DropStale(Fixtures.Now.AddHours(13));
        Assert.Empty(s.Sessions);
    }

    [Fact]
    public void Project_stays_from_first_event()
    {
        var s = new WatchState();
        s.Apply(E("ask_pre.json", 100));
        var later = E("agent_pre_tool.json", 200, "s-webshop");
        later.Project = "Hark";
        later.Cwd = "/home/dev/projects/WebShop/Hark";
        s.Apply(later);
        Assert.Equal("WebShop", Only(s).Project);
        Assert.Equal("/home/dev/projects/WebShop/Hark", Only(s).Cwd);
    }

    [Fact]
    public void Drop_stale_after_two_hours()
    {
        var s = new WatchState();
        s.Apply(E("session_start.json", 100));
        s.DropStale(Fixtures.Now.AddMinutes(119));
        Assert.Single(s.Sessions);
        s.DropStale(Fixtures.Now.AddMinutes(121));
        Assert.Empty(s.Sessions);
    }
}
