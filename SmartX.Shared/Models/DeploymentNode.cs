namespace SmartX.Shared.Models;

public abstract class DeploymentNode
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DeploymentNode? Parent { get; set; }
}

public class Facility : DeploymentNode
{
    public List<Zone> Zones { get; set; } = new List<Zone>();
}

public class Zone : DeploymentNode
{
    public List<SubZone> SubZones { get; set; } = new List<SubZone>();
}

public class SubZone : DeploymentNode
{
    public List<SensorNode> Nodes { get; set; } = new List<SensorNode>();
}

public class SensorNode : DeploymentNode
{
    public string MacAddress { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
}