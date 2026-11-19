using System;

namespace Hark.Core
{
    /// <summary>A rectangle on the screen, in pixels.</summary>
    public struct ScreenBox
    {
        public ScreenBox(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public double Left { get; }

        public double Top { get; }

        public double Width { get; }

        public double Height { get; }

        public double Right => Left + Width;

        public double Bottom => Top + Height;
    }

    /// <summary>Where the user dropped the pill: its centre and its top, so it grows evenly when the panel opens.</summary>
    public sealed class PillAnchor
    {
        public PillAnchor(double centreX, double top)
        {
            CentreX = centreX;
            Top = top;
        }

        public double CentreX { get; }

        public double Top { get; }
    }

    /// <summary>Works out where the pill window goes. No anchor means the usual spot at the top centre.</summary>
    public static class PillPlacement
    {
        public static PillAnchor AnchorOf(double left, double top, double width) => new PillAnchor(left + width / 2, top);

        /// <param name="margin">See-through room around the visible pill; it may hang off the screen.</param>
        /// <param name="gap">Space between the top of the screen and the pill in the usual spot.</param>
        public static (double Left, double Top) Place(PillAnchor anchor, ScreenBox work, double width, double height, double margin, double gap)
        {
            if (anchor == null)
            {
                return (work.Left + (work.Width - width) / 2, work.Top + gap - margin);
            }
            return KeepOnScreen(anchor.CentreX - width / 2, anchor.Top, width, height, work, margin);
        }

        /// <summary>Moves the window just enough that the visible pill stays inside the work area.</summary>
        public static (double Left, double Top) KeepOnScreen(double left, double top, double width, double height, ScreenBox work, double margin) =>
            (Fit(left, width, work.Left, work.Right, margin), Fit(top, height, work.Top, work.Bottom, margin));

        private static double Fit(double start, double size, double low, double high, double margin)
        {
            var max = high - size + margin;
            var min = low - margin;
            return Math.Max(min, Math.Min(start, max));
        }
    }

    /// <summary>Tells a click from a drag: the window only moves once the mouse has gone past the threshold.</summary>
    public sealed class DragGesture
    {
        private readonly double _threshold;
        private double _mouseX;
        private double _mouseY;
        private double _left;
        private double _top;

        public DragGesture(double threshold)
        {
            _threshold = threshold;
        }

        /// <summary>The mouse button is down on the pill.</summary>
        public bool Active { get; private set; }

        /// <summary>The press has turned into a drag.</summary>
        public bool Dragging { get; private set; }

        public void Begin(double mouseX, double mouseY, double left, double top)
        {
            _mouseX = mouseX;
            _mouseY = mouseY;
            _left = left;
            _top = top;
            Active = true;
            Dragging = false;
        }

        /// <summary>True when the window should move to <paramref name="left"/>, <paramref name="top"/>.</summary>
        public bool Update(double mouseX, double mouseY, out double left, out double top)
        {
            var dx = mouseX - _mouseX;
            var dy = mouseY - _mouseY;
            left = _left + dx;
            top = _top + dy;
            if (!Active)
            {
                return false;
            }
            if (!Dragging && Math.Abs(dx) < _threshold && Math.Abs(dy) < _threshold)
            {
                return false;
            }
            Dragging = true;
            return true;
        }

        /// <summary>The button is up. True when the press was a drag, false when it was a click.</summary>
        public bool End()
        {
            var dragged = Active && Dragging;
            Active = false;
            Dragging = false;
            return dragged;
        }
    }

    /// <summary>Spots a double click on the pill, measured in screen pixels so it still works when the pill changes size.</summary>
    public sealed class DoubleClick
    {
        private readonly TimeSpan _maxGap;
        private readonly double _maxDistance;
        private DateTime? _firstAt;
        private double _firstX;
        private double _firstY;

        public DoubleClick(TimeSpan maxGap, double maxDistance)
        {
            _maxGap = maxGap;
            _maxDistance = maxDistance;
        }

        /// <summary>
        /// A press. True when it completes a double click. Only a press outside the buttons can start one,
        /// so clicking a button twice quickly still works as two clicks.
        /// </summary>
        public bool Down(DateTime at, double x, double y, bool onButton)
        {
            if (_firstAt.HasValue && at - _firstAt.Value <= _maxGap
                && Math.Abs(x - _firstX) <= _maxDistance && Math.Abs(y - _firstY) <= _maxDistance)
            {
                _firstAt = null;
                return true;
            }
            _firstAt = onButton ? (DateTime?)null : at;
            _firstX = x;
            _firstY = y;
            return false;
        }

        public void Forget() => _firstAt = null;
    }
}
