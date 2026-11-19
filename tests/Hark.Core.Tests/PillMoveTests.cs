namespace Hark.Core.Tests;

public class PillPlacementTests
{
    // Screen pixels: a 1920 x 1040 work area (taskbar below), 24 px of see-through shadow room, 16 px gap at the top.
    private static readonly ScreenBox Work = new ScreenBox(0, 0, 1920, 1040);
    private const double Margin = 24;
    private const double Gap = 16;

    [Fact]
    public void Pill_starts_at_the_top_centre()
    {
        var spot = PillPlacement.Place(null, Work, 348, 92, Margin, Gap);
        Assert.Equal(786, spot.Left);
        Assert.Equal(-8, spot.Top);
    }

    [Fact]
    public void Moved_pill_keeps_its_centre_and_top_when_its_width_changes()
    {
        var anchor = PillPlacement.AnchorOf(300, 500, 348);
        Assert.Equal(474, anchor.CentreX);
        Assert.Equal(500, anchor.Top);
        var panel = PillPlacement.Place(anchor, Work, 608, 300, Margin, Gap);
        Assert.Equal(170, panel.Left);
        Assert.Equal(500, panel.Top);
    }

    [Fact]
    public void Pill_stays_on_screen_at_the_right_and_bottom()
    {
        var spot = PillPlacement.Place(new PillAnchor(1900, 1000), Work, 348, 92, Margin, Gap);
        Assert.Equal(1596, spot.Left);
        Assert.Equal(972, spot.Top);
    }

    [Fact]
    public void Pill_stays_on_screen_at_the_left_and_top()
    {
        var spot = PillPlacement.Place(new PillAnchor(0, -500), Work, 348, 92, Margin, Gap);
        Assert.Equal(-24, spot.Left);
        Assert.Equal(-24, spot.Top);
    }

    [Fact]
    public void Panel_opened_low_on_the_screen_moves_up_to_fit_and_the_pill_goes_back_after()
    {
        var anchor = new PillAnchor(960, 900);
        Assert.Equal(564, PillPlacement.Place(anchor, Work, 608, 500, Margin, Gap).Top);
        Assert.Equal(900, PillPlacement.Place(anchor, Work, 348, 92, Margin, Gap).Top);
    }

    [Fact]
    public void Pill_on_a_second_screen_stays_inside_that_screen()
    {
        var second = new ScreenBox(1920, 0, 2560, 1400);
        var spot = PillPlacement.Place(new PillAnchor(4400, 10), second, 348, 92, Margin, Gap);
        Assert.Equal(4156, spot.Left);
        Assert.Equal(10, spot.Top);
    }

    [Fact]
    public void Window_wider_than_the_screen_sticks_to_the_left_edge()
    {
        Assert.Equal(-24, PillPlacement.KeepOnScreen(500, 100, 3000, 92, Work, Margin).Left);
    }
}

public class DragGestureTests
{
    [Fact]
    public void Small_move_is_a_click_not_a_drag()
    {
        var drag = new DragGesture(4);
        drag.Begin(100, 100, 500, 20);
        Assert.False(drag.Update(102, 101, out _, out _));
        Assert.False(drag.End());
        Assert.False(drag.Active);
    }

    [Fact]
    public void Move_past_the_threshold_moves_the_window_with_the_mouse()
    {
        var drag = new DragGesture(4);
        drag.Begin(100, 100, 500, 20);
        Assert.True(drag.Update(110, 130, out var left, out var top));
        Assert.Equal(510, left);
        Assert.Equal(50, top);
        Assert.True(drag.Dragging);
        Assert.True(drag.End());
        Assert.False(drag.Active);
    }

    [Fact]
    public void Once_dragging_even_small_moves_follow_the_mouse()
    {
        var drag = new DragGesture(4);
        drag.Begin(100, 100, 500, 20);
        drag.Update(120, 100, out _, out _);
        Assert.True(drag.Update(101, 100, out var left, out _));
        Assert.Equal(501, left);
    }

    [Fact]
    public void Mouse_move_without_a_press_does_nothing()
    {
        var drag = new DragGesture(4);
        Assert.False(drag.Update(300, 300, out _, out _));
        Assert.False(drag.End());
    }
}

public class DoubleClickTests
{
    private static readonly DateTime T0 = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

    private static DoubleClick Make() => new DoubleClick(TimeSpan.FromMilliseconds(500), 2);

    [Fact]
    public void Two_quick_presses_on_the_same_spot_are_a_double_click()
    {
        var clicks = Make();
        Assert.False(clicks.Down(T0, 10, 10, false));
        Assert.True(clicks.Down(T0.AddMilliseconds(300), 11, 10, false));
    }

    [Fact]
    public void Slow_second_press_starts_a_new_double_click()
    {
        var clicks = Make();
        clicks.Down(T0, 10, 10, false);
        Assert.False(clicks.Down(T0.AddMilliseconds(600), 10, 10, false));
        Assert.True(clicks.Down(T0.AddMilliseconds(800), 10, 10, false));
    }

    [Fact]
    public void Second_press_far_away_is_not_a_double_click()
    {
        var clicks = Make();
        clicks.Down(T0, 10, 10, false);
        Assert.False(clicks.Down(T0.AddMilliseconds(100), 30, 10, false));
    }

    [Fact]
    public void Press_on_a_button_never_starts_a_double_click()
    {
        var clicks = Make();
        Assert.False(clicks.Down(T0, 10, 10, true));
        Assert.False(clicks.Down(T0.AddMilliseconds(100), 10, 10, false));
    }

    [Fact]
    public void Second_press_landing_on_a_button_still_counts()
    {
        var clicks = Make();
        clicks.Down(T0, 10, 10, false);
        Assert.True(clicks.Down(T0.AddMilliseconds(100), 10, 10, true));
    }

    [Fact]
    public void Third_quick_press_starts_over()
    {
        var clicks = Make();
        clicks.Down(T0, 10, 10, false);
        clicks.Down(T0.AddMilliseconds(100), 10, 10, false);
        Assert.False(clicks.Down(T0.AddMilliseconds(200), 10, 10, false));
    }

    [Fact]
    public void Forget_drops_the_first_press()
    {
        var clicks = Make();
        clicks.Down(T0, 10, 10, false);
        clicks.Forget();
        Assert.False(clicks.Down(T0.AddMilliseconds(100), 10, 10, false));
    }
}
