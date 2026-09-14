
using System.Text.Json.Serialization;

namespace SmartX.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SensorCategory
{
    Environmental,
    PowerConsumption,
    Actuator
}