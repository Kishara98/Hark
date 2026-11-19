using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Hark.Core;

namespace Hark.App
{
    /// <summary>
    /// The black pill at the top centre; with agents running it opens into the agent panel (board H).
    /// It can be dragged anywhere and stays on screen; a double click or the tray item puts it back at the top.
    /// </summary>
    internal sealed class PillWindow : Window
    {
        private const double PanelWidth = 560;
        private const string ChevronDown = "m6 9 6 6 6-6";
        private const string ChevronUp = "m18 15-6-6-6 6";
        private const string CheckIcon = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0 M8.5 12l2.5 2.5 4.5-5";
        private const string OpenCircle = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0";

        private readonly RotateTransform _rotate = new RotateTransform();
        private readonly FrameworkElement _spinner;
        private bool _spinning;
        private string _shownKey;
        private string _target;
        private bool _expanded;
        private string _openAgent;
        private PillModel _pill;
        private IReadOnlyList<AgentRow> _agents = Array.Empty<AgentRow>();
        private readonly DragGesture _drag = new DragGesture(Math.Max(System.Windows.Forms.SystemInformation.DragSize.Width, 4));
        private readonly DoubleClick _clicks = new DoubleClick(TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime),
            Math.Max(System.Windows.Forms.SystemInformation.DoubleClickSize.Width, 4));
        private PillAnchor _anchor;
        private bool _expandedAtPress;
        private bool _pressOnPill;

        public event Action<string> OpenRequested;

        public event Action CloseRequested;

        public PillWindow()
        {
            Title = "Hark pill";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Theme.Sans;
            _spinner = Theme.Icon(Theme.SpinnerIcon, 14, Theme.Spinner, 2);
            _spinner.RenderTransform = _rotate;
            _spinner.RenderTransformOrigin = new Point(0.5, 0.5);
            _spinner.Margin = new Thickness(0, 0, 6, 0);
            NoActivate.Apply(this);
            SizeChanged += (s, e) => Place();
            PreviewMouseLeftButtonDown += OnPress;
            PreviewMouseMove += OnMouseMove;
            PreviewMouseLeftButtonUp += OnRelease;
            LostMouseCapture += (s, e) => FinishDrag();
        }

        /// <summary>Puts the pill back at the top centre.</summary>
        public void ResetPosition()
        {
            _anchor = null;
            Place();
        }

        public void Update(PillModel pill, bool closed, IReadOnlyList<AgentRow> agents)
        {
            _pill = pill;
            _agents = agents;
            if (agents.Count == 0)
            {
                _expanded = false;
            }
            if (_openAgent != null && agents.All(a => a.AgentId != _openAgent))
            {
                _openAgent = null;
            }
            if (!pill.Visible || closed)
            {
                _shownKey = null;
                Spin(false);
                if (IsVisible)
                {
                    Hide();
                }
                return;
            }
            _target = pill.TargetSessionId;
            var key = $"{pill.ShowOpen}|{pill.Text}|{pill.AgentsText}|{pill.TaskText}|{_expanded}|{_openAgent}";
            if (_expanded)
            {
                key += "|" + string.Join(";", agents.Select(a =>
                    $"{a.AgentId},{a.Elapsed},{a.Progress.Label},{string.Join(",", a.Todos.Select(t => t.Status + t.Text))},{string.Join(",", a.RecentSteps)}"));
            }
            if (key != _shownKey)
            {
                Content = Build();
                _shownKey = key;
            }
            Spin(Progress(pill).Length > 0);
            if (!IsVisible)
            {
                Show();
            }
            Place();
        }

        private void Redraw()
        {
            _shownKey = null;
            Update(_pill, false, _agents);
        }

        private void Spin(bool on)
        {
            if (on == _spinning)
            {
                return;
            }
            _spinning = on;
            // An animation started with BeginAnimation runs until it is removed, even on a hidden window.
            _rotate.BeginAnimation(RotateTransform.AngleProperty,
                on ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever } : null);
        }

        private UIElement Build()
        {
            (_spinner.Parent as Panel)?.Children.Remove(_spinner);
            var header = PillRow();
            if (!_expanded)
            {
                var pill = new Border
                {
                    Margin = new Thickness(Theme.ShadowRoom),
                    Height = 44,
                    CornerRadius = new CornerRadius(22),
                    Background = Theme.Ink,
                    Padding = new Thickness(16, 0, 8, 0),
                    Effect = Theme.Shadow(),
                    Child = header,
                };
                if (_agents.Count > 0)
                {
                    // A click opens the panel (see OnRelease); a press that moves drags the pill instead.
                    pill.Cursor = Cursors.Hand;
                }
                return pill;
            }
            var panel = new StackPanel();
            var top = new Border { Height = 40, Padding = new Thickness(10, 0, 2, 0), Child = header, Background = Brushes.Transparent };
            panel.Children.Add(top);
            panel.Children.Add(new Border { Height = 1, Background = Theme.Hex(0x27272a), Margin = new Thickness(8, 0, 8, 4) });
            foreach (var agent in _agents)
            {
                panel.Children.Add(AgentBlock(agent));
            }
            var note = Theme.Text("\"2 of 5 done\" comes from the to-do list Claude keeps. \"~60%\" is estimated from past runs of that agent type. "
                + "Drag the panel to move it; a double click puts it back at the top.",
                11, Theme.Muted, FontWeights.Normal);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(14, 6, 14, 8);
            panel.Children.Add(note);
            return new Border
            {
                Margin = new Thickness(Theme.ShadowRoom),
                Width = PanelWidth,
                CornerRadius = new CornerRadius(18),
                Background = Theme.Ink,
                Padding = new Thickness(6),
                Effect = Theme.Shadow(),
                Child = panel,
            };
        }

        private UIElement PillRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var p = _pill;
            if (p.ShowOpen)
            {
                var dot = new Grid { Width = 16, Height = 16, Margin = new Thickness(0, 0, 12, 0) };
                dot.Children.Add(new Ellipse { Width = 16, Height = 16, Fill = Theme.WaitRing });
                dot.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Theme.WaitDot });
                row.Children.Add(dot);
                row.Children.Add(Theme.Text(p.Text, 13, Theme.White, FontWeights.Medium));
            }
            var progress = Progress(p);
            if (progress.Length > 0)
            {
                if (p.ShowOpen)
                {
                    row.Children.Add(new Rectangle { Width = 1, Height = 16, Fill = Theme.PillDivider, Margin = new Thickness(12, 0, 12, 0) });
                }
                row.Children.Add(_spinner);
                row.Children.Add(Theme.Text(progress, 13, p.ShowOpen ? Theme.PillMuted : Theme.White, FontWeights.Normal));
            }
            if (p.ShowOpen)
            {
                var open = Theme.Button("Open", Theme.Text("Open", 12, Theme.Ink, FontWeights.SemiBold), Theme.White, null, Theme.Ink, 32, 16,
                    new Thickness(14, 0, 14, 0), () => OpenRequested?.Invoke(_target));
                open.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(open);
            }
            if (_agents.Count > 0)
            {
                var chevron = Theme.Button(_expanded ? "Hide progress" : "Show progress", Theme.Icon(_expanded ? ChevronUp : ChevronDown, 16, Theme.PillMuted),
                    null, null, Theme.PillMuted, 28, 14, new Thickness(0), Toggle);
                chevron.Width = 28;
                chevron.Margin = new Thickness(p.ShowOpen ? 4 : 8, 0, 0, 0);
                row.Children.Add(chevron);
            }
            var close = Theme.Button("Close", Theme.Icon(Theme.CloseIcon, 14, Theme.PillMuted), null, null, Theme.PillMuted, 28, 14,
                new Thickness(0), () => CloseRequested?.Invoke());
            close.Width = 28;
            close.Margin = new Thickness(_agents.Count > 0 ? 0 : (p.ShowOpen ? 4 : 10), 0, 0, 0);
            row.Children.Add(close);
            if (!_expanded)
            {
                return row;
            }
            // In the panel the header spans the width: text left, buttons right.
            var grid = new DockPanel { LastChildFill = false };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var b in row.Children.OfType<Button>().ToList())
            {
                row.Children.Remove(b);
                buttons.Children.Add(b);
            }
            DockPanel.SetDock(buttons, Dock.Right);
            grid.Children.Add(buttons);
            grid.Children.Add(row);
            return grid;
        }

        /// <summary>Running tasks and agents in one line: "projects · 2 of 5 done · 2 agents running".</summary>
        private static string Progress(PillModel p) =>
            string.Join(" · ", new[] { p.TaskText, p.AgentsText }.Where(t => !string.IsNullOrEmpty(t)));

        private static Brush Accent(AgentRow row) => row.IsMainTask ? Theme.Spinner : Pets.Color(row.PetIndex);

        private static FrameworkElement Avatar(AgentRow row) => row.IsMainTask
            ? new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(19),
                Background = Theme.Hex(0x60a5fa, 0x29),
                Child = Theme.Icon(Theme.EyeIcon, 22, Theme.Spinner, 1.6),
            }
            : Pets.Avatar(row.PetIndex, 38);

        private void Toggle()
        {
            _expanded = !_expanded && _agents.Count > 0;
            Redraw();
        }

        private UIElement AgentBlock(AgentRow agent)
        {
            var open = agent.AgentId == _openAgent;
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var avatar = Avatar(agent);
            Grid.SetColumn(avatar, 0);
            line.Children.Add(avatar);

            var middle = new StackPanel { Margin = new Thickness(12, 0, 12, 0) };
            var top = new DockPanel { LastChildFill = false };
            var elapsed = Theme.Text(agent.Elapsed, 12, Theme.PillMuted, FontWeights.Normal);
            DockPanel.SetDock(elapsed, Dock.Right);
            top.Children.Add(elapsed);
            top.Children.Add(Theme.Text(agent.PetName, 13, Theme.White, FontWeights.SemiBold));
            top.Children.Add(new Border
            {
                BorderBrush = Theme.PillDivider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Child = Theme.Text(agent.AgentType, 11, Theme.PillMuted, FontWeights.Normal),
            });
            middle.Children.Add(top);
            var task = Theme.Text(agent.Task, 13, Theme.Hex(0xd4d4d8), FontWeights.Normal);
            task.TextTrimming = TextTrimming.CharacterEllipsis;
            task.Margin = new Thickness(0, 4, 0, 0);
            middle.Children.Add(task);
            middle.Children.Add(Bar(agent));
            Grid.SetColumn(middle, 1);
            line.Children.Add(middle);

            var chevron = Theme.Icon(open ? ChevronUp : ChevronDown, 16, open ? Theme.PillMuted : Theme.Muted);
            Grid.SetColumn(chevron, 2);
            line.Children.Add(chevron);

            var button = Theme.Button($"{agent.PetName}, {agent.Task}", line, null, null, Theme.White, double.NaN, 12, new Thickness(10),
                () =>
                {
                    _openAgent = open ? null : agent.AgentId;
                    Redraw();
                });
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            if (!open)
            {
                return button;
            }
            var block = new StackPanel();
            block.Children.Add(button);
            block.Children.Add(Details(agent));
            return new Border { CornerRadius = new CornerRadius(12), Background = Theme.Hex(0x141417), Margin = new Thickness(0, 2, 0, 2), Child = block };
        }

        private static UIElement Bar(AgentRow agent)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
            var fraction = Math.Max(0.02, Math.Min(1, agent.Progress.Fraction));
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction + 0.0001, GridUnitType.Star) });
            track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = Theme.Hex(0x27272a) });
            Grid.SetColumnSpan(track.Children[0], 2);
            var fill = new Border { CornerRadius = new CornerRadius(3), Background = Accent(agent) };
            Grid.SetColumn(fill, 0);
            track.Children.Add(fill);
            row.Children.Add(track);
            var label = Theme.Text(agent.Progress.Label, 12, agent.Progress.IsEstimate ? Theme.PillMuted : Theme.Hex(0xd4d4d8), FontWeights.Normal);
            label.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            return row;
        }

        private static UIElement Details(AgentRow agent)
        {
            var stack = new StackPanel { Margin = new Thickness(60, 0, 14, 12) };
            if (agent.Todos.Count > 0)
            {
                stack.Children.Add(Theme.Text("To-do list", 11, Theme.PillMuted, FontWeights.Medium));
                foreach (var todo in agent.Todos)
                {
                    var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                    var running = todo.Status == "in_progress";
                    var done = todo.Status == "completed";
                    var icon = running ? Theme.Icon(Theme.SpinnerIcon, 15, Accent(agent))
                        : Theme.Icon(done ? CheckIcon : OpenCircle, 15, done ? Accent(agent) : Theme.Hex(0x52525b));
                    icon.Margin = new Thickness(0, 0, 8, 0);
                    item.Children.Add(icon);
                    item.Children.Add(Theme.Text(todo.Text, 13, running ? Theme.White : done ? Theme.PillMuted : Theme.Muted,
                        running ? FontWeights.SemiBold : FontWeights.Normal));
                    stack.Children.Add(item);
                }
            }
            if (agent.RecentSteps.Count > 0)
            {
                var title = Theme.Text("Latest steps", 11, Theme.PillMuted, FontWeights.Medium);
                title.Margin = new Thickness(0, agent.Todos.Count > 0 ? 10 : 0, 0, 0);
                stack.Children.Add(title);
                var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                var steps = agent.RecentSteps.Skip(Math.Max(0, agent.RecentSteps.Count - 3)).ToList();
                for (var i = 0; i < steps.Count; i++)
                {
                    var last = i == steps.Count - 1;
                    var text = Theme.Text(steps[i], 11, last ? Theme.White : Theme.PillMuted, FontWeights.Normal, Theme.Mono);
                    text.TextTrimming = TextTrimming.CharacterEllipsis;
                    text.MaxWidth = 420;
                    chips.Children.Add(new Border
                    {
                        Background = last ? (agent.IsMainTask ? Theme.Hex(0x60a5fa, 0x29) : Pets.Tint(agent.PetIndex)) : Theme.Hex(0x1f1f24),
                        BorderBrush = last ? Accent(agent) : Theme.Hex(0x27272a),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 0, 6, 0),
                        Margin = new Thickness(0, 0, 4, 4),
                        Child = text,
                    });
                }
                stack.Children.Add(chips);
            }
            return stack;
        }

        private void OnPress(object sender, MouseButtonEventArgs e)
        {
            var mouse = MouseAt();
            var onButton = InButton(e.OriginalSource as DependencyObject);
            if (_clicks.Down(DateTime.UtcNow, mouse.X, mouse.Y, onButton))
            {
                // The first click may have opened the panel; a double click only moves the pill back.
                e.Handled = true;
                _expanded = _expandedAtPress && _agents.Count > 0;
                _anchor = null;
                Redraw();
                AppLog.Write("pill back at the top (double click)");
                return;
            }
            if (onButton)
            {
                return;
            }
            _expandedAtPress = _expanded;
            _pressOnPill = !_expanded && _agents.Count > 0;
            var box = WindowBox();
            _drag.Begin(mouse.X, mouse.Y, box.Left, box.Top);
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_drag.Active)
            {
                return;
            }
            var mouse = MouseAt();
            if (!_drag.Update(mouse.X, mouse.Y, out var left, out var top))
            {
                return;
            }
            _clicks.Forget();
            Mouse.OverrideCursor = Cursors.SizeAll;
            var box = WindowBox();
            var spot = PillPlacement.KeepOnScreen(left, top, box.Width, box.Height, WorkAreaAt(mouse.X, mouse.Y), Theme.ShadowRoom * Scale());
            MoveTo(spot.Left, spot.Top);
        }

        private void OnRelease(object sender, MouseButtonEventArgs e)
        {
            if (!_drag.Active)
            {
                return;
            }
            var dragged = _drag.End();
            Mouse.OverrideCursor = null;
            ReleaseMouseCapture();
            e.Handled = true;
            if (dragged)
            {
                KeepSpot();
            }
            else if (_pressOnPill && !_expanded && _agents.Count > 0)
            {
                Toggle();
            }
        }

        /// <summary>The mouse was taken away mid drag (the pill hid, another window grabbed it): keep where it got to.</summary>
        private void FinishDrag()
        {
            if (!_drag.Active)
            {
                return;
            }
            Mouse.OverrideCursor = null;
            if (_drag.End())
            {
                KeepSpot();
            }
        }

        private void KeepSpot()
        {
            var box = WindowBox();
            _anchor = PillPlacement.AnchorOf(box.Left, box.Top, box.Width);
            AppLog.Write($"pill moved to {box.Left:0},{box.Top:0}");
        }

        private bool InButton(DependencyObject element)
        {
            while (element != null && element != this)
            {
                if (element is ButtonBase)
                {
                    return true;
                }
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        /// <summary>Everything below is in screen pixels, so it works the same on every monitor and scale.</summary>
        private void Place()
        {
            if (_drag.Active || new WindowInteropHelper(this).Handle == IntPtr.Zero)
            {
                return;
            }
            var scale = Scale();
            var margin = Theme.ShadowRoom * scale;
            var work = _anchor == null ? Box(System.Windows.Forms.Screen.PrimaryScreen.WorkingArea) : WorkAreaAt(_anchor.CentreX, _anchor.Top + margin);
            var spot = PillPlacement.Place(_anchor, work, ActualWidth * scale, ActualHeight * scale, margin, 16 * scale);
            MoveTo(spot.Left, spot.Top);
        }

        private double Scale() => VisualTreeHelper.GetDpi(this).DpiScaleX;

        private static (double X, double Y) MouseAt()
        {
            NativeMethods.GetCursorPos(out var p);
            return (p.X, p.Y);
        }

        private ScreenBox WindowBox()
        {
            NativeMethods.GetWindowRect(new WindowInteropHelper(this).Handle, out var r);
            return new ScreenBox(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        private static ScreenBox WorkAreaAt(double x, double y) =>
            Box(System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)x, (int)y)).WorkingArea);

        private static ScreenBox Box(System.Drawing.Rectangle r) => new ScreenBox(r.Left, r.Top, r.Width, r.Height);

        private void MoveTo(double left, double top)
        {
            var x = (int)Math.Round(left);
            var y = (int)Math.Round(top);
            var now = WindowBox();
            if ((int)now.Left == x && (int)now.Top == y)
            {
                return;
            }
            NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, x, y, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }
}
