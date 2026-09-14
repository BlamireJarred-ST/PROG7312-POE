using SmartX.Shared.Models;

namespace SmartX.Shared.Services;

// Raw batches of telemetry data from sensor
// Jagged Array used to store data
public class TelemetryBatchProcessor
{
    // Define MAX number of batches
    private const int MAX_BATCHES = 100;

    //Stores data for each batch 
    private readonly double[][] _rawBatches;

    // Stores device IDs corresponding to each batch
    private readonly string[] _deviceIds;

    // Stores processed telemetry records
    private readonly List<TelemetryRecord> _processed;

    // Keeps track of the number of batches added
    private int _batchCount;

    // Creates a new instance of the TelemetryBatchProcessor class and initializes the storage
    public TelemetryBatchProcessor()
    {
        _rawBatches = new double[MAX_BATCHES][];
        _deviceIds = new string[MAX_BATCHES];
        _processed = new List<TelemetryRecord>();

        _batchCount = 0;
    }

    // Adds a batch of telemetry readings for a specific device
    public void AddBatch(string deviceId, double[] readings)
    {
        // Check if the batch count has reached the maximum limit
        if (_batchCount >= MAX_BATCHES)
        {
            // Throw an exception if the batch buffer is full
            throw new InvalidOperationException(
                $"The telemetry batch buffer is full. Maximum capacity is {MAX_BATCHES} batches.");
        }

        // Validate the device ID and readings
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            // Throw an exception if the device ID is empty or whitespace
            throw new ArgumentException(
                "Device ID cannot be empty.",
                nameof(deviceId));
        }

        // Check if the readings array is null or empty
        if (readings == null || readings.Length == 0)
        {
            // Throw an exception if the readings array is null or empty
            throw new ArgumentException(
                "Telemetry readings cannot be empty.",
                nameof(readings));
        }

        // Store the readings and device ID in the respective arrays
        _rawBatches[_batchCount] = readings;
        _deviceIds[_batchCount] = deviceId;

        _batchCount++;
    }

    // Processes all added batches and converts them into telemetry records
    public List<TelemetryRecord> ProcessBatches()
    {
        // Clear the processed list before processing new batches
        _processed.Clear();

        // Process each stored batch
        for (int batchIndex = 0; batchIndex < _batchCount; batchIndex++)
        {
            double[] readings = _rawBatches[batchIndex];

            // Process each reading in the batch
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

            // Clear the batch data after processing so the storage can be reused
            _rawBatches[batchIndex] = null!;
            _deviceIds[batchIndex] = null!;
        }

        // Reset the batch count 
        _batchCount = 0;

        return _processed.ToList();
    }

    // Returns the number of processed telemetry records
    public int GetProcessedCount()
    {
        return _processed.Count;
    }
}