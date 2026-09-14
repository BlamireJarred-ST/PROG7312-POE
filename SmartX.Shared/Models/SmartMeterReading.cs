
namespace SmartX.Shared.Models;

// Represents the load in watts
public class SmartMeterReading
{
    // Stores Load in watts
    public double Load { get; set; }

    //  Creates new smart meter reading
    public SmartMeterReading(double load)
    {
        Load = load;
    }

    // Adds loads of two smart meter readings
    public static SmartMeterReading operator +(SmartMeterReading a, SmartMeterReading b)
    {
        return new SmartMeterReading(a.Load + b.Load);
    }

    // Subtracts loads of two smart meter readings
    public static SmartMeterReading operator -(SmartMeterReading a, SmartMeterReading b)
    {
        return new SmartMeterReading(a.Load - b.Load);
    }

    // Compares loads of two smart meter readings
    public static bool operator >(SmartMeterReading a, SmartMeterReading b)
    {
        return a.Load > b.Load;
    }

    // Compares loads of two smart meter readings
    public static bool operator <(SmartMeterReading a, SmartMeterReading b)
    {
        return a.Load < b.Load;
    }

    // Converts the smart meter reading to a string representation
    public override string ToString() => $"({Load})W";
}
