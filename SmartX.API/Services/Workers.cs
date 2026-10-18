namespace SmartX.API.Services;

// Drains the telemetry queues in the background so the API endpoints return immediately
public sealed class TelemetryWorker : BackgroundService
{
    private readonly TelemetryPipeline _pipeline;

    public TelemetryWorker(TelemetryPipeline pipeline) => _pipeline = pipeline;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _pipeline.RunAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}

// Flags devices that stop sending telemetry as disconnected
public sealed class HeartbeatMonitor : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly DeviceRegistry _registry;
    private readonly TelemetryPipeline _pipeline;

    public HeartbeatMonitor(DeviceRegistry registry, TelemetryPipeline pipeline)
    {
        _registry = registry;
        _pipeline = pipeline;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckEvery);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // a paused pipeline would make every device look silent
                if (_pipeline.Paused) continue;

                foreach (string mac in _registry.FindStale(StaleAfter))
                {
                    _pipeline.ReportDisconnect(mac);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
