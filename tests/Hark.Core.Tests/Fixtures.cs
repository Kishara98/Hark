namespace Hark.Core.Tests;

internal static class Fixtures
{
    public static readonly DateTime Now = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures", "events");

    public static IEnumerable<string> AllNames() =>
        Directory.GetFiles(Dir, "*.json").Select(Path.GetFileName).OrderBy(n => n)!;

    public static string Text(string name) => File.ReadAllText(Path.Combine(Dir, name));

    public static HookEvent Event(string name, long? ts = null, DateTime? received = null)
    {
        Assert.True(HookEventParser.TryParse(Text(name), received ?? Now, out var e), name);
        if (ts.HasValue)
        {
            e.Ts = ts.Value;
        }
        return e;
    }
}
