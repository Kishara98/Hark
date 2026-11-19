using System;

namespace Hark.Core
{
    /// <summary>The Claude Code VS Code extension opens a given chat from vscode://anthropic.claude-code/open?session=id.</summary>
    public static class ChatLink
    {
        private const string Prefix = "vscode://anthropic.claude-code/open?session=";

        /// <summary>The link for a VS Code session, or null (CLI session, or not a real session id).</summary>
        public static string For(SourceKind source, string sessionId)
        {
            if (source != SourceKind.VsCode || !Guid.TryParseExact(sessionId ?? "", "D", out _))
            {
                return null;
            }
            return Prefix + sessionId;
        }
    }
}
