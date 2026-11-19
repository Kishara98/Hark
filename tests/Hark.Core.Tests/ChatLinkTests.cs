namespace Hark.Core.Tests;

public class ChatLinkTests
{
    [Fact]
    public void Vscode_session_gets_the_extension_link()
    {
        Assert.Equal(
            "vscode://anthropic.claude-code/open?session=5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9",
            ChatLink.For(SourceKind.VsCode, "5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9"));
    }

    [Fact]
    public void Cli_session_has_no_link()
    {
        Assert.Null(ChatLink.For(SourceKind.Cli, "5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("s-webshop")]
    [InlineData("5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9&prompt=rm")]
    public void Only_real_session_ids_get_a_link(string id)
    {
        Assert.Null(ChatLink.For(SourceKind.VsCode, id));
    }
}
