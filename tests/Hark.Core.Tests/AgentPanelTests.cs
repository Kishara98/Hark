namespace Hark.Core.Tests;

public class PetBookTests
{
    [Fact]
    public void Running_agents_get_different_pets()
    {
        var pets = new PetBook();
        var ids = new[] { "a", "b", "c" }.Select(pets.Assign).ToList();
        Assert.Equal(new[] { 0, 1, 2 }, ids);
        Assert.Equal("Fox", PetBook.Names[ids[0]]);
        Assert.Equal(0, pets.Assign("a"));
    }

    [Fact]
    public void A_freed_pet_is_used_again_when_no_other_is_free()
    {
        var pets = new PetBook();
        for (var i = 0; i < 8; i++)
        {
            pets.Assign("a" + i);
        }
        pets.Release("a0");
        Assert.Equal(0, pets.Assign("new"));
    }

    [Fact]
    public void A_resumed_agent_gets_its_pet_back()
    {
        var pets = new PetBook();
        Assert.Equal(0, pets.Assign("dev"));
        pets.Release("dev");
        Assert.Equal(1, pets.Assign("rev"));
        Assert.Equal(0, pets.Assign("dev"));
    }

    [Fact]
    public void More_than_eight_agents_wrap_around()
    {
        var pets = new PetBook();
        for (var i = 0; i < 8; i++)
        {
            pets.Assign("a" + i);
        }
        Assert.InRange(pets.Assign("ninth"), 0, 7);
    }
}

public class StepStatsTests
{
    [Fact]
    public void Typical_defaults_to_twelve()
    {
        Assert.Equal(12, new StepStats().Typical("Explore"));
    }

    [Fact]
    public void Typical_needs_three_runs_before_it_trusts_them()
    {
        // Live 18/11: one learned run of 1 step made a real task show ~80% at step 4.
        var s = new StepStats();
        s.Record("main task", 1);
        s.Record("main task", 4);
        Assert.Equal(12, s.Typical("main task"));
        s.Record("main task", 6);
        Assert.Equal(4, s.Typical("main task"));
    }

    [Fact]
    public void Typical_is_the_median_of_recent_runs()
    {
        var s = new StepStats();
        foreach (var n in new[] { 4, 30, 10, 8, 9 })
        {
            s.Record("Explore", n);
        }
        Assert.Equal(9, s.Typical("Explore"));
        Assert.Equal(12, s.Typical("Plan"));
    }

    [Fact]
    public void Keeps_the_last_twenty_runs()
    {
        var s = new StepStats();
        for (var i = 0; i < 20; i++)
        {
            s.Record("Explore", 100);
        }
        for (var i = 0; i < 20; i++)
        {
            s.Record("Explore", 5);
        }
        Assert.Equal(5, s.Typical("Explore"));
    }

    [Fact]
    public void Saves_and_loads()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "agent-stats.json");
        var s = new StepStats();
        foreach (var n in new[] { 7, 7, 7 })
        {
            s.Record("Explore", n);
        }
        s.Save(path);
        Assert.Equal(7, StepStats.Load(path).Typical("Explore"));
        Assert.Equal(12, StepStats.Load(Path.Combine(t.Path, "missing.json")).Typical("Explore"));
        File.WriteAllText(path, "{bad");
        Assert.Equal(12, StepStats.Load(path).Typical("Explore"));
    }
}

public class AgentStateTests
{
    private const string Cli = "0c1d2e3f-4a5b-4c6d-8e7f-a0b1c2d3e4f5";

    private static HookEvent Launch(string type, string task, long ts)
    {
        var e = Fixtures.Event("agent_pre_tool.json", ts);
        e.AgentId = null;
        e.AgentType = null;
        e.Tool = "Agent";
        e.SpawnType = type;
        e.Summary = "Agent: " + task;
        return e;
    }

    private static HookEvent Start(string id, string type, long ts)
    {
        var e = Fixtures.Event("subagent_start.json", ts);
        e.AgentId = id;
        e.AgentType = type;
        return e;
    }

    private static HookEvent Step(string id, string summary, long ts)
    {
        var e = Fixtures.Event("agent_pre_tool.json", ts);
        e.AgentId = id;
        e.Summary = summary;
        return e;
    }

    private static AgentInfo Agent(WatchState s, string id) => s.Sessions.Single().Agents[id];

    [Fact]
    public void Agent_gets_its_task_from_the_launch()
    {
        var s = new WatchState();
        s.Apply(Launch("Plan", "Draft the hook setup", 100));
        s.Apply(Launch("Explore", "Find split logic", 110));
        s.Apply(Start("a1", "Explore", 120));
        s.Apply(Start("a2", "Plan", 130));
        Assert.Equal("Find split logic", Agent(s, "a1").Task);
        Assert.Equal("Draft the hook setup", Agent(s, "a2").Task);
    }

    [Fact]
    public void Agent_without_a_launch_uses_its_type()
    {
        var s = new WatchState();
        s.Apply(Start("a1", "Explore", 100));
        Assert.Equal("Explore", Agent(s, "a1").Task);
    }

    [Fact]
    public void Todo_list_updates_the_agent()
    {
        var s = new WatchState();
        s.Apply(Start("a1", "general-purpose", 100));
        var todo = Step("a1", "TodoWrite", 110);
        todo.Tool = "TodoWrite";
        todo.Todos = new[] { new TodoItem { Text = "Fetch", Status = "completed" }, new TodoItem { Text = "Run tests", Status = "in_progress" } };
        s.Apply(todo);
        Assert.Equal(2, Agent(s, "a1").Todos.Count);
        Assert.Equal("in_progress", Agent(s, "a1").Todos[1].Status);
    }

    [Fact]
    public void Recent_steps_keep_the_last_five()
    {
        var s = new WatchState();
        s.Apply(Start("a1", "Explore", 100));
        for (var i = 1; i <= 7; i++)
        {
            s.Apply(Step("a1", "Read f" + i, 100 + i));
        }
        Assert.Equal(new[] { "Read f3", "Read f4", "Read f5", "Read f6", "Read f7" }, Agent(s, "a1").RecentSteps);
        Assert.Equal(7, Agent(s, "a1").Steps);
    }

    [Fact]
    public void Finished_agent_teaches_the_typical_steps_and_frees_its_pet()
    {
        var s = new WatchState();
        s.Stats.Record("Explore", 4);
        s.Stats.Record("Explore", 4);
        s.Apply(Start("a1", "Explore", 100));
        for (var i = 1; i <= 4; i++)
        {
            s.Apply(Step("a1", "Read", 100 + i));
        }
        var stop = Fixtures.Event("subagent_stop.json", 200);
        stop.AgentId = "a1";
        stop.AgentType = "Explore";
        s.Apply(stop);
        Assert.Equal(4, s.Stats.Typical("Explore"));
        Assert.False(s.Sessions.Single().Agents.ContainsKey("a1"));
        s.Apply(Start("a2", "Explore", 300));
        Assert.Equal(1, Agent(s, "a2").PetIndex); // Fox rests for a1 in case it is resumed
    }

    private static HookEvent StopOf(string id, long ts)
    {
        var e = Fixtures.Event("subagent_stop.json", ts);
        e.AgentId = id;
        e.AgentType = "general-purpose";
        return e;
    }

    // Live order 17/11: the developer hands back, the reviewer is launched, and the developer is resumed
    // (SendMessage, a second SubagentStart with the same id) in the same second the reviewer starts.
    private static WatchState DeveloperResumed(bool resumedFirst)
    {
        var s = new WatchState();
        s.Apply(Launch("general-purpose", "Developer agent: Task 3", 100));
        s.Apply(Start("dev", "general-purpose", 110));
        s.Apply(Step("dev", "Bash: mvn test", 120));
        s.Apply(StopOf("dev", 200));
        s.Apply(Launch("general-purpose", "Reviewer agent: Task 3", 210));
        s.Apply(Start(resumedFirst ? "dev" : "rev", "general-purpose", 220));
        s.Apply(Start(resumedFirst ? "rev" : "dev", "general-purpose", 220));
        return s;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resumed_agent_keeps_its_task_and_leaves_the_new_launch_alone(bool resumedFirst)
    {
        var s = DeveloperResumed(resumedFirst);
        Assert.Equal("Developer agent: Task 3", Agent(s, "dev").Task);
        Assert.Equal("Reviewer agent: Task 3", Agent(s, "rev").Task);
    }

    [Fact]
    public void Resumed_agent_keeps_its_pet()
    {
        var s = DeveloperResumed(resumedFirst: false);
        Assert.Equal(0, Agent(s, "dev").PetIndex);
        Assert.Equal(1, Agent(s, "rev").PetIndex);
    }

    [Fact]
    public void Second_start_for_a_running_agent_keeps_its_steps()
    {
        var s = new WatchState();
        s.Apply(Launch("Explore", "Find split logic", 100));
        s.Apply(Start("a1", "Explore", 110));
        s.Apply(Step("a1", "Read a.cs", 120));
        s.Apply(Step("a1", "Read b.cs", 130));
        s.Apply(Start("a1", "Explore", 140));
        Assert.Equal(2, Agent(s, "a1").Steps);
        Assert.Equal("Find split logic", Agent(s, "a1").Task);
    }

    private static HookEvent Named(HookEvent e, string task)
    {
        e.AgentTask = task;
        return e;
    }

    [Fact]
    public void Agent_task_on_the_start_names_the_agent_and_uses_up_its_launch()
    {
        var s = new WatchState();
        s.Apply(Launch("general-purpose", "Developer agent", 100));
        s.Apply(Launch("general-purpose", "Reviewer agent", 101));
        s.Apply(Named(Start("rev", "general-purpose", 110), "Reviewer agent"));
        s.Apply(Start("dev", "general-purpose", 111));
        Assert.Equal("Reviewer agent", Agent(s, "rev").Task);
        Assert.Equal("Developer agent", Agent(s, "dev").Task);
    }

    [Fact]
    public void Step_with_agent_task_names_an_agent_seen_first_after_a_restart()
    {
        // Live 18/11: Hark restarted after the developer agent's launch, so its row said "general-purpose".
        var s = new WatchState();
        s.Apply(Named(Step("dev", "Bash: mvn test", 100), "Developer agent: PROJ-2516 Task 3"));
        Assert.Equal("Developer agent: PROJ-2516 Task 3", Agent(s, "dev").Task);
    }

    [Fact]
    public void Agent_task_corrects_a_wrongly_matched_launch()
    {
        var s = new WatchState();
        s.Apply(Launch("general-purpose", "Reviewer agent", 100));
        s.Apply(Start("dev", "general-purpose", 110));
        Assert.Equal("Reviewer agent", Agent(s, "dev").Task);
        s.Apply(Named(Step("dev", "Read a.cs", 120), "Developer agent"));
        Assert.Equal("Developer agent", Agent(s, "dev").Task);
    }

    [Fact]
    public void Start_read_after_the_first_step_still_links_the_task()
    {
        var s = new WatchState();
        s.Apply(Launch("Explore", "Find split logic", 100));
        s.Apply(Step("a1", "Read a.cs", 120));
        s.Apply(Start("a1", "Explore", 110));
        Assert.Equal(1, Agent(s, "a1").Steps);
        Assert.Equal("Find split logic", Agent(s, "a1").Task);
    }

    [Fact]
    public void Agents_in_two_sessions_get_different_pets()
    {
        var s = new WatchState();
        s.Apply(Start("a1", "Explore", 100));
        var other = Start("b1", "Explore", 110);
        other.SessionId = "s-other";
        s.Apply(other);
        var pets = s.Sessions.SelectMany(x => x.Agents.Values).Select(a => a.PetIndex).ToList();
        Assert.Equal(pets.Count, pets.Distinct().Count());
    }
}

public class AgentRowTests
{
    private static AgentInfo Agent(int steps, params (string text, string status)[] todos) => new AgentInfo
    {
        AgentId = "a1",
        AgentType = "Explore",
        Task = "Find split logic",
        Steps = steps,
        StartedUtc = Fixtures.Now,
        Todos = todos.Select(t => new TodoItem { Text = t.text, Status = t.status }).ToList(),
    };

    [Fact]
    public void Todo_list_gives_real_progress()
    {
        var p = AgentProgress.For(Agent(9, ("a", "completed"), ("b", "completed"), ("c", "completed"), ("d", "in_progress"), ("e", "pending")), new StepStats());
        Assert.Equal(0.6, p.Fraction, 3);
        Assert.Equal("3 of 5 done", p.Label);
        Assert.False(p.IsEstimate);
    }

    [Fact]
    public void Estimate_is_half_full_at_the_usual_length()
    {
        var stats = new StepStats();
        foreach (var n in new[] { 10, 10, 10 })
        {
            stats.Record("Explore", n);
        }
        var p = AgentProgress.For(Agent(10), stats);
        Assert.Equal(0.5, p.Fraction, 3);
        Assert.Equal("~50% · step 10", p.Label);
        Assert.True(p.IsEstimate);
        Assert.Equal("~38% · step 6", AgentProgress.For(Agent(6), stats).Label);
    }

    [Fact]
    public void Estimate_keeps_moving_past_the_usual_length()
    {
        // Live 17/11: a developer agent at step 33 when 18 is usual showed ~95% and stayed there.
        Assert.Equal("~65% · step 33", AgentProgress.Estimate(33, 18).Label);
        var earlier = AgentProgress.Estimate(40, 12).Fraction;
        var later = AgentProgress.Estimate(41, 12).Fraction;
        Assert.True(later > earlier);
        Assert.True(AgentProgress.Estimate(1000, 12).Fraction < 1);
    }

    [Theory]
    [InlineData(25, "25s")]
    [InlineData(80, "1m 20s")]
    [InlineData(3900, "1h 05m")]
    public void Elapsed_text(int seconds, string expected)
    {
        Assert.Equal(expected, ViewPlanner.Elapsed(Fixtures.Now, Fixtures.Now.AddSeconds(seconds)));
    }

    [Fact]
    public void Rows_show_pet_task_and_progress_in_start_order()
    {
        var s = new WatchState();
        var first = Fixtures.Event("subagent_start.json", 100, Fixtures.Now);
        first.AgentId = "a1";
        var second = Fixtures.Event("subagent_start.json", 110, Fixtures.Now.AddSeconds(5));
        second.AgentId = "a2";
        s.Apply(first);
        s.Apply(second);
        var rows = ViewPlanner.Agents(s, Fixtures.Now.AddSeconds(80));
        Assert.Equal(new[] { "a1", "a2" }, rows.Select(r => r.AgentId));
        Assert.Equal("Fox", rows[0].PetName);
        Assert.Equal("Owl", rows[1].PetName);
        Assert.Equal("Explore", rows[0].Task);
        Assert.Equal("1m 20s", rows[0].Elapsed);
        Assert.Equal("~0% · step 0", rows[0].Progress.Label);
        Assert.Equal("projects", rows[0].Project);
    }

    [Fact]
    public void Parser_reads_the_agent_task()
    {
        Assert.True(HookEventParser.TryParse("{\"event\":\"PreToolUse\",\"session\":\"s\",\"agent_id\":\"a1\",\"agent_task\":\"Developer agent\"}",
            Fixtures.Now, out var e));
        Assert.Equal("Developer agent", e.AgentTask);
    }

    [Fact]
    public void Parser_reads_todos_and_spawn_type()
    {
        Assert.True(HookEventParser.TryParse(
            "{\"event\":\"PreToolUse\",\"session\":\"s\",\"tool\":\"Agent\",\"spawn_type\":\"Explore\",\"todos\":[{\"text\":\"Fetch\",\"status\":\"completed\"}]}",
            Fixtures.Now, out var e));
        Assert.Equal("Explore", e.SpawnType);
        Assert.Equal("Fetch", e.Todos.Single().Text);
        Assert.Equal("completed", e.Todos.Single().Status);
    }
}
