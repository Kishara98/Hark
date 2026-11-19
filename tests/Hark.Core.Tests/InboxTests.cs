namespace Hark.Core.Tests;

public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hark-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name, string text)
    {
        var p = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, text);
        return p;
    }

    public void Dispose() => Directory.Delete(Path, true);
}

public class InboxTests
{
    private static string Record(long ts, string ev = "Stop") => $"{{\"ts\":{ts},\"event\":\"{ev}\",\"session\":\"s\"}}";

    [Fact]
    public void Reads_and_deletes_json()
    {
        using var t = new TempDir();
        var f = t.File("1-1-a.json", Fixtures.Text("stop.json"));
        var events = new Inbox(t.Path, _ => { }).ReadNew(Fixtures.Now);
        var e = Assert.Single(events);
        Assert.Equal("Stop", e.Event);
        Assert.Equal(Fixtures.Now, e.ReceivedUtc);
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void Skips_tmp_files()
    {
        using var t = new TempDir();
        var tmp = t.File("1-1-a.json.tmp", Record(1));
        Assert.Empty(new Inbox(t.Path, _ => { }).ReadNew(Fixtures.Now));
        Assert.True(File.Exists(tmp));
    }

    [Fact]
    public void Bad_json_moves_to_bad_and_rest_still_read()
    {
        using var t = new TempDir();
        t.File("1-1-bad.json", "{oops");
        t.File("2-1-good.json", Record(2));
        var logged = new List<string>();
        var events = new Inbox(t.Path, logged.Add).ReadNew(Fixtures.Now);
        Assert.Single(events);
        Assert.True(File.Exists(Path.Combine(t.Path, "bad", "1-1-bad.json")));
        Assert.Contains(logged, l => l.Contains("1-1-bad.json"));
    }

    [Fact]
    public void Returns_sorted_by_ts()
    {
        using var t = new TempDir();
        t.File("a.json", Record(300));
        t.File("b.json", Record(100));
        t.File("c.json", Record(200));
        Assert.Equal(new long[] { 100, 200, 300 }, new Inbox(t.Path, _ => { }).ReadNew(Fixtures.Now).Select(e => e.Ts));
    }

    [Fact]
    public void Missing_dir_returns_empty()
    {
        Assert.Empty(new Inbox(Path.Combine(Path.GetTempPath(), "hark-missing-" + Guid.NewGuid().ToString("N")), _ => { }).ReadNew(Fixtures.Now));
    }

    [Fact]
    public void Purge_removes_old_bad_and_tmp_files()
    {
        using var t = new TempDir();
        var oldBad = t.File("bad/old.json", "x");
        var newBad = t.File("bad/new.json", "x");
        var oldTmp = t.File("old.json.tmp", "x");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(oldBad, now.AddDays(-2));
        File.SetLastWriteTimeUtc(oldTmp, now.AddDays(-2));
        new Inbox(t.Path, _ => { }).PurgeOld(now, TimeSpan.FromDays(1));
        Assert.False(File.Exists(oldBad));
        Assert.False(File.Exists(oldTmp));
        Assert.True(File.Exists(newBad));
    }

    [Fact]
    public void Touch_updates_mtime_and_remove_deletes()
    {
        using var t = new TempDir();
        Heartbeat.Touch(t.Path);
        var hb = Path.Combine(t.Path, "heartbeat");
        Assert.True(File.Exists(hb));
        File.SetLastWriteTimeUtc(hb, DateTime.UtcNow.AddHours(-1));
        Heartbeat.Touch(t.Path);
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(hb) < TimeSpan.FromMinutes(1));
        Heartbeat.Remove(t.Path);
        Assert.False(File.Exists(hb));
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Defaults_when_file_missing()
    {
        using var t = new TempDir();
        var s = new SettingsStore(Path.Combine(t.Path, "settings.json")).Load();
        Assert.Equal(5, s.SnoozeMinutes);
        Assert.Equal("Ctrl+Alt+H", s.OpenHotkey);
        Assert.False(s.Paused);
    }

    [Fact]
    public void Settings_round_trip()
    {
        using var t = new TempDir();
        var store = new SettingsStore(Path.Combine(t.Path, "sub", "settings.json"));
        store.Save(new AppSettings { SnoozeMinutes = 1, OpenHotkey = "Ctrl+Alt+H", Paused = true });
        var s = store.Load();
        Assert.Equal(1, s.SnoozeMinutes);
        Assert.Equal("Ctrl+Alt+H", s.OpenHotkey);
        Assert.True(s.Paused);
        Assert.Contains("\"openHotkey\": \"Ctrl+Alt+H\"", File.ReadAllText(Path.Combine(t.Path, "sub", "settings.json")));
    }

    [Fact]
    public void Bad_settings_file_falls_back_to_defaults()
    {
        using var t = new TempDir();
        var f = t.File("settings.json", "{bad");
        Assert.Equal(5, new SettingsStore(f).Load().SnoozeMinutes);
    }

    [Fact]
    public void Snooze_minutes_below_one_uses_default()
    {
        using var t = new TempDir();
        var f = t.File("settings.json", "{\"snoozeMinutes\": 0}");
        Assert.Equal(5, new SettingsStore(f).Load().SnoozeMinutes);
    }
}
