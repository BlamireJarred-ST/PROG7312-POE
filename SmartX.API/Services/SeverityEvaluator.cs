using SmartX.Shared.Services;

namespace SmartX.API.Services;

// Server-side severity rules (matches /api/telemetry/thresholds from Part 1, plus hydroponics moisture)
public static class SeverityEvaluator
{
    public sealed record Limit(double Warn, double Critical, bool HigherIsWorse);

    private static readonly Dictionary<string, Limit> Limits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Temperature"] = new Limit(35, 45, true),     // degrees C
        ["PowerWattage"] = new Limit(700, 900, true),  // watts
        ["Moisture"] = new Limit(30, 20, false)        // percent - LOWER is worse
    };

    public static Limit? GetLimit(string packetType) => Limits.GetValueOrDefault(packetType);

    public static string Evaluate(string packetType, string dataValue)
    {
        // An open valve is unexpected -> warning (same rule as the Part 1 dashboard)
        if (string.Equals(packetType, "ValveState", StringComparison.OrdinalIgnoreCase))
        {
            return bool.TryParse(dataValue, out bool open) && open ? "Warning" : "Normal";
        }

        var limit = GetLimit(packetType);

        if (limit is null || !ValueParser.TryParseNumber(dataValue, out double v))
        {
            return "Normal";
        }

        if (limit.HigherIsWorse)
        {
            return v > limit.Critical ? "Critical" : v > limit.Warn ? "Warning" : "Normal";
        }

        return v < limit.Critical ? "Critical" : v < limit.Warn ? "Warning" : "Normal";
    }

    public static int Rank(string severity) => severity switch
    {
        "Critical" => 2,
        "Warning" => 1,
        _ => 0
    };

    public static string Higher(string a, string b) => Rank(a) >= Rank(b) ? a : b;
}
