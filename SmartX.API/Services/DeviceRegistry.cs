using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Live registered devices.
// DATA STRUCTURE: ConcurrentDictionary keyed by MAC address -> O(1) lookup for every incoming packet.
public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<string, DeviceState> _devices =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(Sensor sensor)
    {
        _devices.TryAdd(sensor.MacAddress, new DeviceState
        {
            MacAddress = sensor.MacAddress,
            NodeId = sensor.NodeId,
            Room = sensor.Room,
            Zone = sensor.Zone,
            Category = sensor.Category
        });
    }

    public bool Contains(string mac) => _devices.ContainsKey(mac);

    // O(1) lookup of the live (mutable) device object
    public bool TryGet(string mac, [MaybeNullWhen(false)] out DeviceState device)
        => _devices.TryGetValue(mac, out device);

    public DeviceState? GetSnapshot(string mac)
    {
        if (!_devices.TryGetValue(mac, out var device)) return null;
        lock (device) return device.Clone();
    }

    public List<DeviceState> Snapshot()
    {
        var list = new List<DeviceState>(_devices.Count);
        foreach (var device in _devices.Values)
        {
            lock (device) list.Add(device.Clone());
        }
        return list.OrderBy(d => d.NodeId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Applies a telemetry record. Returns true when the device was previously Offline (it has reconnected).
    public bool ApplyTelemetry(DeviceState device, TelemetryRecord record)
    {
        lock (device)
        {
            bool wasOffline = device.Status == DeviceStatus.Offline;
            device.Status = DeviceStatus.Online;
            device.PacketType = record.PacketType;
            device.LastValue = record.DataValue;
            device.Severity = record.Severity;
            device.LastSeenUtc = DateTime.UtcNow;
            return wasOffline;
        }
    }

    public void ApplyCommand(DeviceState device, CommandType type)
    {
        lock (device)
        {
            switch (type)
            {
                case CommandType.PumpOn: device.PumpOn = true; break;
                case CommandType.PumpOff: device.PumpOn = false; break;
                case CommandType.RelayOn: device.RelayOn = true; break;
                case CommandType.RelayOff: device.RelayOn = false; break;
                case CommandType.ValveOpen: device.ValveOpen = true; break;
                case CommandType.ValveClose: device.ValveOpen = false; break;
            }
        }
    }

    public void MarkOffline(DeviceState device)
    {
        lock (device) device.Status = DeviceStatus.Offline;
    }

    // Online devices that have not sent telemetry within the timeout
    public List<string> FindStale(TimeSpan timeout)
    {
        var cutoff = DateTime.UtcNow - timeout;
        var stale = new List<string>();

        foreach (var device in _devices.Values)
        {
            lock (device)
            {
                if (device.Status == DeviceStatus.Online && device.LastSeenUtc < cutoff)
                {
                    stale.Add(device.MacAddress);
                }
            }
        }
        return stale;
    }

    // Search + filter by operational category and active alert state
    public List<DeviceState> Query(string? search, string? category, string? state)
    {
        IEnumerable<DeviceState> result = Snapshot();

        if (!string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<SensorCategory>(category.Replace(" ", ""), true, out var cat))
        {
            result = result.Where(d => d.Category == cat);
        }

        if (!string.IsNullOrWhiteSpace(state) &&
            !state.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            result = result.Where(d => d.AlertState.Equals(state, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // every word must match one of the searchable fields
            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            result = result.Where(d =>
            {
                string haystack = $"{d.MacAddress} {d.NodeId} {d.Room} {d.Zone} {d.Category} {d.PacketType}";
                return words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase));
            });
        }

        return result.ToList();
    }
}
