using System.Globalization;

namespace SmartX.Shared.Services;

// Culture-safe parsing of telemetry values.
// Part 1 formats numbers with the machine culture (e.g. "23,45" on en-ZA), while a Docker
// container runs with the invariant culture. This parser accepts both "23.45" and "23,45".
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

        // Valve state: true/false -> 1/0
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
