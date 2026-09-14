namespace SmartX.Shared.Models;

public class TelemetryBatchRequest
{
    public string[] DeviceIds { get; set; } = Array.Empty<string>();
    public double[][] Batches { get; set; } = Array.Empty<double[]>();
}