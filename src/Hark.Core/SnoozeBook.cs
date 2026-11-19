using System;
using System.Collections.Generic;
using System.Linq;

namespace Hark.Core
{
    /// <summary>Remembers which cards the user snoozed or closed.</summary>
    public sealed class SnoozeBook
    {
        private readonly Dictionary<string, DateTime> _snoozed = new Dictionary<string, DateTime>();
        private readonly HashSet<string> _dismissed = new HashSet<string>();

        private string _closedPill;

        /// <summary>Hides the pill until it has something new: a new waiting item, or a new agent run.</summary>
        public void ClosePill(PillModel pill) => _closedPill = pill.Visible ? pill.CloseKey : null;

        public bool IsPillClosed(PillModel pill)
        {
            if (!pill.Visible)
            {
                _closedPill = null;
                return false;
            }
            return _closedPill != null && _closedPill == pill.CloseKey;
        }

        public void Snooze(string id, DateTime untilUtc) => _snoozed[id] = untilUtc;

        public void Dismiss(string id) => _dismissed.Add(id);

        public bool IsHidden(string id, DateTime nowUtc) =>
            _dismissed.Contains(id) || (_snoozed.TryGetValue(id, out var until) && nowUtc < until);

        public void Forget(IEnumerable<string> liveIds)
        {
            var live = new HashSet<string>(liveIds);
            foreach (var id in _snoozed.Keys.Where(k => !live.Contains(k)).ToList())
            {
                _snoozed.Remove(id);
            }
            _dismissed.RemoveWhere(id => !live.Contains(id));
        }
    }
}
