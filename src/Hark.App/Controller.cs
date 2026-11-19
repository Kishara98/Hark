using System;
using System.Linq;
using System.Windows.Threading;
using Hark.Core;

namespace Hark.App
{
    /// <summary>Every second: read the inbox, update the state, redraw the pill and the cards.</summary>
    internal sealed class Controller
    {
        private static readonly TimeSpan BeatEvery = TimeSpan.FromSeconds(30);

        private readonly WatchState _state = new WatchState(StepStats.Load(AppPaths.StatsFile));
        private readonly SnoozeBook _book = new SnoozeBook();
        private readonly Inbox _inbox = new Inbox(AppPaths.InboxDir, AppLog.Write);
        private readonly SettingsStore _store = new SettingsStore(AppPaths.SettingsFile);
        private readonly AnswerChannel _answers = new AnswerChannel(AppPaths.DataDir);
        private readonly PillWindow _pill = new PillWindow();
        private readonly CardStackWindow _cards = new CardStackWindow();
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly System.Collections.Generic.HashSet<string> _logged = new System.Collections.Generic.HashSet<string>();
        private DateTime _lastBeat = DateTime.MinValue;
        private bool _openAvailable;

        public Controller()
        {
            Settings = _store.Load();
            _timer.Tick += (s, e) => Tick();
            _cards.SnoozeRequested += card =>
            {
                _book.Snooze(card.Id, DateTime.UtcNow.AddMinutes(Settings.SnoozeMinutes));
                Redraw(DateTime.UtcNow);
            };
            _cards.DismissRequested += card =>
            {
                _book.Dismiss(card.Id);
                Redraw(DateTime.UtcNow);
            };
            _cards.OpenRequested += card => RequestOpen(card.SessionId);
            _cards.AnswerInVsCodeRequested += card => RequestOpen(card.SessionId);
            _cards.SendRequested += (card, answers) =>
            {
                _answers.Send(card.AnswerId, answers);
                _book.Dismiss(card.Id);
                AppLog.Write($"answered from the card: {card.Project}, {answers.Count} question(s)");
                Redraw(DateTime.UtcNow);
            };
            _pill.OpenRequested += RequestOpen;
            _pill.CloseRequested += () =>
            {
                _book.ClosePill(ViewPlanner.Pill(_state, DateTime.UtcNow));
                Redraw(DateTime.UtcNow);
            };
        }

        /// <summary>Raised when something to open appears (true) or the screen is clear again (false).</summary>
        public event Action<bool> OpenAvailableChanged;

        /// <summary>Raised with a session id when the user asks to open its window.</summary>
        public event Action<string> OpenSession;

        /// <summary>Raised after every redraw; true while any session waits for the user.</summary>
        public event Action<bool> WaitingChanged;

        public AppSettings Settings { get; private set; }

        public WatchState State => _state;

        public Card TopCard { get; private set; }

        /// <summary>The oldest waiting session (what the pill's Open opens).</summary>
        public string PillTarget { get; private set; }

        /// <summary>Opens a session's window; a question the answer hook holds is let go first so VS Code shows it.</summary>
        public void RequestOpen(string sessionId)
        {
            var pending = _state.Sessions.FirstOrDefault(s => s.SessionId == sessionId)?.Pending;
            if (pending?.Kind == PendingKind.Question && _answers.IsHeld(pending.ToolUseId))
            {
                _answers.Release(pending.ToolUseId);
            }
            OpenSession?.Invoke(sessionId);
        }

        public void Start()
        {
            _inbox.PurgeOld(DateTime.UtcNow, TimeSpan.FromDays(1));
            _answers.PurgeOld(DateTime.UtcNow, TimeSpan.FromMinutes(31));
            _answers.SetPaused(Settings.Paused);
            Tick();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            Heartbeat.Remove(AppPaths.DataDir);
            ReleaseAll();
            _pill.Close();
            _cards.Close();
        }

        public void SetPaused(bool paused)
        {
            Settings.Paused = paused;
            _store.Save(Settings);
            _answers.SetPaused(paused);
            if (paused)
            {
                ReleaseAll();
            }
            Redraw(DateTime.UtcNow);
        }

        public void ResetPillPosition()
        {
            _pill.ResetPosition();
            AppLog.Write("pill back at the top (tray)");
        }

        private void Tick()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now - _lastBeat >= BeatEvery)
                {
                    Heartbeat.Touch(AppPaths.DataDir);
                    _lastBeat = now;
                }
                _state.ApplyBatch(_inbox.ReadNew(now));
                _state.DropStale(now);
                if (_state.Stats.Changed)
                {
                    _state.Stats.Save(AppPaths.StatsFile);
                }
                Redraw(now);
            }
            catch (Exception ex)
            {
                AppLog.Write("tick failed: " + ex);
            }
        }

        private void Redraw(DateTime now)
        {
            var all = ViewPlanner.AllCards(_state);
            foreach (var card in all.Where(c => !_logged.Contains(c.Id)))
            {
                AppLog.Write(LogFile.CardLine(card));
            }
            _logged.Clear();
            _logged.UnionWith(all.Select(c => c.Id));
            _book.Forget(all.Select(c => c.Id));
            var cards = ViewPlanner.Cards(_state, _book, now);
            var pill = ViewPlanner.Pill(_state, now);
            TopCard = cards.FirstOrDefault();
            PillTarget = pill.TargetSessionId;
            WaitingChanged?.Invoke(pill.ShowOpen);
            var pillClosed = _book.IsPillClosed(pill);
            var available = !Settings.Paused && (TopCard != null || PillTarget != null);
            if (available != _openAvailable)
            {
                _openAvailable = available;
                OpenAvailableChanged?.Invoke(available);
            }
            if (Settings.Paused)
            {
                _pill.Update(new PillModel(), true, Array.Empty<AgentRow>());
                _cards.Update(Array.Empty<Card>(), now, Settings, _answers.IsHeld);
                return;
            }
            _pill.Update(pill, pillClosed, ViewPlanner.PanelRows(_state, now));
            _cards.Update(cards, now, Settings, _answers.IsHeld);
        }

        private void ReleaseAll()
        {
            foreach (var id in _answers.HeldIds())
            {
                _answers.Release(id);
            }
        }
    }
}
