using SmartX.Shared.Models;

namespace SmartX.Shared.Services;

public class TelemetryBatchProcessor
{
    private const int MAX_BATCHES = 100;

    private readonly double[][] _rawBatches;

    private readonly string[] _deviceIds;

    private readonly List<TelemetryRecord> _processed;

    private int _batchCount;

    public TelemetryBatchProcessor()
    {
        _rawBatches = new double[MAX_BATCHES][];
        _deviceIds = new string[MAX_BATCHES];
        _processed = new List<TelemetryRecord>();

        _batchCount = 0;
    }

    public void AddBatch(string deviceId, double[] readings)
    {
        if (_batchCount >= MAX_BATCHES)
        {
            throw new InvalidOperationException(
                $"The telemetry batch buffer is full. Maximum capacity is {MAX_BATCHES} batches.");
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new ArgumentException(
                "Device ID cannot be empty.",
                nameof(deviceId));
        }

        if (readings == null || readings.Length == 0)
        {
            throw new ArgumentException(
                "Telemetry readings cannot be empty.",
                nameof(readings));
        }

        _rawBatches[_batchCount] = readings;
        _deviceIds[_batchCount] = deviceId;

        _batchCount++;
    }

    public List<TelemetryRecord> ProcessBatches()
    {
        _processed.Clear();

        for (int batchIndex = 0; batchIndex < _batchCount; batchIndex++)
        {
            double[] readings = _rawBatches[batchIndex];

            for (int readingIndex = 0;
                 readingIndex < readings.Length;
                 readingIndex++)
            {
                double value = readings[readingIndex];

                var record = new TelemetryRecord
                {
                    DeviceId = _deviceIds[batchIndex],
                    DataValue = value.ToString("F2"),
                    PacketType = "RawBatch",
                    Timestamp = DateTime.UtcNow,
                    Severity = "Normal"
                };

                _processed.Add(record);
            }

            _rawBatches[batchIndex] = null!;
            _deviceIds[batchIndex] = null!;
        }

        _batchCount = 0;

        return _processed.ToList();
    }

    public int GetProcessedCount()
    {
        return _processed.Count;
    }
}