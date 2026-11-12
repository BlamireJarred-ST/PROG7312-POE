using System.Diagnostics;
using SmartX.API.DataStructures;
using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Historical telemetry index: one AVL tree per device (found by MAC address in a Dictionary).
// Live telemetry is added by the processing pipeline; range queries use the tree instead of scanning a list.
public sealed class TelemetryHistoryIndex
{
    // keeps memory bounded: the oldest readings are dropped first
    private const int MaxRecordsPerDevice = 50_000;

    private readonly Dictionary<string, TelemetryAvlTree> _trees = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Add(TelemetryRecord record)
    {
        lock (_lock)
        {
            AddUnlocked(record);
        }
    }

    public void AddRange(IEnumerable<TelemetryRecord> records)
    {
        lock (_lock)
        {
            foreach (var record in records)
            {
                AddUnlocked(record);
            }
        }
    }

    public void Clear()
    {
        lock (_lock) _trees.Clear();
    }

    private void AddUnlocked(TelemetryRecord record)
    {
        if (!_trees.TryGetValue(record.DeviceId, out var tree))
        {
            _trees[record.DeviceId] = tree = new TelemetryAvlTree();
        }

        tree.Insert(record);

        while (tree.RecordCount > MaxRecordsPerDevice)
        {
            tree.RemoveOldest();
        }
    }

    // Range query: readings of one device between two instants, optionally of one packet type
    // (e.g. all Moisture readings between 14:00 and 16:00 for NODE-04).
    public RangeQueryResult Query(string deviceId, DateTime? fromUtc, DateTime? toUtc, string? packetType, int take)
    {
        DateTime from = fromUtc ?? DateTime.MinValue;
        DateTime to = toUtc ?? DateTime.MaxValue;

        var result = new RangeQueryResult
        {
            DeviceId = deviceId,
            PacketType = string.IsNullOrWhiteSpace(packetType) ? null : packetType,
            FromUtc = from,
            ToUtc = to
        };

        long started = Stopwatch.GetTimestamp();

        List<TelemetryRecord> inRange;
        int visited;

        lock (_lock)
        {
            if (!_trees.TryGetValue(deviceId, out var tree))
            {
                return result;
            }

            inRange = tree.Range(from, to, out visited);

            result.TotalRecordsInTree = tree.RecordCount;
            result.TotalNodesInTree = tree.NodeCount;
            result.TreeHeight = tree.Height;
            result.OptimalHeight = tree.OptimalHeight;
        }

        if (result.PacketType != null)
        {
            inRange = inRange
                .Where(r => string.Equals(r.PacketType, result.PacketType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        result.ElapsedMicroseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1000.0;
        result.NodesVisited = visited;
        result.TotalMatches = inRange.Count;

        // keep the newest 'take' readings, still in ascending timestamp order
        result.Records = inRange.Count > take ? inRange.Skip(inRange.Count - take).ToList() : inRange;
        result.Returned = result.Records.Count;

        return result;
    }

    public List<TreeStatsEntry> Stats()
    {
        lock (_lock)
        {
            return _trees
                .Select(pair => new TreeStatsEntry
                {
                    DeviceId = pair.Key,
                    RecordCount = pair.Value.RecordCount,
                    NodeCount = pair.Value.NodeCount,
                    TreeHeight = pair.Value.Height,
                    OptimalHeight = pair.Value.OptimalHeight,
                    OldestUtc = pair.Value.OldestTimestamp,
                    NewestUtc = pair.Value.NewestTimestamp
                })
                .OrderBy(s => s.DeviceId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
