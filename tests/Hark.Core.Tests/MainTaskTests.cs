namespace Hark.Core.Tests;

public class MainTaskTests
{
    private const string Cli = "0c1d2e3f-4a5b-4c6d-8e7f-a0b1c2d3e4f5";

    private static HookEvent Todo(long ts, params (string text, string status)[] items)
    {
        var e = Fixtures.Event("agent_pre_tool.json", ts);
        e.AgentId = null;
        e.AgentType = null;
        e.Tool = "TodoWrite";
        e.Summary = "TodoWrite";
        e.Todos = items.Select(i => new TodoItem { Text = i.text, Status = i.status }).ToList();
        return e;
    }

    private static HookEvent MainStep(string summary, long ts)
    {
        var e = Fixtures.Event("agent_pre_tool.json", ts);
        e.AgentId = null;
        e.AgentType = null;
        e.Summary = summary;
        return e;
    }

    private static WatchState Running(params HookEvent[] events)
    {
        var s = new WatchState();
        s.Apply(Fixtures.Event("user_prompt.json", 50));
        foreach (var e in events)
        {
            s.Apply(e);
        }
        return s;
    }

    private static readonly (string, string)[] FiveItems =
    {
        ("Read the plan", "completed"), ("Write tests", "completed"), ("Running the tests", "in_progress"), ("Fix bugs", "pending"), ("Commit", "pending"),
    };

    [Fact]
    public void Main_todo_list_and_steps_belong_to_the_session()
    {
        var s = Running(Todo(100, FiveItems), MainStep("Bash: dotnet test", 110));
        var session = s.Sessions.Single();
        Assert.Equal(5, session.Todos.Count);
        Assert.Equal(new[] { "TodoWrite", "Bash: dotnet test" }, session.RecentSteps);
        Assert.Empty(session.Agents);
    }

    [Fact]
    public void New_prompt_starts_a_new_task()
    {
        var s = Running(Todo(100, FiveItems));
        s.Apply(Fixtures.Event("user_prompt.json", 200));
        Assert.Empty(s.Sessions.Single().Todos);
        Assert.Empty(s.Sessions.Single().RecentSteps);
    }

    [Fact]
    public void Task_row_comes_first_with_the_current_item_and_real_progress()
    {
        var s = Running(Todo(100, FiveItems));
        var sub = Fixtures.Event("subagent_start.json", 120);
        s.Apply(sub);
        var rows = ViewPlanner.PanelRows(s, Fixtures.Now.AddSeconds(30));
        Assert.Equal(2, rows.Count);
        var task = rows[0];
        Assert.True(task.IsMainTask);
        Assert.Equal("projects", task.PetName);
        Assert.Equal("main task", task.AgentType);
        Assert.Equal("Running the tests", task.Task);
        Assert.Equal("2 of 5 done", task.Progress.Label);
        Assert.Equal(5, task.Todos.Count);
        Assert.False(rows[1].IsMainTask);
    }

    [Fact]
    public void No_task_row_without_a_todo_list_or_after_stop()
    {
        Assert.Empty(ViewPlanner.PanelRows(Running(MainStep("Read a.cs", 100)), Fixtures.Now));
        var done = Running(Todo(100, FiveItems));
        done.Apply(Fixtures.Event("stop.json", 200));
        Assert.Empty(ViewPlanner.PanelRows(done, Fixtures.Now));
    }

    [Fact]
    public void Current_item_falls_back_to_next_pending_then_last_done()
    {
        var pending = Running(Todo(100, ("A", "completed"), ("B", "pending")));
        Assert.Equal("B", ViewPlanner.PanelRows(pending, Fixtures.Now)[0].Task);
        var allDone = Running(Todo(100, ("A", "completed"), ("B", "completed")));
        Assert.Equal("B", ViewPlanner.PanelRows(allDone, Fixtures.Now)[0].Task);
    }

    [Fact]
    public void Task_time_counts_from_the_first_todo_list_when_the_prompt_was_missed()
    {
        var s = new WatchState();
        var first = Todo(100, FiveItems);
        first.ReceivedUtc = Fixtures.Now;
        s.Apply(first);
        var later = MainStep("Read a.cs", 110);
        later.ReceivedUtc = Fixtures.Now.AddSeconds(50);
        s.Apply(later);
        Assert.Equal("1m 20s", ViewPlanner.PanelRows(s, Fixtures.Now.AddSeconds(80))[0].Elapsed);
    }

    [Fact]
    public void Working_session_without_a_list_shows_after_5_seconds()
    {
        var s = Running(MainStep("Read a.cs", 100));
        Assert.Empty(ViewPlanner.PanelRows(s, Fixtures.Now.AddSeconds(4)));
        var row = Assert.Single(ViewPlanner.PanelRows(s, Fixtures.Now.AddSeconds(6)));
        Assert.True(row.IsMainTask);
        Assert.Equal("Read a.cs", row.Task);
        Assert.Equal("~8% · step 1", row.Progress.Label);
        Assert.True(row.Progress.IsEstimate);
        Assert.Equal("projects · working", ViewPlanner.Pill(s, Fixtures.Now.AddSeconds(6)).TaskText);
        Assert.False(ViewPlanner.Pill(s, Fixtures.Now.AddSeconds(4)).Visible);
    }

    [Fact]
    public void Working_session_before_its_first_step_says_thinking()
    {
        var s = Running();
        Assert.Equal("Thinking", Assert.Single(ViewPlanner.PanelRows(s, Fixtures.Now.AddSeconds(10))).Task);
    }

    [Fact]
    public void Quiet_session_hides_after_15_minutes()
    {
        var s = Running(Todo(100, FiveItems));
        Assert.Single(ViewPlanner.PanelRows(s, Fixtures.Now.AddMinutes(14)));
        Assert.Empty(ViewPlanner.PanelRows(s, Fixtures.Now.AddMinutes(16)));
        Assert.False(ViewPlanner.Pill(s, Fixtures.Now.AddMinutes(16)).Visible);
    }

    [Fact]
    public void Finished_task_teaches_the_usual_main_task_steps()
    {
        var s = Running(MainStep("a", 100), MainStep("b", 110), MainStep("c", 120));
        s.Stats.Record("main task", 3);
        s.Stats.Record("main task", 3);
        s.Apply(Fixtures.Event("stop.json", 200, Fixtures.Now.AddSeconds(10)));
        Assert.Equal(3, s.Stats.Typical("main task"));
    }

    [Fact]
    public void Quick_turn_the_pill_never_showed_does_not_teach_the_usual_length()
    {
        // Live 17/11: quick replies taught a usual length of 2 steps, so a real task showed ~95% after 2 steps.
        var s = Running(MainStep("a", 100));
        s.Stats.Record("main task", 9);
        s.Stats.Record("main task", 9);
        s.Apply(Fixtures.Event("stop.json", 200, Fixtures.Now.AddSeconds(3)));
        Assert.Equal(StepStats.DefaultSteps, s.Stats.Typical("main task"));
    }

    [Fact]
    public void Pill_shows_task_progress_without_agents()
    {
        var p = ViewPlanner.Pill(Running(Todo(100, FiveItems)));
        Assert.True(p.Visible);
        Assert.False(p.ShowOpen);
        Assert.Equal("projects · 2 of 5 done", p.TaskText);
        Assert.Equal("", p.AgentsText);
    }

    [Fact]
    public void Pill_counts_several_tasks()
    {
        var s = Running(Todo(100, FiveItems));
        var other = Fixtures.Event("user_prompt.json", 60);
        other.SessionId = "s-other";
        s.Apply(other);
        var todo = Todo(110, ("X", "pending"));
        todo.SessionId = "s-other";
        s.Apply(todo);
        Assert.Equal("2 tasks running", ViewPlanner.Pill(s).TaskText);
    }

    [Fact]
    public void Closing_the_pill_holds_while_the_same_task_runs()
    {
        var s = Running(Todo(100, FiveItems));
        var book = new SnoozeBook();
        book.ClosePill(ViewPlanner.Pill(s));
        s.Apply(Todo(110, ("Read the plan", "completed"), ("Write tests", "completed"), ("Running the tests", "completed"), ("Fix bugs", "in_progress"), ("Commit", "pending")));
        Assert.True(book.IsPillClosed(ViewPlanner.Pill(s)));
    }
}
