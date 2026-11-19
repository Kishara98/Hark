namespace Hark.Core.Tests;

public class LogFileTests
{
    [Fact]
    public void Appends_lines()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "logs", "app.log");
        LogFile.Append(path, "one", 1000);
        LogFile.Append(path, "two", 1000);
        Assert.Equal(new[] { "one", "two" }, File.ReadAllLines(path));
    }

    [Fact]
    public void Rolls_over_when_the_limit_is_reached()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "app.log");
        LogFile.Append(path, new string('a', 60), 100);
        LogFile.Append(path, new string('b', 60), 100);
        Assert.Equal(new[] { new string('b', 60) }, File.ReadAllLines(path));
        Assert.Equal(new[] { new string('a', 60) }, File.ReadAllLines(path + ".1"));
    }

    [Fact]
    public void Keeps_only_one_old_file()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "app.log");
        LogFile.Append(path, new string('a', 60), 100);
        LogFile.Append(path, new string('b', 60), 100);
        LogFile.Append(path, new string('c', 60), 100);
        Assert.Equal(new[] { new string('c', 60) }, File.ReadAllLines(path));
        Assert.Equal(new[] { new string('b', 60) }, File.ReadAllLines(path + ".1"));
        Assert.Equal(2, Directory.GetFiles(t.Path).Length);
    }

    [Fact]
    public void Card_log_line_never_has_the_card_text()
    {
        var s = new WatchState();
        s.Apply(Fixtures.Event("permission_request.json", 100));
        var card = ViewPlanner.AllCards(s).Single();
        var line = LogFile.CardLine(card);
        Assert.Equal("card: Permission | projects · Claude CLI", line);
        Assert.DoesNotContain("note.txt", line);
    }
}
