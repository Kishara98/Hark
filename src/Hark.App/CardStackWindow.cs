using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hark.Core;

namespace Hark.App
{
    /// <summary>Bottom right stack: the top card in full and up to two peek bars behind it.</summary>
    internal sealed class CardStackWindow : Window
    {
        private const double PeekStep = 7;
        private readonly Grid _root = new Grid();
        private readonly Dictionary<string, AnswerDraft> _answers = new Dictionary<string, AnswerDraft>();
        private string _shownKey;
        private CardView _view;
        private Card _top;
        private int _count;
        private AppSettings _settings;
        private bool _canAnswer;
        private string _ago;

        public event Action<Card> SnoozeRequested;
        public event Action<Card> DismissRequested;
        public event Action<Card> OpenRequested;
        public event Action<Card> AnswerInVsCodeRequested;
        public event Action<Card, IReadOnlyDictionary<string, string>> SendRequested;

        public CardStackWindow()
        {
            Title = "Hark cards";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = Theme.CardWidth + 2 * Theme.ShadowRoom;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Theme.Sans;
            _root.Margin = new Thickness(Theme.ShadowRoom);
            Content = _root;
            NoActivate.Apply(this);
            // Typing an own answer lends the window the keyboard; give it back when the user moves on.
            Deactivated += (s, e) => NoActivate.Restore(this);
            SizeChanged += (s, e) => Place();
        }

        public void Update(IReadOnlyList<Card> cards, DateTime nowUtc, AppSettings settings, Func<string, bool> isHeld)
        {
            foreach (var id in _answers.Keys.Where(k => cards.All(c => c.Id != k)).ToList())
            {
                _answers.Remove(id);
            }
            if (cards.Count == 0)
            {
                _shownKey = null;
                if (IsVisible)
                {
                    Hide();
                    NoActivate.Restore(this);
                }
                return;
            }
            _top = cards[0];
            _count = cards.Count;
            _settings = settings;
            _canAnswer = _top.Kind == CardKind.Question && _top.Questions.Count > 0 && isHeld(_top.AnswerId);
            _ago = ViewPlanner.Ago(_top.SinceUtc, nowUtc);
            var key = $"{_top.Id}|{_count}|{settings.SnoozeMinutes}|{settings.OpenHotkey}|{_canAnswer}|{_top.Meta}|{_top.Body}";
            if (key != _shownKey)
            {
                Build();
                _shownKey = key;
            }
            _view?.SetAgo(_ago);
            if (!IsVisible)
            {
                Show();
            }
            Place();
        }

        private void Build()
        {
            var card = _top;
            if (!_answers.TryGetValue(card.Id, out var state))
            {
                state = new AnswerDraft(card.Questions);
                _answers[card.Id] = state;
            }
            _root.Children.Clear();
            var behind = Math.Min(_count, 3) - 1;
            for (var i = behind; i >= 1; i--)
            {
                _root.Children.Add(new Border
                {
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Top,
                    Background = Theme.White,
                    BorderBrush = Theme.Line,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Opacity = i == 1 ? 0.85 : 0.55,
                    Margin = new Thickness(9 * i, PeekStep * (behind - i), 9 * i, 0),
                });
            }
            var actions = new CardActions
            {
                Dismiss = () => DismissRequested?.Invoke(card),
                Snooze = () => SnoozeRequested?.Invoke(card),
                Open = () => OpenRequested?.Invoke(card),
                AnswerInVsCode = () =>
                {
                    NoActivate.Restore(this);
                    AnswerInVsCodeRequested?.Invoke(card);
                },
                Send = answers =>
                {
                    NoActivate.Restore(this);
                    SendRequested?.Invoke(card, answers);
                },
                Refresh = Build,
                WantKeyboard = () =>
                {
                    NoActivate.Allow(this);
                    Activate();
                },
            };
            _view = new CardView(card, _count, _ago, _settings, _canAnswer, state, actions)
            {
                Margin = new Thickness(0, PeekStep * behind, 0, 0),
            };
            _root.Children.Add(_view);
        }

        private void Place()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - 20 - Theme.CardWidth - Theme.ShadowRoom;
            Top = area.Bottom - 20 - ActualHeight + Theme.ShadowRoom;
        }
    }
}
