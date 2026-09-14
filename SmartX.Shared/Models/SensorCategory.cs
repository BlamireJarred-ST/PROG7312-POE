
using System.Text.Json.Serialization;

namespace SmartX.Shared.Models;

// Defines the category of a sensor, which can be Environmental, PowerConsumption, or Actuator.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SensorCategory
{
    // Measures temprature
    Environmental,
    // Measures power in watts
    PowerConsumption,
    // Monitiors valves
    Actuator
}