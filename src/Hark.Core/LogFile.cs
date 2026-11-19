using System;
using System.IO;
using System.Text;

namespace Hark.Core
{
    /// <summary>A size capped log: past the limit the file becomes app.log.1 (one old file kept).</summary>
    public static class LogFile
    {
        public const long DefaultMaxBytes = 1024 * 1024;

        public static void Append(string path, string line, long maxBytes)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var text = line + Environment.NewLine;
            var file = new FileInfo(path);
            if (file.Exists && file.Length + Encoding.UTF8.GetByteCount(text) > maxBytes)
            {
                var old = path + ".1";
                if (File.Exists(old))
                {
                    File.Delete(old);
                }
                File.Move(path, old);
            }
            File.AppendAllText(path, text);
        }

        /// <summary>The log line for a new card: type and session only, never the text (it can hold a full command).</summary>
        public static string CardLine(Card card) => $"card: {card.Chip} | {card.Meta}";
    }
}
