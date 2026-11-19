using System;
using System.IO;

namespace Hark.App
{
    internal static class AppPaths
    {
        /// <summary>%LOCALAPPDATA%\Hark: inbox, heartbeat and logs. The hook recorder writes here from WSL.</summary>
        public static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hark");

        public static string InboxDir => Path.Combine(DataDir, "inbox");

        /// <summary>Typical step counts per agent type, learned from finished agents.</summary>
        public static string StatsFile => Path.Combine(DataDir, "agent-stats.json");

        public static string LogFile
        {
            get
            {
                var folder = Path.Combine(DataDir, "logs");
                Directory.CreateDirectory(folder);
                return Path.Combine(folder, "app.log");
            }
        }

        public static string SettingsFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hark", "settings.json");

        public static string FontsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts") + Path.DirectorySeparatorChar;
    }
}
