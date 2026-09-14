
namespace SmartX.Shared.Models;

public class Sensor
{
    public string MacAddress { get; set; } = string.Empty;

    public string Room { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;

    public SensorCategory Category { get; set; }

    public DateTime RegisteredAt { get; set; }

    public Sensor()
    {
        RegisteredAt = DateTime.UtcNow;
    }
}
