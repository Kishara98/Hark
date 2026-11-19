using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Hark.Core;

namespace Hark.Hook
{
    /// <summary>
    /// HarkHook.exe: the Claude Code hooks for Hark when Claude Code runs natively on Windows (in WSL the Python hooks do this).
    ///   HarkHook.exe record|answer|agent --data-dir DIR   run by Claude Code for each event
    ///   HarkHook.exe install|uninstall                    add or remove the hooks in the Claude Code settings of this user
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var verb = args.Length > 0 ? args[0] : "";
            var rest = args.Skip(1).ToArray();
            // Claude Code sends UTF-8; the console code page would garble anything outside ASCII.
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            var env = Environment();
            switch (verb)
            {
                case "record":
                    return HookRecorder.Run(rest, stdin, env, () => DateTime.UtcNow);
                case "answer":
                    return AnswerHook.Run(rest, stdin, env, stdout, () => DateTime.UtcNow, Thread.Sleep);
                case "agent":
                    return AgentHook.Run(rest, stdin, env, stdout, () => DateTime.UtcNow);
                case "install":
                    return Install();
                case "uninstall":
                    return Uninstall();
                default:
                    Console.Error.WriteLine("Usage: HarkHook.exe install | uninstall | record|answer|agent --data-dir <folder>");
                    return 2;
            }
        }

        private static string SettingsPath()
        {
            // Claude Code keeps its user settings in CLAUDE_CONFIG_DIR when that is set, else in %USERPROFILE%\.claude.
            var dir = System.Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".claude");
            }
            return Path.Combine(dir, "settings.json");
        }

        private static int Install()
        {
            try
            {
                var exe = typeof(Program).Assembly.Location;
                var dataDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Hark");
                Directory.CreateDirectory(Path.Combine(dataDir, "inbox"));
                var settings = SettingsPath();
                var backup = HookInstaller.Install(settings,
                    HookInstaller.Command(exe, "record", dataDir),
                    HookInstaller.Command(exe, "answer", dataDir),
                    HookInstaller.Command(exe, "agent", dataDir));
                if (backup != null)
                {
                    Console.WriteLine("backup: " + backup);
                }
                Console.WriteLine($"installed {HookInstaller.Events.Count} event hooks, the answer hook and the agent hook in {settings}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not install the Hark hooks: " + ex.Message);
                return 1;
            }
        }

        private static int Uninstall()
        {
            try
            {
                var settings = SettingsPath();
                var backup = HookInstaller.Uninstall(settings);
                Console.WriteLine(backup == null ? "no Claude Code settings found, nothing to remove" : $"removed the Hark hooks from {settings} (backup: {backup})");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Could not remove the Hark hooks: " + ex.Message);
                return 1;
            }
        }

        private static IReadOnlyDictionary<string, string> Environment()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                result[(string)entry.Key] = entry.Value as string;
            }
            return result;
        }
    }
}
