namespace Hark.Core.Tests;

public class AnswerDraftTests
{
    private static AskQuestion Q(string text, bool multi = false) =>
        new AskQuestion { Text = text, Options = new[] { "a", "b", "c" }, Multi = multi };

    [Fact]
    public void Single_question_single_choice_sends_on_pick()
    {
        var d = new AnswerDraft(new[] { Q("Q1?") });
        Assert.Equal(PickResult.Send, d.Pick(0, "b"));
        Assert.Equal("b", d.Answers!["Q1?"]);
    }

    [Fact]
    public void Picking_on_a_middle_step_moves_to_the_next()
    {
        var d = new AnswerDraft(new[] { Q("Q1?"), Q("Q2?"), Q("Q3?") });
        Assert.Equal(PickResult.Next, d.Pick(0, "a"));
        Assert.Equal(1, d.Step);
        Assert.Equal(PickResult.Next, d.Pick(1, "c"));
        Assert.Equal(2, d.Step);
    }

    [Fact]
    public void Last_step_pick_sends_when_all_answered()
    {
        var d = new AnswerDraft(new[] { Q("Q1?"), Q("Q2?") });
        d.Pick(0, "a");
        Assert.Equal(PickResult.Send, d.Pick(1, "b"));
        Assert.Equal(new Dictionary<string, string> { ["Q1?"] = "a", ["Q2?"] = "b" }, d.Answers);
    }

    [Fact]
    public void Last_step_pick_stays_when_an_earlier_question_is_open()
    {
        var d = new AnswerDraft(new[] { Q("Q1?"), Q("Q2?") });
        d.Next();
        Assert.Equal(PickResult.Stay, d.Pick(1, "b"));
        Assert.Null(d.Answers);
    }

    [Fact]
    public void Multi_choice_toggles_and_stays()
    {
        var d = new AnswerDraft(new[] { Q("Q1?", multi: true), Q("Q2?") });
        Assert.Equal(PickResult.Stay, d.Pick(0, "a"));
        Assert.Equal(PickResult.Stay, d.Pick(0, "c"));
        Assert.Equal(PickResult.Stay, d.Pick(0, "a"));
        Assert.Equal(new[] { "c" }, d.Chosen[0]);
        Assert.True(d.StepAnswered(0));
        Assert.Equal(0, d.Step);
    }

    [Fact]
    public void Own_text_answers_a_step()
    {
        var d = new AnswerDraft(new[] { Q("Q1?") });
        Assert.False(d.StepAnswered(0));
        d.SetOwn(0, "  my words ");
        Assert.True(d.StepAnswered(0));
        Assert.Equal("my words", d.Answers!["Q1?"]);
    }

    [Fact]
    public void Back_and_next_stay_in_range()
    {
        var d = new AnswerDraft(new[] { Q("Q1?"), Q("Q2?") });
        d.Back();
        Assert.Equal(0, d.Step);
        d.Next();
        d.Next();
        Assert.Equal(1, d.Step);
        Assert.True(d.IsLast);
        d.Back();
        Assert.Equal(0, d.Step);
    }
}
