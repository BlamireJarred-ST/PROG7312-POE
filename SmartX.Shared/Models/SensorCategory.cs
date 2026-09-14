
using System.Text.Json.Serialization;

namespace SmartX.Shared.Models;

// Represents enum values as strings
[JsonConverter(typeof(JsonStringEnumConverter))]
// Defines the category of a sensor, which can be Environmental, PowerConsumption, or Actuator.
public enum SensorCategory
{
    // Measures temprature
    Environmental,
    // Measures power in watts
    PowerConsumption,
    // Monitiors valves
    Actuator
}