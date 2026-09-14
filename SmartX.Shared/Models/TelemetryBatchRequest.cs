namespace SmartX.Shared.Models;

// Represents a batch request for telemetry data from multiple devices
public class TelemetryBatchRequest
{
    // Array of device IDs for which telemetry data is requested
    public string[] DeviceIds { get; set; } = Array.Empty<string>();
    // 2D array of telemetry data batches corresponding to the device IDs
    public double[][] Batches { get; set; } = Array.Empty<double[]>();
}