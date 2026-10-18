using System.Text.Json.Serialization;

namespace SmartX.Shared.Models;

// Manual override commands an operator can send to a device
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommandType
{
    PumpOn,
    PumpOff,
    RelayOn,
    RelayOff,
    ValveOpen,
    ValveClose
}

public static class CommandTypeExtensions
{
    // The command that reverts this one (used by Undo)
    public static CommandType Inverse(this CommandType type) => type switch
    {
        CommandType.PumpOn => CommandType.PumpOff,
        CommandType.PumpOff => CommandType.PumpOn,
        CommandType.RelayOn => CommandType.RelayOff,
        CommandType.RelayOff => CommandType.RelayOn,
        CommandType.ValveOpen => CommandType.ValveClose,
        CommandType.ValveClose => CommandType.ValveOpen,
        _ => type
    };

    public static string ToLabel(this CommandType type) => type switch
    {
        CommandType.PumpOn => "Pump ON",
        CommandType.PumpOff => "Pump OFF",
        CommandType.RelayOn => "Relay ON",
        CommandType.RelayOff => "Relay OFF",
        CommandType.ValveOpen => "Valve OPEN",
        CommandType.ValveClose => "Valve CLOSE",
        _ => type.ToString()
    };
}

// Body of POST /api/commands
public class CommandRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public CommandType Type { get; set; }
    public string IssuedBy { get; set; } = "Operator";
}

// A command that was applied to a device (stored on the undo Stack and in the audit history)
public class OverrideCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeviceId { get; set; } = string.Empty;
    public CommandType Type { get; set; }
    public string IssuedBy { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    // True when this entry was created by an Undo
    public bool IsUndo { get; set; }
    public Guid? UndoOf { get; set; }
    public string Description { get; set; } = string.Empty;
}
