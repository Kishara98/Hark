using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Hark.Core;

namespace Hark.App
{
    /// <summary>Lists the visible top level windows and brings one to the front.</summary>
    internal static class WindowFocuser
    {
        public static IReadOnlyList<WindowInfo> List()
        {
            var result = new List<WindowInfo>();
            var names = new Dictionary<uint, string>();
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }
                var length = NativeMethods.GetWindowTextLength(hwnd);
                if (length == 0)
                {
                    return true;
                }
                var title = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hwnd, title, title.Capacity);
                NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (!names.TryGetValue(pid, out var name))
                {
                    name = ProcessName(pid);
                    names[pid] = name;
                }
                result.Add(new WindowInfo { Handle = hwnd.ToInt64(), ProcessName = name, Title = title.ToString() });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public static bool Focus(WindowInfo window)
        {
            var hwnd = new IntPtr(window.Handle);
            if (NativeMethods.IsIconic(hwnd))
            {
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            }
            var front = NativeMethods.GetForegroundWindow();
            if (front == hwnd || NativeMethods.SetForegroundWindow(hwnd))
            {
                return true;
            }
            // Windows can refuse a focus change; joining the front window's input queue is the usual way round.
            var frontThread = NativeMethods.GetWindowThreadProcessId(front, out _);
            var ownThread = NativeMethods.GetCurrentThreadId();
            var attached = frontThread != ownThread && NativeMethods.AttachThreadInput(ownThread, frontThread, true);
            try
            {
                NativeMethods.BringWindowToTop(hwnd);
                return NativeMethods.SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(ownThread, frontThread, false);
                }
            }
        }

        private static string ProcessName(uint pid)
        {
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName;
                }
            }
            catch (ArgumentException)
            {
                return "";
            }
            catch (InvalidOperationException)
            {
                return "";
            }
        }
    }
}
