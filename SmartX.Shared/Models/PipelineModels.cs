namespace SmartX.Shared.Models;

// One telemetry packet as it left the processing pipeline
public class ProcessedEntry
{
    // 1-based processing order
    public long Order { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string PacketType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    // STANDARD (FIFO queue) or PRIORITY (priority queue)
    public string Lane { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}

public class PipelineStatus
{
    public int StandardQueueDepth { get; set; }
    public int PriorityQueueDepth { get; set; }
    public bool Paused { get; set; }
    public long TotalProcessed { get; set; }
    public long UnknownDevicePackets { get; set; }
    public int ActiveErrorCount { get; set; }
    public long DuplicateAlertsSuppressed { get; set; }
    // Oldest -> newest
    public List<ProcessedEntry> RecentlyProcessed { get; set; } = new();
}

public class AlertsSnapshot
{
    // Contents of the HashSet of active, unique error states
    public List<string> ActiveErrorStates { get; set; } = new();
    public long DuplicatesSuppressed { get; set; }
    // Newest first
    public List<AlertEntry> Recent { get; set; } = new();
}

public class SearchActivityRequest
{
    public string Term { get; set; } = string.Empty;
}

public class SimulateAlertRequest
{
    public string? DeviceId { get; set; }
    public string? PacketType { get; set; }
    public string? Value { get; set; }
}

public class SeedHistoryRequest
{
    public string? AlertDeviceId { get; set; }
    public string? CommandDeviceId { get; set; }
    public CommandType Command { get; set; } = CommandType.PumpOn;
    public string SearchTerm { get; set; } = "pump";
    public int Repetitions { get; set; } = 3;
}
