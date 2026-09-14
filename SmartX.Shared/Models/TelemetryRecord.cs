
namespace SmartX.Shared.Models;

public class TelemetryRecord
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string PacketType { get; set; } = string.Empty;
    public string DataValue { get; set; } = string.Empty;
    public string Severity { get; set; } = "Normal";
}
