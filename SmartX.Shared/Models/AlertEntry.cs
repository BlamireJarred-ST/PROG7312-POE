namespace SmartX.Shared.Models;

// One raised alert (shown in the alert feed)
public class AlertEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string DeviceId { get; set; } = string.Empty;
    // Telemetry packet type, or "Disconnected"
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    // STANDARD or PRIORITY queue lane that carried the packet
    public string Lane { get; set; } = string.Empty;
}
