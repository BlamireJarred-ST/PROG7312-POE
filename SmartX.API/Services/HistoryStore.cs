using System.Collections.Concurrent;
using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Historical sensor logs.
// DATA STRUCTURE: SortedDictionary<DateTime, TelemetryRecord> per device - always iterates in timestamp
// order, even if packets were processed out of order (e.g. a critical packet jumped the queue),
// so the dashboard can draw the timeline without sorting.
public sealed class HistoryStore
{
    private const int MaxPerDevice = 1000;

    private sealed class DeviceHistory
    {
        public readonly object Lock = new();
        public readonly SortedDictionary<DateTime, TelemetryRecord> Entries = new();
    }

    private readonly ConcurrentDictionary<string, DeviceHistory> _byDevice =
        new(StringComparer.OrdinalIgnoreCase);

    public void Add(TelemetryRecord record)
    {
        var history = _byDevice.GetOrAdd(record.DeviceId, _ => new DeviceHistory());

        lock (history.Lock)
        {
            // SortedDictionary keys must be unique: nudge identical timestamps by one tick
            DateTime key = record.Timestamp;
            while (history.Entries.ContainsKey(key))
            {
                key = key.AddTicks(1);
            }

            history.Entries.Add(key, record);

            // keep memory bounded: drop the oldest entry
            if (history.Entries.Count > MaxPerDevice)
            {
                history.Entries.Remove(history.Entries.Keys.First());
            }
        }
    }

    // Latest N records in ascending timestamp order
    public List<TelemetryRecord> Latest(string mac, int take)
    {
        if (!_byDevice.TryGetValue(mac, out var history))
        {
            return new List<TelemetryRecord>();
        }

        lock (history.Lock)
        {
            int skip = Math.Max(0, history.Entries.Count - take);
            return history.Entries.Values.Skip(skip).ToList();
        }
    }
}
