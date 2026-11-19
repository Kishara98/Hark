using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hark.Core
{
    /// <summary>Reads the event files the hook recorder drops into the inbox folder.</summary>
    public sealed class Inbox
    {
        private readonly string _dir;
        private readonly Action<string> _log;

        public Inbox(string dir, Action<string> log)
        {
            _dir = dir;
            _log = log ?? (_ => { });
        }

        public IReadOnlyList<HookEvent> ReadNew(DateTime receivedUtc)
        {
            if (!Directory.Exists(_dir))
            {
                return Array.Empty<HookEvent>();
            }
            var events = new List<HookEvent>();
            foreach (var path in Directory.GetFiles(_dir).Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                string text;
                try
                {
                    text = File.ReadAllText(path);
                }
                catch (IOException)
                {
                    continue; // still being written or locked: try again next time
                }
                if (HookEventParser.TryParse(text, receivedUtc, out var e))
                {
                    events.Add(e);
                    TryDelete(path);
                }
                else
                {
                    MoveToBad(path);
                }
            }
            return events.OrderBy(e => e.Ts).ToList();
        }

        public void PurgeOld(DateTime nowUtc, TimeSpan maxAge)
        {
            var bad = Path.Combine(_dir, "bad");
            var files = (Directory.Exists(bad) ? Directory.GetFiles(bad) : Array.Empty<string>())
                .Concat(Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*.tmp") : Array.Empty<string>());
            foreach (var path in files)
            {
                if (nowUtc - File.GetLastWriteTimeUtc(path) > maxAge)
                {
                    TryDelete(path);
                }
            }
        }

        private void MoveToBad(string path)
        {
            var name = Path.GetFileName(path);
            try
            {
                var bad = Path.Combine(_dir, "bad");
                Directory.CreateDirectory(bad);
                var target = Path.Combine(bad, name);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                File.Move(path, target);
                _log($"bad event file moved to bad: {name}");
            }
            catch (IOException ex)
            {
                _log($"could not move bad event file {name}: {ex.Message}");
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                _log($"could not delete {Path.GetFileName(path)}: {ex.Message}");
            }
        }
    }

    /// <summary>The file the recorder checks to know Hark is running.</summary>
    public static class Heartbeat
    {
        public const string FileName = "heartbeat";

        public static void Touch(string dataDir)
        {
            Directory.CreateDirectory(dataDir);
            var path = Path.Combine(dataDir, FileName);
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }

        public static void Remove(string dataDir)
        {
            try
            {
                File.Delete(Path.Combine(dataDir, FileName));
            }
            catch (IOException)
            {
            }
        }
    }
}
