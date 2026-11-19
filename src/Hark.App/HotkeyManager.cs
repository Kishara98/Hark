using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Hark.Core;

namespace Hark.App
{
    /// <summary>Global hotkeys through RegisterHotKey on a hidden message only window.</summary>
    internal sealed class HotkeyManager : NativeWindow, IDisposable
    {
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private readonly List<int> _registered = new List<int>();

        public HotkeyManager()
        {
            CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        }

        /// <summary>Raised with the id passed to Register.</summary>
        public event EventHandler<int> Pressed;

        /// <summary>False when another app already uses the key combination.</summary>
        public bool Register(int id, Hotkey hotkey)
        {
            var ok = NativeMethods.RegisterHotKey(Handle, id, (uint)hotkey.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)hotkey.VirtualKey);
            if (ok)
            {
                _registered.Add(id);
            }
            return ok;
        }

        public void Unregister(int id)
        {
            if (_registered.Remove(id))
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
        }

        public void Dispose()
        {
            foreach (var id in _registered)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
            _registered.Clear();
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                Pressed?.Invoke(this, m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }
    }
}
