
namespace SmartX.Shared.Models;

// Sensor identification
public class Sensor
{
    // Sensor MAC address
    public string MacAddress { get; set; } = string.Empty;
    // Sensor Location
    public string Room { get; set; } = string.Empty;
    // Sensor Zone
    public string Zone { get; set; } = string.Empty;
    // Sensor Node ID
    public string NodeId { get; set; } = string.Empty;

    // Sensor Category
    public SensorCategory Category { get; set; }

    // Sensor Registration Timestamp
    public DateTime RegisteredAt { get; set; }

    // New sensor creation timestamp
    public Sensor()
    {
        RegisteredAt = DateTime.UtcNow;
    }
}
