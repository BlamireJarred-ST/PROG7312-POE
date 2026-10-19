using SmartX.API.Services;
using SmartX.Shared.Models;

namespace SmartX.API;

// All Part 2 endpoints
public static class Part2Endpoints
{
    public static void MapPart2Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Part 2 - Command Stream");

        // search, category, state
        api.MapGet("/devices", (string? search, string? category, string? state, DeviceRegistry registry)
            => Results.Ok(registry.Query(search, category, state)))
            .WithName("GetDevices");

        api.MapGet("/devices/{mac}", (string mac, DeviceRegistry registry) =>
            registry.GetSnapshot(mac) is { } device
                ? Results.Ok(device)
                : Results.NotFound(new { message = $"No device '{mac}' is registered." }))
            .WithName("GetDevice");

        // Timeline data from the SortedDictionary
        api.MapGet("/devices/{mac}/history", (string mac, int? take, DeviceRegistry registry, HistoryStore history) =>
        {
            if (!registry.Contains(mac))
            {
                return Results.NotFound(new { message = $"No device '{mac}' is registered." });
            }

            return Results.Ok(history.Latest(mac, Math.Clamp(take ?? 60, 1, 500)));
        })
        .WithName("GetDeviceHistory");

        // Simulates a node disconnected report.
        api.MapPost("/devices/{mac}/disconnect", (string mac, DeviceRegistry registry, TelemetryPipeline pipeline) =>
        {
            if (!registry.TryGet(mac, out var device))
            {
                return Results.NotFound(new { message = $"No device '{mac}' is registered." });
            }

            bool raised = pipeline.ReportDisconnect(device.MacAddress);

            return Results.Ok(new
            {
                device = device.MacAddress,
                raised,
                duplicateSuppressed = !raised,
                message = raised
                    ? "Disconnect alert raised."
                    : "Duplicate ignored: this node is already in the active error set."
            });
        })
        .WithName("SimulateDisconnect");

        api.MapPost("/commands", (CommandRequest request, DeviceRegistry registry, CommandHistory commands, ActionEngine engine) =>
        {
            if (!registry.TryGet(request.DeviceId, out var device))
            {
                return Results.NotFound(new { message = $"No device '{request.DeviceId}' is registered." });
            }

            registry.ApplyCommand(device, request.Type);

            var command = new OverrideCommand
            {
                DeviceId = device.MacAddress,
                Type = request.Type,
                IssuedBy = string.IsNullOrWhiteSpace(request.IssuedBy) ? "Operator" : request.IssuedBy,
                Description = $"{request.Type.ToLabel()} on {device.NodeId}"
            };

            commands.Push(command);                                  // Stack.Push
            engine.RecordCommand(device.MacAddress, request.Type);   // feeds the predictive engine

            return Results.Created($"/api/commands/{command.Id}", command);
        })
        .WithName("IssueCommand");

        // Populates most recent command and applies inverse
        api.MapPost("/commands/undo", (DeviceRegistry registry, CommandHistory commands) =>
        {
            if (!commands.TryPopForUndo(out var original) || original is null)  // Stack.Pop
            {
                return Results.Conflict(new { message = "Nothing to undo." });
            }

            var inverse = original.Type.Inverse();

            string description = $"UNDO: {inverse.ToLabel()}";

            if (registry.TryGet(original.DeviceId, out var device))
            {
                registry.ApplyCommand(device, inverse);
                description = $"UNDO: {inverse.ToLabel()} on {device.NodeId}";
            }

            var undo = new OverrideCommand
            {
                DeviceId = original.DeviceId,
                Type = inverse,
                IssuedBy = original.IssuedBy,
                IsUndo = true,
                UndoOf = original.Id,
                Description = description
            };

            commands.RecordUndo(undo);

            return Results.Ok(new { undone = original, applied = undo });
        })
        .WithName("UndoLastCommand");

        // Undo stack content
        api.MapGet("/commands/stack", (CommandHistory commands) => Results.Ok(commands.StackSnapshot()))
            .WithName("GetUndoStack");

        // Full audit trail
        api.MapGet("/commands/history", (CommandHistory commands) => Results.Ok(commands.Audit(100)))
            .WithName("GetCommandHistory");

        // alerts
        api.MapGet("/alerts", (AlertCenter alerts) => Results.Ok(alerts.Snapshot(50)))
            .WithName("GetAlerts");

        // pipeline status and control
        api.MapGet("/pipeline/status", (TelemetryPipeline pipeline) => Results.Ok(pipeline.GetStatus()))
            .WithName("GetPipelineStatus");

        // Holds the worker so packets stack up in the queues
        api.MapPost("/pipeline/pause", (TelemetryPipeline pipeline) =>
        {
            pipeline.Pause();
            return Results.Ok(new { paused = true });
        })
        .WithName("PausePipeline");

        api.MapPost("/pipeline/resume", (TelemetryPipeline pipeline) =>
        {
            pipeline.Resume();
            return Results.Ok(new { paused = false });
        })
        .WithName("ResumePipeline");

        // The dashboard reports a search when the user submits it
        api.MapPost("/activity/search", (SearchActivityRequest request, ActionEngine engine) =>
        {
            engine.RecordSearch(request.Term);
            return Results.NoContent();
        })
        .WithName("RecordSearch");

        api.MapGet("/suggestions", (ActionEngine engine) => Results.Ok(engine.GetSuggestions()))
            .WithName("GetSuggestions");

        // simulation helpers 

        // Sends a CRITICAL packet through the normal ingestion path. Body is optional: defaults to the first device and a 980 W power spike.
        api.MapPost("/simulate/critical-alert", (SimulateAlertRequest? request, DeviceRegistry registry, TelemetryPipeline pipeline) =>
        {
            string? mac = request?.DeviceId;
            if (string.IsNullOrWhiteSpace(mac))
            {
                mac = registry.Snapshot().FirstOrDefault()?.MacAddress;
            }

            if (mac is null || !registry.TryGet(mac, out var device))
            {
                return Results.NotFound(new { message = "Device not found." });
            }

            string packetType = string.IsNullOrWhiteSpace(request?.PacketType) ? "PowerWattage" : request!.PacketType!;
            string value = string.IsNullOrWhiteSpace(request?.Value) ? DefaultCriticalValue(packetType) : request!.Value!;

            var record = new TelemetryRecord
            {
                DeviceId = device.MacAddress,
                Timestamp = DateTime.UtcNow,
                PacketType = packetType,
                DataValue = value,
                Severity = "Critical"
            };

            string lane = pipeline.Enqueue(record);
            var status = pipeline.GetStatus();

            return Results.Ok(new
            {
                message = $"Critical {packetType} packet sent for {device.NodeId}.",
                lane,
                standardQueueDepth = status.StandardQueueDepth,
                priorityQueueDepth = status.PriorityQueueDepth
            });
        })
        .WithName("SimulateCriticalAlert");

        // alert -> search -> command pattern. Body is optional: defaults to the first actuator and power device, a "TurnOff" command, and 3 repetitions.
        api.MapPost("/simulate/seed-history", (SeedHistoryRequest? request, DeviceRegistry registry, ActionEngine engine) =>
        {
            request ??= new SeedHistoryRequest();
            var devices = registry.Snapshot();

            string? alertMac = request.AlertDeviceId ??
                               devices.FirstOrDefault(d => d.Category == SensorCategory.Actuator)?.MacAddress;
            string? commandMac = request.CommandDeviceId ??
                                 devices.FirstOrDefault(d => d.Category == SensorCategory.PowerConsumption)?.MacAddress;

            if (alertMac is null || commandMac is null ||
                !registry.TryGet(alertMac, out var alertDevice) ||
                !registry.TryGet(commandMac, out var commandDevice))
            {
                return Results.BadRequest(new { message = "Could not resolve the alert / command devices." });
            }

            int repetitions = Math.Clamp(request.Repetitions, 2, 10);

            engine.SeedDemoPattern(alertDevice.MacAddress, commandDevice.MacAddress,
                request.Command, request.SearchTerm, repetitions);

            return Results.Ok(new
            {
                message = $"Seeded {repetitions} x (alert on {alertDevice.NodeId} -> search '{request.SearchTerm}' -> " +
                          $"{request.Command.ToLabel()} on {commandDevice.NodeId})."
            });
        })
        .WithName("SeedHistory");
    }

    private static string DefaultCriticalValue(string packetType) => packetType.ToLowerInvariant() switch
    {
        "temperature" => "52.0",
        "powerwattage" => "980",
        "moisture" => "12",
        "valvestate" => "true",
        _ => "999"
    };
}
