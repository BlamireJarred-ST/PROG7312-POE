using System.Globalization;

namespace SmartX.Shared.Services;

// parsing of telemetry values
public static class ValueParser
{
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim();

        // Valve state
        if (bool.TryParse(s, out bool flag))
        {
            value = flag ? 1 : 0;
            return true;
        }

        // Decimal comma
        if (s.Contains(',') && !s.Contains('.'))
        {
            s = s.Replace(',', '.');
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
