using System.Collections.Concurrent;
using SmartX.Shared.Models;
using SmartX.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Enable Swagger when API is running
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

//Data storage
// Stores registered sensors by MAC address
ConcurrentDictionary<string, Sensor> sensors = new();

// Stores telemetry records in memory
List<TelemetryRecord> telemetryLog = new();
object telemetryLock = new();

// Stores sensor attachments by MAC address
ConcurrentDictionary<string, List<SensorAttachment>> sensorAttachments = new ConcurrentDictionary<string, List<SensorAttachment>>();

// Nodes for the sensor 
// Facility created for demonstration
var facility = new Facility
{
    Name = "Facility A",
    IsActive = true
};

// Zone created for demonstration
var zone = new Zone
{
    Name = "Zone 1",
    IsActive = true,
    Parent = facility
};

// SubZone created for demonstration
var subZone = new SubZone
{
    Name = "Sub-Zone 1",
    IsActive = true,
    Parent = zone
};

// Sensor nodes created for demonstration
var sensorNode = new SensorNode
{
    Name = "Node 1",
    IsActive = true,
    MacAddress = "AA:BB:CC:DD:EE:FF",
    NodeId = "NODE-01",
    Parent = subZone
};

// Additional sensor nodes created for demonstration
var sensorNode2 = new SensorNode
{
    Name = "Node 2",
    IsActive = true,
    MacAddress = "AA:BB:CC:DD:EE:02",
    NodeId = "NODE-02",
    Parent = subZone
};

// Additional sensor node created for demonstration
var sensorNode3 = new SensorNode
{
    Name = "Node 3",
    IsActive = true,
    MacAddress = "AA:BB:CC:DD:EE:03",
    NodeId = "NODE-03",
    Parent = subZone
};

// Inactive zone created for demonstration
var inactiveZone = new Zone
{
    Name = "Zone 2 (Offline)",
    IsActive = false,
    Parent = facility
};

// Inactive sensor node created for demonstration
var offlineNode = new SensorNode
{
    Name = "Node Offline",
    IsActive = true,
    MacAddress = "FF:FF:FF:FF:FF:FF",
    NodeId = "NODE-OFFLINE",
    Parent = inactiveZone
};

// Add Zone to  facility
facility.Zones.Add(zone);
// Add SubZone to Zone
zone.SubZones.Add(subZone);
// Add SubZone to inactive Zone
subZone.Nodes.Add(sensorNode);
// Add SubZone to inactive Zone
subZone.Nodes.Add(sensorNode2);
// Add SubZone to inactive Zone
subZone.Nodes.Add(sensorNode3);

// Inactive Zone
Dictionary<string, SensorNode> sensorNodes = new()
{
    [sensorNode.MacAddress] = sensorNode,
    [sensorNode2.MacAddress] = sensorNode2,
    [sensorNode3.MacAddress] = sensorNode3,
    ["FF:FF:FF:FF:FF:FF"] = offlineNode
};

// Pre-populate some sensors for demonstration
sensors.TryAdd("AA:BB:CC:DD:EE:FF", new Sensor
{
    MacAddress = "AA:BB:CC:DD:EE:FF",
    Room = "Server Room",
    Zone = "Zone 1",
    NodeId = "NODE-01",
    Category = SensorCategory.Environmental
});

sensors.TryAdd("AA:BB:CC:DD:EE:02", new Sensor
{
    MacAddress = "AA:BB:CC:DD:EE:02",
    Room = "Pump Room",
    Zone = "Zone 1",
    NodeId = "NODE-02",
    Category = SensorCategory.PowerConsumption
});

sensors.TryAdd("AA:BB:CC:DD:EE:03", new Sensor
{
    MacAddress = "AA:BB:CC:DD:EE:03",
    Room = "Valve Bay",
    Zone = "Zone 1",
    NodeId = "NODE-03",
    Category = SensorCategory.Actuator
});

// API Endpoints

// Register a new sensor
app.MapPost("/api/sensors", (Sensor sensor) =>
{
    if (sensors.ContainsKey(sensor.MacAddress))
        return Results.Conflict(new { message = $"Sensor '{sensor.MacAddress}' already registered." });
    
    if (!sensors.TryAdd(sensor.MacAddress, sensor))
    {
        return Results.Conflict(new
        {
            message = $"Sensor '{sensor.MacAddress}' could not be added. It may already exist."
        });
    }

    return Results.Created($"/api/sensors/{sensor.MacAddress}", sensor);
})
.WithName("RegisterSensor")
.WithOpenApi();


// Get all registered sensors
app.MapGet("/api/sensors", () =>
{
    return Results.Ok(sensors.Values.ToList());
})
.WithName("GetSensors")
.WithOpenApi();

// single telemetry record
app.MapPost("/api/telemetry", (TelemetryRecord record) =>
{
    record.Timestamp = DateTime.UtcNow;

    lock (telemetryLock)
    {
        telemetryLog.Add(record);
    }

    return Results.Created("/api/telemetry", record);
})
.WithName("IngestTelemetry")
.WithOpenApi();

// retrieve the latest 50 records
app.MapGet("/api/telemetry", () =>
{
    List<TelemetryRecord> latestRecords;

    lock (telemetryLock)
    {
        latestRecords = telemetryLog
            .TakeLast(50)
            .Reverse()
            .ToList();
    }

    return Results.Ok(latestRecords);
})
.WithName("GetTelemetry")
.WithOpenApi();

// Retrieve telemetry for a specific sensor
app.MapGet("/api/telemetry/{mac}", (string mac) =>
{
    List<TelemetryRecord> records;

    lock (telemetryLock)
    {
        records = telemetryLog
            .Where(r => r.DeviceId.Equals(mac, StringComparison.OrdinalIgnoreCase))
            .TakeLast(50)
            .Reverse()
            .ToList();
    }

    if (records.Count == 0)
    {
        return Results.NotFound(new
        {
            message = $"No telemetry records found for sensor '{mac}'."
        });
    }

    return Results.Ok(records);
})
.WithName("GetTelemetryByMac")
.WithOpenApi();

// ingest multiple telemetry batches
app.MapPost("/api/telemetry/batch", (TelemetryBatchRequest request) =>
{
    if (request.DeviceIds.Length != request.Batches.Length)
    {
        return Results.BadRequest(new
        {
            message = "The number of device IDs must match the number of telemetry batches."
        });
    }

    var processor = new TelemetryBatchProcessor();

    try
    {
        for (int i = 0; i < request.Batches.Length; i++)
        {
            processor.AddBatch(
                request.DeviceIds[i],
                request.Batches[i]);
        }

        List<TelemetryRecord> records = processor.ProcessBatches();

        lock (telemetryLock)
        {
            telemetryLog.AddRange(records);
        }

        return Results.Ok(new
        {
            message = "Telemetry batch processed successfully.",
            batchesReceived = request.Batches.Length,
            recordsIngested = records.Count
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new
        {
            message = ex.Message
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new
        {
            message = ex.Message
        });
    }
})
.WithName("IngestTelemetryBatch")
.WithOpenApi();

// Validates the deployment of a sensor node
app.MapGet("/api/sensors/{mac}/validate", (string mac) =>
{
    if (!sensorNodes.TryGetValue(mac, out var node))
    {
        return Results.NotFound(new
        {
            message = $"No deployment node found for MAC address '{mac}'."
        });
    }

    bool isValid =
        DeploymentValidator.ValidateNodeConfiguration(node);

    string path =
        DeploymentValidator.GetNodePath(node);

    string message = isValid
        ? "The sensor node is safely configured. All deployment levels are active."
        : "The sensor node is not safely configured because one or more deployment levels are inactive.";

    return Results.Ok(new
    {
        mac = node.MacAddress,
        path,
        isValid,
        message
    });
})
.WithName("ValidateSensorNode")
.WithOpenApi();

// Upload a file to a registered sensor profile
app.MapPost("/api/sensors/{mac}/attachments",
    async (string mac, IFormFile file) =>
    {
        // Check that the sensor exists
        if (!sensors.ContainsKey(mac))
        {
            return Results.NotFound(new
            {
                message = $"No sensor with MAC address '{mac}' is registered."
            });
        }

        // Validate the uploaded file
        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new
            {
                message = "The uploaded file is empty."
            });
        }

        // Read the uploaded file into memory
        using var stream = new MemoryStream();

        await file.CopyToAsync(stream);

        byte[] fileBytes = stream.ToArray();

        // Create the attachment object
        var attachment = new SensorAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            FileData = fileBytes,
            UploadedAt = DateTime.UtcNow
        };

        // Get or create the attachment list for this sensor
        var list = sensorAttachments.GetOrAdd(mac, _ => new List<SensorAttachment>());

        lock (list)   // lock the list itself — ConcurrentDictionary protects the dict,
        {             // but List<T> still needs a lock for concurrent adds
            list.Add(attachment);
        }

        // Return a summary without the binary FileData
        return Results.Created(
            $"/api/sensors/{mac}/attachments",
            new
            {
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSizeBytes,
                attachment.UploadedAt
            });
    })
.WithName("UploadAttachment")
.DisableAntiforgery()
.WithOpenApi();

// List files attached to a sensor
app.MapGet("/api/sensors/{mac}/attachments", (string mac) =>
{
    // Check that the sensor exists
    if (!sensors.ContainsKey(mac))
    {
        return Results.NotFound(new
        {
            message = $"No sensor with MAC address '{mac}' is registered."
        });
    }

    // Get the attachment list
    if (!sensorAttachments.TryGetValue(mac, out var list))
    {
        return Results.Ok(new List<object>());
    }

    // Return metadata only
    var attachments = list.Select(attachment => new
    {
        attachment.FileName,
        attachment.ContentType,
        attachment.FileSizeBytes,
        attachment.UploadedAt
    }).ToList();

    return Results.Ok(attachments);
})
.WithName("GetAttachments")
.WithOpenApi();

// returns the anomaly thresholds used by the system
app.MapGet("/api/telemetry/thresholds", () =>
{
    return Results.Ok(new
    {
        temperature = new { warningAbove = 35.0, criticalAbove = 45.0 },
        powerWattage = new { warningAbove = 700, criticalAbove = 900 },
        valveState = new { criticalWhenTrue = false }
        // A valve stuck open (true) when it should be closed is a critical state
    });
})
.WithName("GetThresholds")
.WithOpenApi();

app.Run();
