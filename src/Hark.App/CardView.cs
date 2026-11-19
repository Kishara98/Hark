using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hark.Core;

namespace Hark.App
{
    internal sealed class CardActions
    {
        public Action Dismiss;
        public Action Snooze;
        public Action Open;
        public Action AnswerInVsCode;
        public Action<IReadOnlyDictionary<string, string>> Send;
        public Action Refresh;
        public Action WantKeyboard;
    }

    /// <summary>One popup card as drawn on board E; a held question gets answer chips and a one question at a time view.</summary>
    internal sealed class CardView : Border
    {
        private const string ChevronDown = "m6 9 6 6 6-6";
        private const string ChevronUp = "m18 15-6-6-6 6";

        private readonly Card _card;
        private readonly AnswerDraft _draft;
        private readonly CardActions _actions;
        private TextBlock _ago;
        private Button _forward;

        public CardView(Card card, int count, string ago, AppSettings settings, bool canAnswer, AnswerDraft draft, CardActions actions)
        {
            _card = card;
            _draft = draft;
            _actions = actions;
            Width = Theme.CardWidth;
            Background = Theme.White;
            BorderBrush = Theme.Line;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(14);
            Padding = new Thickness(16);
            Effect = Theme.Shadow();
            VerticalAlignment = VerticalAlignment.Top;

            var stack = new StackPanel();
            stack.Children.Add(Header(count, ago, canAnswer));
            stack.Children.Add(ChipRow(card));
            if (canAnswer && draft.Expanded)
            {
                AddStep(stack);
            }
            else
            {
                stack.Children.Add(Body(card.Body));
                if (card.Options.Count > 0)
                {
                    stack.Children.Add(Options(canAnswer));
                }
                if (card.Kind == CardKind.Question || card.Kind == CardKind.Permission)
                {
                    stack.Children.Add(Actions(card, settings, canAnswer));
                }
            }
            Child = stack;
        }

        public void SetAgo(string ago)
        {
            if (_ago != null && _ago.Text != ago)
            {
                _ago.Text = ago;
            }
        }

        private static TextBlock Body(string text)
        {
            var body = Theme.Text(text, 15, Theme.Ink, FontWeights.Medium);
            body.TextWrapping = TextWrapping.Wrap;
            body.LineHeight = 21;
            body.Margin = new Thickness(0, 12, 0, 0);
            return body;
        }

        private UIElement Header(int count, string ago, bool canAnswer)
        {
            var grid = new Grid();
            for (var i = 0; i < 7; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            }
            Add(grid, new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = Theme.Ink,
                Child = Theme.Icon(Theme.EyeIcon, 13, Theme.White, 2),
            }, 0);
            Add(grid, Margin(Theme.Text("Hark", 13, Theme.Ink, FontWeights.SemiBold), 8), 1);
            _ago = Theme.Text(ago, 12, Theme.Muted, FontWeights.Normal);
            Add(grid, Margin(_ago, 8), 2);
            if (count > 1)
            {
                Add(grid, Theme.Text($"1 of {count}", 12, Theme.Text2, FontWeights.Normal), 4);
            }
            if (canAnswer)
            {
                var expanded = _draft.Expanded;
                var toggle = Theme.Button(expanded ? "Hide answers" : "Show answers",
                    Theme.Icon(expanded ? ChevronUp : ChevronDown, 16, Theme.Text2), null, null, Theme.Text2, 28, 8, new Thickness(0),
                    () =>
                    {
                        _draft.Expanded = !_draft.Expanded;
                        _actions.Refresh();
                    });
                toggle.Width = 28;
                toggle.Margin = new Thickness(8, 0, 0, 0);
                Add(grid, toggle, 5);
            }
            var close = Theme.Button("Dismiss", Theme.Icon(Theme.CloseIcon, 15, Theme.Text2), null, null, Theme.Text2, 28, 8, new Thickness(0), _actions.Dismiss);
            close.Width = 28;
            close.Margin = new Thickness(4, 0, 0, 0);
            Add(grid, close, 6);
            return grid;
        }

        private static UIElement ChipRow(Card card)
        {
            var waiting = card.Kind == CardKind.Question || card.Kind == CardKind.Permission || card.Asks;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(new Border
            {
                Background = waiting ? Theme.WaitFill : Theme.DoneFill,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 2),
                Child = Theme.Text(card.Chip, 11, waiting ? Theme.WaitText : Theme.DoneText, FontWeights.SemiBold),
            });
            row.Children.Add(Margin(Theme.Text(card.Meta, 12, Theme.Text2, FontWeights.Normal), 8));
            return row;
        }

        // Short view: the first question's options as chips; clickable while the question is held.
        private UIElement Options(bool canAnswer)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            stack.Children.Add(Theme.Text(canAnswer ? "Choose an answer" : "Options", 11, Theme.Text2, FontWeights.Medium));
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var option in _card.Options)
            {
                var label = option;
                var chosen = canAnswer && _draft.Chosen.Count > 0 && _draft.Chosen[0].Contains(label);
                var text = Theme.Text(label, 12, chosen ? Theme.White : Theme.ChipText, FontWeights.Medium);
                if (!canAnswer)
                {
                    wrap.Children.Add(new Border
                    {
                        Background = Theme.ChipFill,
                        BorderBrush = Theme.Line,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(0, 0, 6, 6),
                        Child = text,
                    });
                    continue;
                }
                var chip = Theme.Button(label, text, chosen ? Theme.Ink : Theme.ChipFill, chosen ? Theme.Ink : Theme.Line, Theme.ChipText,
                    28, 8, new Thickness(10, 0, 10, 0), () => Pick(0, label));
                chip.Margin = new Thickness(0, 0, 6, 6);
                wrap.Children.Add(chip);
            }
            stack.Children.Add(wrap);
            return stack;
        }

        // Full view: one question (step) at a time.
        private void AddStep(StackPanel stack)
        {
            var i = _draft.Step;
            var q = _draft.Questions[i];
            if (_draft.Questions.Count > 1)
            {
                var step = Theme.Text($"Question {i + 1} of {_draft.Questions.Count}", 11, Theme.Text2, FontWeights.Medium);
                step.Margin = new Thickness(0, 12, 0, 0);
                stack.Children.Add(step);
                var body = Body(q.Text);
                body.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(body);
            }
            else
            {
                stack.Children.Add(Body(q.Text));
            }
            var hint = Theme.Text(q.Multi ? "Choose one or more" : "Choose one", 11, Theme.Text2, FontWeights.Medium);
            hint.Margin = new Thickness(0, 12, 0, 0);
            stack.Children.Add(hint);
            for (var o = 0; o < q.Options.Count; o++)
            {
                var label = q.Options[o];
                var description = o < q.Descriptions.Count ? q.Descriptions[o] : "";
                var chosen = _draft.Chosen[i].Contains(label);
                var lines = new StackPanel();
                lines.Children.Add(Theme.Text(label, 13, Theme.Ink, FontWeights.Medium));
                if (!string.IsNullOrEmpty(description))
                {
                    var d = Theme.Text(description, 12, Theme.Text2, FontWeights.Normal);
                    d.TextWrapping = TextWrapping.Wrap;
                    lines.Children.Add(d);
                }
                var row = Theme.Button(label, lines, chosen ? Theme.ChipFill : Theme.White, chosen ? Theme.Ink : Theme.Line, Theme.Ink,
                    double.NaN, 8, new Thickness(12, 8, 12, 8), () => Pick(i, label));
                row.HorizontalContentAlignment = HorizontalAlignment.Left;
                row.BorderThickness = new Thickness(chosen ? 1.5 : 1);
                row.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(row);
            }
            stack.Children.Add(_draft.OwnOpen.Contains(i) ? OwnAnswerBox(i) : OwnAnswerLink(i));
            stack.Children.Add(StepActions());
            UpdateForward();
        }

        private UIElement OwnAnswerLink(int index)
        {
            var link = Theme.Button("Type your own answer", Theme.Text("Type your own answer", 12, Theme.Text2, FontWeights.Medium), null, null, Theme.Text2,
                28, 6, new Thickness(0), () =>
                {
                    _draft.OwnOpen.Add(index);
                    _draft.FocusOwn = true;
                    _actions.Refresh();
                });
            link.HorizontalAlignment = HorizontalAlignment.Left;
            link.Margin = new Thickness(0, 6, 0, 0);
            return link;
        }

        private UIElement OwnAnswerBox(int index)
        {
            var box = new TextBox
            {
                Text = _draft.Own[index],
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                FontFamily = Theme.Sans,
                FontSize = 13,
                Foreground = Theme.Ink,
                Padding = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            System.Windows.Automation.AutomationProperties.SetName(box, "Your own answer");
            var hint = Theme.Text("Type your own answer", 13, Theme.Muted, FontWeights.Normal);
            hint.IsHitTestVisible = false;
            hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (s, e) =>
            {
                _draft.SetOwn(index, box.Text);
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateForward();
            };
            box.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!box.IsKeyboardFocused)
                {
                    TakeKeyboard(box);
                }
            };
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _forward != null && _forward.IsEnabled)
                {
                    Forward();
                }
            };
            if (_draft.FocusOwn)
            {
                _draft.FocusOwn = false;
                box.Loaded += (s, e) => TakeKeyboard(box);
            }
            var grid = new Grid();
            grid.Children.Add(box);
            grid.Children.Add(hint);
            return new Border
            {
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 8, 0, 0),
                Child = grid,
            };
        }

        private void TakeKeyboard(TextBox box)
        {
            _actions.WantKeyboard();
            box.Dispatcher.BeginInvoke(new Action(() =>
            {
                Keyboard.Focus(box);
                box.CaretIndex = box.Text.Length;
            }));
        }

        private UIElement StepActions()
        {
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Add(grid, Theme.Button("Answer in VS Code", Theme.Text("Answer in VS Code", 13, Theme.ButtonText, FontWeights.Medium), Theme.White, Theme.Line,
                Theme.ButtonText, 36, 8, new Thickness(12, 0, 12, 0), _actions.AnswerInVsCode), 0);
            if (_draft.Step > 0)
            {
                var back = Theme.Button("Back", Theme.Text("Back", 13, Theme.ButtonText, FontWeights.Medium), null, null, Theme.ButtonText, 36, 8,
                    new Thickness(10, 0, 10, 0), () =>
                    {
                        _draft.Back();
                        _actions.Refresh();
                    });
                back.Margin = new Thickness(0, 0, 4, 0);
                Add(grid, back, 2);
            }
            var label = _draft.IsLast ? "Send answer" : "Next";
            _forward = Theme.Button(label, Theme.Text(label, 13, Theme.White, FontWeights.SemiBold), Theme.Ink, null, Theme.White, 36, 8,
                new Thickness(14, 0, 14, 0), Forward);
            Add(grid, _forward, 3);
            return grid;
        }

        private UIElement Actions(Card card, AppSettings settings, bool canAnswer)
        {
            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var snoozeText = $"Snooze {settings.SnoozeMinutes} min";
            Add(grid, Theme.Button(snoozeText, Theme.Text(snoozeText, 13, Theme.ButtonText, FontWeights.Medium), Theme.White, Theme.Line, Theme.ButtonText,
                36, 8, new Thickness(12, 0, 12, 0), _actions.Snooze), 0);
            var openText = canAnswer ? "Answer in VS Code" : card.OpenLabel;
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(Theme.Text(openText, 13, Theme.White, FontWeights.SemiBold));
            label.Children.Add(Margin(Theme.Text(settings.OpenHotkey, 11, Theme.PillMuted, FontWeights.Normal, Theme.Mono), 8));
            Add(grid, Theme.Button(openText, label, Theme.Ink, null, Theme.White, 36, 8, new Thickness(12, 0, 12, 0),
                canAnswer ? _actions.AnswerInVsCode : _actions.Open), 2);
            return grid;
        }

        private void Pick(int index, string label)
        {
            switch (_draft.Pick(index, label))
            {
                case PickResult.Send:
                    _actions.Send(_draft.Answers);
                    return;
                default:
                    // Next step, or a multi choice toggle: the full view shows where the user is.
                    _draft.Expanded = true;
                    _actions.Refresh();
                    return;
            }
        }

        private void Forward()
        {
            if (!_draft.IsLast)
            {
                _draft.Next();
                _actions.Refresh();
                return;
            }
            var answers = _draft.Answers;
            if (answers != null)
            {
                _actions.Send(answers);
            }
        }

        private void UpdateForward()
        {
            if (_forward == null)
            {
                return;
            }
            var ready = _draft.IsLast ? _draft.Answers != null : _draft.StepAnswered(_draft.Step);
            _forward.IsEnabled = ready;
            _forward.Opacity = ready ? 1 : 0.4;
        }

        private static FrameworkElement Margin(FrameworkElement e, double left)
        {
            e.Margin = new Thickness(left, 0, 0, 0);
            return e;
        }

        private static void Add(Grid grid, UIElement e, int column)
        {
            Grid.SetColumn(e, column);
            grid.Children.Add(e);
        }
    }
}
