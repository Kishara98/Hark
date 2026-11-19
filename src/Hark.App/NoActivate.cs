using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Hark.App
{
    /// <summary>Makes a window that never takes keyboard focus and stays out of Alt+Tab.</summary>
    internal static class NoActivate
    {
        private const int GwlExStyle = -20;
        private const long WsExNoActivate = 0x08000000;
        private const long WsExToolWindow = 0x00000080;

        public static void Apply(Window window)
        {
            window.ShowActivated = false;
            window.SourceInitialized += (s, e) => Set(window, WsExNoActivate | WsExToolWindow, true);
        }

        /// <summary>Lets the window take the keyboard (to type an own answer).</summary>
        public static void Allow(Window window) => Set(window, WsExNoActivate, false);

        /// <summary>Back to never taking the keyboard.</summary>
        public static void Restore(Window window) => Set(window, WsExNoActivate, true);

        private static void Set(Window window, long bits, bool on)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(on ? style | bits : style & ~bits));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
