using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Active alert tracking.
// DATA STRUCTURE: HashSet<string> of active, unique error states (e.g. "AA:BB:..|Disconnected" or
// "AA:BB:..|Temperature|Critical"). HashSet.Add returns false for duplicates, so the same
// disconnect / critical condition is never alerted twice until it is cleared.
public sealed class AlertCenter
{
    private const int MaxFeed = 200;

    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<AlertEntry> _feed = new(); // newest first
    private readonly object _lock = new();
    private long _duplicatesSuppressed;

    public long DuplicatesSuppressed
    {
        get { lock (_lock) return _duplicatesSuppressed; }
    }

    public int ActiveCount
    {
        get { lock (_lock) return _active.Count; }
    }

    // Returns true if this is a NEW error state (alert raised); false if it was already active (duplicate ignored)
    public bool TryRaise(string key, AlertEntry entry)
    {
        lock (_lock)
        {
            if (!_active.Add(key))
            {
                _duplicatesSuppressed++;
                return false;
            }

            _feed.AddFirst(entry);
            if (_feed.Count > MaxFeed) _feed.RemoveLast();
            return true;
        }
    }

    public bool Clear(string key)
    {
        lock (_lock) return _active.Remove(key);
    }

    public AlertsSnapshot Snapshot(int take)
    {
        lock (_lock)
        {
            return new AlertsSnapshot
            {
                ActiveErrorStates = _active.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
                DuplicatesSuppressed = _duplicatesSuppressed,
                Recent = _feed.Take(take).ToList()
            };
        }
    }
}
