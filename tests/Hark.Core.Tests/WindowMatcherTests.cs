namespace Hark.Core.Tests;

public class WindowMatcherTests
{
    private static WindowInfo W(long h, string process, string title) => new WindowInfo { Handle = h, ProcessName = process, Title = title };

    private static readonly WindowInfo CodeNotes = W(1, "Code", "report.py - Notes [WSL: Ubuntu] - Visual Studio Code");
    private static readonly WindowInfo CodeAfp = W(2, "Code", "SplitConfig.java - WebShop [WSL: Ubuntu] - Visual Studio Code");
    private static readonly WindowInfo TermNotes = W(3, "WindowsTerminal", "dev@laptop: ~/projects/Notes");
    private static readonly WindowInfo TermAfp = W(4, "WindowsTerminal", "dev@laptop: ~/projects/WebShop");
    private static readonly WindowInfo Chrome = W(5, "chrome", "WebShop - Jira - Google Chrome");

    [Fact]
    public void Picks_vscode_window_by_project()
    {
        Assert.Same(CodeAfp, WindowMatcher.Pick(new[] { Chrome, CodeNotes, CodeAfp }, SourceKind.VsCode, "WebShop"));
    }

    [Fact]
    public void Project_match_ignores_case()
    {
        Assert.Same(CodeAfp, WindowMatcher.Pick(new[] { CodeNotes, CodeAfp }, SourceKind.VsCode, "webshop"));
    }

    [Fact]
    public void Falls_back_to_any_vscode()
    {
        Assert.Same(CodeNotes, WindowMatcher.Pick(new[] { Chrome, CodeNotes, CodeAfp }, SourceKind.VsCode, "Hark"));
    }

    [Fact]
    public void Cli_prefers_terminal_with_project()
    {
        Assert.Same(TermAfp, WindowMatcher.Pick(new[] { CodeAfp, TermNotes, TermAfp }, SourceKind.Cli, "WebShop"));
    }

    [Fact]
    public void Cli_prefers_code_with_project_over_unrelated_terminal()
    {
        Assert.Same(CodeAfp, WindowMatcher.Pick(new[] { TermNotes, CodeAfp }, SourceKind.Cli, "WebShop"));
    }

    [Fact]
    public void Cli_falls_back_to_any_terminal_then_any_code()
    {
        Assert.Same(TermNotes, WindowMatcher.Pick(new[] { CodeNotes, TermNotes }, SourceKind.Cli, "Hark"));
        Assert.Same(CodeNotes, WindowMatcher.Pick(new[] { Chrome, CodeNotes }, SourceKind.Cli, "Hark"));
    }

    [Fact]
    public void Returns_null_when_nothing()
    {
        Assert.Null(WindowMatcher.Pick(new[] { Chrome }, SourceKind.VsCode, "WebShop"));
        Assert.Null(WindowMatcher.Pick(Array.Empty<WindowInfo>(), SourceKind.Cli, "WebShop"));
    }

    [Fact]
    public void Default_open_hotkey_parses()
    {
        Assert.True(Hotkey.TryParse(AppSettings.DefaultOpenHotkey, out var key));
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, key.Modifiers);
        Assert.Equal('H', key.VirtualKey);
    }
}
