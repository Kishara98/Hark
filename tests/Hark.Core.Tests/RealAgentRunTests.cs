namespace Hark.Core.Tests;

/// <summary>A real background Explore agent run captured from VS Code on 2026-11-16 (17 events, file names sort by ts).</summary>
public class RealAgentRunTests
{
    private static List<HookEvent> Events()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "fixtures", "agent_run");
        return Directory.GetFiles(dir, "*.json").OrderBy(f => f)
            .Select(f => { Assert.True(HookEventParser.TryParse(File.ReadAllText(f), Fixtures.Now, out var e)); return e; })
            .ToList();
    }

    [Fact]
    public void Pill_shows_the_agent_while_it_runs()
    {
        var s = new WatchState();
        var seen = new List<string>();
        foreach (var e in Events())
        {
            s.Apply(e);
            seen.Add($"{e.Event}:{ViewPlanner.Pill(s).AgentsText}");
        }
        Assert.Contains("SubagentStart:1 agent running", seen);
        Assert.Contains("PreToolUse:1 agent running", seen);
        Assert.Contains("SubagentStop:", seen);
    }

    [Fact]
    public void Pill_shows_the_agent_when_read_in_one_batch_per_second()
    {
        var s = new WatchState();
        var events = Events();
        var start = events.First(e => e.Event == "SubagentStart").Ts;
        s.ApplyBatch(events.Where(e => e.Ts <= start + 1000));
        Assert.True(ViewPlanner.Pill(s).Visible);
        Assert.Equal("1 agent running", ViewPlanner.Pill(s).AgentsText);
    }
}
