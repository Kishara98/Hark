using System;
using System.Collections.Generic;
using System.Linq;

namespace Hark.Core
{
    public sealed class WindowInfo
    {
        public long Handle { get; set; }
        public string ProcessName { get; set; }
        public string Title { get; set; }
    }

    /// <summary>Finds the window to bring forward for a session.</summary>
    public static class WindowMatcher
    {
        private const string Code = "Code";
        private const string Terminal = "WindowsTerminal";

        public static WindowInfo Pick(IReadOnlyList<WindowInfo> windows, SourceKind source, string project)
        {
            WindowInfo With(string process) => windows.FirstOrDefault(w => Is(w, process) && Has(w, project));
            WindowInfo Any(string process) => windows.FirstOrDefault(w => Is(w, process));

            if (source == SourceKind.VsCode)
            {
                return With(Code) ?? Any(Code);
            }
            // A CLI session runs in Windows Terminal or in a VS Code terminal: a project match wins first.
            return With(Terminal) ?? With(Code) ?? Any(Terminal) ?? Any(Code);
        }

        private static bool Is(WindowInfo w, string process) =>
            string.Equals(w.ProcessName, process, StringComparison.OrdinalIgnoreCase);

        private static bool Has(WindowInfo w, string project) =>
            !string.IsNullOrEmpty(project) && (w.Title ?? "").IndexOf(project, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
