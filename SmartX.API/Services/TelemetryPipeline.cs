using System.Collections.Concurrent;
using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Telemetry processing pipeline.
//
//   POST /api/telemetry ──► Enqueue() ──┬── Critical? ──► PriorityQueue (priority, then FIFO) ──┐
//                                       └── otherwise ──► ConcurrentQueue (strict FIFO) ─────────┤
//                                                                                                 ▼
//                                  background worker: ALWAYS drains the priority queue first, then the FIFO queue
//
// DATA STRUCTURES: Queue (FIFO), PriorityQueue (critical bypass), plus the Dictionary / SortedDictionary / HashSet
// services it feeds.
public sealed class TelemetryPipeline
{
    private const int MaxRecent = 30;

    // Standard FIFO queue
    private readonly ConcurrentQueue<TelemetryRecord> _standard = new();

    // Priority queue for critical alerts. Priority = (rank, sequence): lower rank is served first,
    // the sequence number keeps packets with equal rank in FIFO order. PriorityQueue is not thread-safe -> lock.
    private readonly PriorityQueue<TelemetryRecord, (int Rank, long Seq)> _critical = new();
    private readonly object _criticalLock = new();

    // One signal per enqueued packet wakes the worker
    private readonly SemaphoreSlim _signal = new(0);

    private readonly DeviceRegistry _registry;
    private readonly HistoryStore _history;
    private readonly AlertCenter _alerts;
    private readonly ActionEngine _engine;

    private readonly Queue<ProcessedEntry> _recent = new();
    private readonly object _recentLock = new();

    private long _sequence;
    private long _processed;
    private long _unknownDevice;
    private volatile bool _paused;

    public TelemetryPipeline(DeviceRegistry registry, HistoryStore history, AlertCenter alerts, ActionEngine engine)
    {
        _registry = registry;
        _history = history;
        _alerts = alerts;
        _engine = engine;
    }

    public bool Paused => _paused;
    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    // Classifies the packet and puts it on the correct queue. Returns the lane used.
    public string Enqueue(TelemetryRecord record)
    {
        // The server is authoritative: never trust a client that says "Normal" for a spike
        record.Severity = SeverityEvaluator.Higher(
            record.Severity,
            SeverityEvaluator.Evaluate(record.PacketType, record.DataValue));

        string lane;

        if (record.Severity == "Critical")
        {
            lock (_criticalLock)
            {
                _critical.Enqueue(record, (PriorityRank(record), Interlocked.Increment(ref _sequence)));
            }
            lane = "PRIORITY";
        }
        else
        {
            _standard.Enqueue(record);
            lane = "STANDARD";
        }

        _signal.Release();
        return lane;
    }

    // Severe power spike first, then hydroponics moisture, then temperature, then anything else
    private static int PriorityRank(TelemetryRecord record) =>
        record.PacketType.ToLowerInvariant() switch
        {
            "powerwattage" => 0,
            "moisture" => 1,
            "temperature" => 2,
            _ => 3
        };

    // Worker loop (called by TelemetryWorker)
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _signal.WaitAsync(ct);

            // Allows the README test to stack up packets, then release them to show the ordering
            while (_paused && !ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);
            }

            if (TryDequeueNext(out var record, out var lane))
            {
                Process(record!, lane);
            }
        }
    }

    // Priority queue is ALWAYS served before the FIFO queue
    private bool TryDequeueNext(out TelemetryRecord? record, out string lane)
    {
        lock (_criticalLock)
        {
            if (_critical.TryDequeue(out record, out _))
            {
                lane = "PRIORITY";
                return true;
            }
        }

        if (_standard.TryDequeue(out record))
        {
            lane = "STANDARD";
            return true;
        }

        lane = string.Empty;
        return false;
    }

    private void Process(TelemetryRecord record, string lane)
    {
        long order = Interlocked.Increment(ref _processed);

        lock (_recentLock)
        {
            _recent.Enqueue(new ProcessedEntry
            {
                Order = order,
                DeviceId = record.DeviceId,
                PacketType = record.PacketType,
                Value = record.DataValue,
                Severity = record.Severity,
                Lane = lane,
                ProcessedAtUtc = DateTime.UtcNow
            });

            while (_recent.Count > MaxRecent) _recent.Dequeue();
        }

        // O(1) Dictionary lookup of the device by MAC address
        if (!_registry.TryGet(record.DeviceId, out var device))
        {
            Interlocked.Increment(ref _unknownDevice);
            return;
        }

        // A packet from a device flagged as disconnected means it is back
        bool reconnected = _registry.ApplyTelemetry(device, record);
        if (reconnected)
        {
            _alerts.Clear($"{device.MacAddress}|Disconnected");
        }

        // SortedDictionary keeps the history in timestamp order
        _history.Add(record);

        UpdateAlertState(device, record, lane);
    }

    // HashSet-based de-duplication of active error states
    private void UpdateAlertState(DeviceState device, TelemetryRecord record, string lane)
    {
        string baseKey = $"{device.MacAddress}|{record.PacketType}";

        foreach (string severity in new[] { "Warning", "Critical" })
        {
            string key = $"{baseKey}|{severity}";

            if (record.Severity == severity)
            {
                var entry = new AlertEntry
                {
                    DeviceId = device.MacAddress,
                    AlertType = record.PacketType,
                    Severity = severity,
                    Lane = lane,
                    Message = $"{device.NodeId}: {record.PacketType} = {record.DataValue} ({severity})"
                };

                // false => the same error state is already active: duplicate ignored
                if (_alerts.TryRaise(key, entry))
                {
                    _engine.RecordAlert(device.MacAddress);
                }
            }
            else
            {
                // condition is no longer present: allow it to alert again in the future
                _alerts.Clear(key);
            }
        }
    }

    // Used by the heartbeat monitor and the disconnect simulation endpoint.
    // Returns true if a NEW disconnect alert was raised, false if the node was already flagged (duplicate ignored).
    public bool ReportDisconnect(string mac)
    {
        if (!_registry.TryGet(mac, out var device))
        {
            return false;
        }

        _registry.MarkOffline(device);

        var entry = new AlertEntry
        {
            DeviceId = device.MacAddress,
            AlertType = "Disconnected",
            Severity = "Critical",
            Lane = "SYSTEM",
            Message = $"{device.NodeId} stopped reporting (disconnected)"
        };

        bool raised = _alerts.TryRaise($"{device.MacAddress}|Disconnected", entry);

        if (raised)
        {
            _engine.RecordAlert(device.MacAddress);
        }

        return raised;
    }

    public PipelineStatus GetStatus()
    {
        int priorityDepth;
        lock (_criticalLock) priorityDepth = _critical.Count;

        List<ProcessedEntry> recent;
        lock (_recentLock) recent = _recent.ToList();

        return new PipelineStatus
        {
            StandardQueueDepth = _standard.Count,
            PriorityQueueDepth = priorityDepth,
            Paused = _paused,
            TotalProcessed = Interlocked.Read(ref _processed),
            UnknownDevicePackets = Interlocked.Read(ref _unknownDevice),
            ActiveErrorCount = _alerts.ActiveCount,
            DuplicateAlertsSuppressed = _alerts.DuplicatesSuppressed,
            RecentlyProcessed = recent
        };
    }
}
