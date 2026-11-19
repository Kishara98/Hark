using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Hark.App
{
    /// <summary>Tray icon (amber dot while Claude waits) with "Pause popups", "Reset pill position" and "Quit".</summary>
    internal sealed class TrayController : IDisposable
    {
        private readonly NotifyIcon _icon = new NotifyIcon();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly Icon _normal;
        private readonly Icon _waiting;
        private bool _isWaiting;

        public TrayController(bool paused)
        {
            using (var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
            {
                _normal = new Icon(exeIcon, SystemInformation.SmallIconSize);
            }
            _waiting = WithDot(_normal);

            var pause = new ToolStripMenuItem("Pause popups") { CheckOnClick = true, Checked = paused };
            pause.CheckedChanged += (s, e) => PauseChanged?.Invoke(pause.Checked);
            var reset = new ToolStripMenuItem("Reset pill position");
            reset.Click += (s, e) => ResetPill?.Invoke();
            var quit = new ToolStripMenuItem("Quit");
            quit.Click += (s, e) => Quit?.Invoke();
            _menu.Items.AddRange(new ToolStripItem[] { pause, reset, new ToolStripSeparator(), quit });

            _icon.ContextMenuStrip = _menu;
            _icon.Icon = _normal;
            _icon.Text = "Hark";
            _icon.Visible = true;
        }

        public event Action<bool> PauseChanged;

        public event Action Quit;

        /// <summary>Puts the pill back at the top centre.</summary>
        public event Action ResetPill;

        public void SetWaiting(bool waiting)
        {
            if (waiting == _isWaiting)
            {
                return;
            }
            _isWaiting = waiting;
            _icon.Icon = waiting ? _waiting : _normal;
            _icon.Text = waiting ? "Hark: Claude is waiting for you" : "Hark";
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            DestroyIcon(_waiting.Handle);
            _normal.Dispose();
        }

        private static Icon WithDot(Icon icon)
        {
            using (var bitmap = icon.ToBitmap())
            {
                using (var g = Graphics.FromImage(bitmap))
                using (var ring = new SolidBrush(Color.White))
                using (var dot = new SolidBrush(Color.FromArgb(0xf5, 0x9e, 0x0b)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var size = bitmap.Width * 0.56f;
                    var x = bitmap.Width - size;
                    g.FillEllipse(ring, x, 0, size, size);
                    var inset = Math.Max(1f, bitmap.Width / 16f);
                    g.FillEllipse(dot, x + inset, inset, size - 2 * inset, size - 2 * inset);
                }
                return Icon.FromHandle(bitmap.GetHicon());
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr handle);
    }
}
