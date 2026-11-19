using System;
using System.Globalization;
using System.IO;
using Hark.Core;

namespace Hark.App
{
    /// <summary>One line per event in %LOCALAPPDATA%\Hark\logs\app.log. Never throws.</summary>
    internal static class AppLog
    {
        private static readonly object Gate = new object();

        public static void Write(string message)
        {
            try
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine;
                lock (Gate)
                {
                    LogFile.Append(AppPaths.LogFile, line.TrimEnd(), LogFile.DefaultMaxBytes);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
