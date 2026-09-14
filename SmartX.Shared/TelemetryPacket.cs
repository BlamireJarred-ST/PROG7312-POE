
namespace SmartX.Shared.Models;

// Represents a telemetry packet containing data from a device
public class TelemetryPacket<T>
{
    // Unique identifier for the device sending the telemetry data
    public string DeviceId { get; set; }
    // Timestamp indicating when the telemetry data was generated
    public DateTime Timestamp { get; set; }
    // The actual telemetry data of type T
    public T Data { get; set; }
    // Type of the telemetry packet
    public string PacketType { get; set; }

    // Initializes a new instance of the TelemetryPacket class 
    public TelemetryPacket(string deviceId, T data, string packetType)
    {
        DeviceId = deviceId;
        Timestamp = DateTime.UtcNow;
        Data = data;
        PacketType = packetType;
    }

    // Returns a string representation of the telemetry packet
    public override string ToString()
        => $"[{PacketType}] Station {DeviceId} @ {Timestamp:HH:mm:ss} => {Data}";
}
