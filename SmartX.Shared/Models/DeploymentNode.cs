namespace SmartX.Shared.Models;

public abstract class DeploymentNode
{
    // identifier for the deployment node
    public string Name { get; set; } = string.Empty;
    // identifier for the deployment node
    public bool IsActive { get; set; }
    // identifier for the deployment node
    public DeploymentNode? Parent { get; set; }
}

//a facility can have mulitple zones
public class Facility : DeploymentNode
{
    //collection of zones in the facility
    public List<Zone> Zones { get; set; } = new List<Zone>();
}

// a zone can have multiple subzones
public class Zone : DeploymentNode
{
    // collection of subzones in the zone
    public List<SubZone> SubZones { get; set; } = new List<SubZone>();
}

// a subzone can have multiple sensor nodes
public class SubZone : DeploymentNode
{
    // collection of sensor nodes in the subzone
    public List<SensorNode> Nodes { get; set; } = new List<SensorNode>();
}

// a sensor node can have multiple sensors
public class SensorNode : DeploymentNode
{
    // collection of sensors in the sensor node
    public string MacAddress { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
}