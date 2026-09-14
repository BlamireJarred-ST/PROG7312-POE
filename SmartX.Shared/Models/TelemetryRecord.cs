
namespace SmartX.Shared.Models;

public class TelemetryRecord
{
    // ID for the device sending the telemetry data
    public string DeviceId { get; set; } = string.Empty;
    // Timestamp indicating when the telemetry data was generated
    public DateTime Timestamp { get; set; }
    // The actual telemetry data as a string
    public string PacketType { get; set; } = string.Empty;
    // The actual telemetry data as a string
    public string DataValue { get; set; } = string.Empty;
    // Severity level of the telemetry data, defaulting to = "Normal"
    public string Severity { get; set; } = "Normal";
}
