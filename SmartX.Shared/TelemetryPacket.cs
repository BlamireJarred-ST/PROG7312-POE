
namespace SmartX.Shared.Models; 

public class TelemetryPacket<T>
{
    public string DeviceId { get; set; }
    public DateTime Timestamp { get; set; }
    public T Data { get; set; }
    public string PacketType { get; set; }

    public TelemetryPacket(string deviceId, T data, string packetType)
    {
        DeviceId = deviceId;
        Timestamp = DateTime.UtcNow;
        Data = data;
        PacketType = packetType;
    }

    public override string ToString()
        => $"[{PacketType}] Station {DeviceId} @ {Timestamp:HH:mm:ss} => {Data}";
}
