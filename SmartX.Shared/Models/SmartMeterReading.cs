
namespace SmartX.Shared.Models;

public class SmartMeterReading
{
    public double Load { get; set; }

    public SmartMeterReading(double load)
    {
        Load = load;
    }

    public static SmartMeterReading operator +(SmartMeterReading a, SmartMeterReading b)
    {
        return new SmartMeterReading(a.Load + b.Load);
    }

    public static SmartMeterReading operator -(SmartMeterReading a, SmartMeterReading b)
    {
        return new SmartMeterReading(a.Load - b.Load);
    }

    public static bool operator >(SmartMeterReading a, SmartMeterReading b)
    {
        return a.Load > b.Load;
    }

    public static bool operator <(SmartMeterReading a, SmartMeterReading b)
    {
        return a.Load < b.Load;
    }

    public override string ToString() => $"({Load})W";
}
