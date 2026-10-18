using System.Text.Json.Serialization;

namespace SmartX.Shared.Models;

// Connection status of a registered device
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceStatus
{
    // Registered but no telemetry has been received yet
    NeverSeen,
    // Telemetry received recently
    Online,
    // Heartbeat timed out or a disconnect was reported
    Offline
}

// Live state of one registered device (the value stored in the API's device Dictionary)
public class DeviceState
{
    public string MacAddress { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string Room { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public SensorCategory Category { get; set; }
    public DeviceStatus Status { get; set; } = DeviceStatus.NeverSeen;

    // Most recent telemetry
    public string PacketType { get; set; } = string.Empty;
    public string LastValue { get; set; } = string.Empty;
    public string Severity { get; set; } = "Normal";
    public DateTime? LastSeenUtc { get; set; }

    // Outputs controlled by manual override commands
    public bool PumpOn { get; set; }
    public bool RelayOn { get; set; }
    public bool ValveOpen { get; set; }

    // Set by the client when the Action Engine highlights this device
    public bool Flagged { get; set; }

    // The state used by the "active alert state" filter: Normal / Warning / Critical / Offline
    [JsonIgnore]
    public string AlertState => Status == DeviceStatus.Offline ? "Offline" : Severity;

    [JsonIgnore]
    public string OutputSummary =>
        $"Pump {(PumpOn ? "ON" : "off")} | Relay {(RelayOn ? "ON" : "off")} | Valve {(ValveOpen ? "OPEN" : "closed")}";

    [JsonIgnore]
    public string FlagMark => Flagged ? "⚑" : string.Empty;

    [JsonIgnore]
    public DateTime? LastSeenLocal => LastSeenUtc?.ToLocalTime();

    public DeviceState Clone() => (DeviceState)MemberwiseClone();
}
