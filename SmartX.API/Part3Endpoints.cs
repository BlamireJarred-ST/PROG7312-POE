using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using SmartX.API.Services;
using SmartX.Shared.Models;

namespace SmartX.API;

// Part 3 endpoints: Network Topology and Mesh Routing + historical telemetry queries (AVL tree)
public static class Part3Endpoints
{
    public static void MapPart3Endpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").WithTags("Part 3 - Network Topology");

        // ---------------- mesh graph, spanning tree ----------------

        // The whole mesh: nodes, links, and which links are in the minimum spanning tree.
        // mst = prim | kruskal
        api.MapGet("/topology", (string? mst, MeshTopologyService topology) =>
            Results.Ok(topology.GetSnapshot(mst)))
            .WithName("GetTopology");

        // Track the routing path of one node. {id} is a MAC address or an identifier such as NODE-02
        api.MapGet("/topology/nodes/{id}/route", (string id, string? mst, MeshTopologyService topology) =>
            topology.GetRoute(id, mst) is { } route
                ? Results.Ok(route)
                : Results.NotFound(new { message = $"No mesh node '{id}'." }))
            .WithName("GetNodeRoute");

        // Fail or restore a node (simulates a node going down)
        api.MapPost("/topology/nodes/{id}/status", (string id, NodeStatusRequest request, MeshTopologyService topology) =>
            topology.SetNodeStatus(id, request.IsUp)
                ? Results.Ok(new { id, isUp = request.IsUp })
                : Results.NotFound(new { message = $"No mesh node '{id}'." }))
            .WithName("SetNodeStatus");

        // BFS or DFS from a gateway / node: connected devices, and the ones that are down or isolated
        api.MapGet("/topology/traverse", ([FromQuery(Name = "from")] string? start, string? algorithm, MeshTopologyService topology) =>
            topology.Traverse(start, algorithm) is { } result
                ? Results.Ok(result)
                : Results.NotFound(new { message = $"No mesh node '{start}'." }))
            .WithName("TraverseTopology");

        // For each failed node: who was routed through it and where they must hop now
        api.MapGet("/topology/failures", (string? mst, MeshTopologyService topology) =>
            Results.Ok(topology.GetFailureAdvice(mst)))
            .WithName("GetFailureAdvice");

        // ---------------- historical telemetry (AVL tree range queries) ----------------

        // Readings of one device between two instants, optionally of one packet type.
        // Example: /api/history/AA:BB:CC:DD:EE:04?from=2026-11-12T12:00:00Z&to=2026-11-12T14:00:00Z&type=Moisture
        api.MapGet("/history/{mac}", (
                string mac,
                [FromQuery(Name = "from")] DateTime? fromTime,
                [FromQuery(Name = "to")] DateTime? toTime,
                string? type,
                int? take,
                DeviceRegistry registry,
                TelemetryHistoryIndex index) =>
        {
            if (!registry.Contains(mac))
            {
                return Results.NotFound(new { message = $"No device '{mac}' is registered." });
            }

            var result = index.Query(
                mac,
                ToUtcOrNull(fromTime),
                ToUtcOrNull(toTime),
                type,
                Math.Clamp(take ?? 500, 1, 5000));

            return Results.Ok(result);
        })
        .WithName("QueryHistory");

        // Shape of each device's tree (records, nodes, height vs the optimal height)
        api.MapGet("/history/stats", (TelemetryHistoryIndex index) => Results.Ok(index.Stats()))
            .WithName("GetHistoryStats");

        // Generates demo history (one reading every 2 minutes for the last N hours) so range queries have data
        api.MapPost("/history/seed", (int? hours, DeviceRegistry registry, TelemetryHistoryIndex index) =>
        {
            int span = Math.Clamp(hours ?? 6, 1, 48);
            int total = SeedHistory(registry, index, span);

            return Results.Ok(new { message = $"Seeded {total} readings covering the last {span} hour(s).", readings = total });
        })
        .WithName("SeedTelemetryHistory");
    }

    // Query-string dates arrive with a 'Z' as local time: normalise everything to UTC
    private static DateTime? ToUtcOrNull(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        DateTime date = value.Value;

        return date.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : date.ToUniversalTime();
    }

    // ---------------------------------------------------------------- demo history generator

    private static string PacketTypeFor(DeviceState device)
    {
        if (device.Room.Contains("Hydroponics", StringComparison.OrdinalIgnoreCase))
        {
            return "Moisture";
        }

        return device.Category switch
        {
            SensorCategory.PowerConsumption => "PowerWattage",
            SensorCategory.Actuator => "ValveState",
            _ => "Temperature"
        };
    }

    private static int SeedHistory(DeviceRegistry registry, TelemetryHistoryIndex index, int hours)
    {
        index.Clear();

        DateTime now = DateTime.UtcNow;
        const int readingsPerHour = 30; // one every 2 minutes
        int points = hours * readingsPerHour;
        int total = 0;

        foreach (var device in registry.Snapshot())
        {
            string type = PacketTypeFor(device);
            var random = new Random(StableSeed(device.MacAddress));
            var batch = new List<TelemetryRecord>(points + 1);

            // oldest first: sorted input is the worst case for a plain binary search tree, so it shows why AVL is used
            for (int i = points; i >= 0; i--)
            {
                double hoursAgo = i / (double)readingsPerHour;
                DateTime time = now - TimeSpan.FromMinutes(2 * i);
                double noise = random.NextDouble() - 0.5;

                string value = type switch
                {
                    // a moisture drop 2 to 2.5 hours ago
                    "Moisture" => Format(hoursAgo is >= 2.0 and <= 2.5
                        ? 14 + 6 * random.NextDouble()
                        : 48 + 10 * Math.Sin(hoursAgo * 0.9) + 2 * noise),

                    // a power spike about an hour ago
                    "PowerWattage" => Format(hoursAgo is >= 1.0 and <= 1.1
                        ? 910 + 40 * random.NextDouble()
                        : 450 + 150 * Math.Sin(hoursAgo * 1.3) + 40 * noise),

                    "ValveState" => ((int)(hoursAgo * 3)) % 2 == 0 ? "True" : "False",

                    _ => Format(24 + 4 * Math.Sin(hoursAgo * 1.1) + noise)
                };

                batch.Add(new TelemetryRecord
                {
                    DeviceId = device.MacAddress,
                    Timestamp = time,
                    PacketType = type,
                    DataValue = value,
                    Severity = SeverityEvaluator.Evaluate(type, value)
                });
            }

            index.AddRange(batch);
            total += batch.Count;
        }

        return total;
    }

    private static string Format(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    // deterministic per device, so the demo data looks the same every time
    private static int StableSeed(string text)
    {
        int hash = 17;

        foreach (char c in text)
        {
            hash = hash * 31 + c;
        }

        return hash;
    }
}
