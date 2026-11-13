namespace SmartX.Shared.Models;

// One vertex of the mesh graph 
public class MeshNodeInfo
{
    // MAC address - the unique identifier of the node
    public string Id { get; set; } = string.Empty;
    // Friendly identifier, e.g. NODE-02
    public string Label { get; set; } = string.Empty;
    // Gateway | Sensor
    public string Role { get; set; } = "Sensor";
    public string Room { get; set; } = string.Empty;
    // False when the node is manually failed or its device is offline
    public bool IsUp { get; set; } = true;
    // Up | Down (manual) | Offline (no heartbeat)
    public string Status { get; set; } = "Up";
    // Layout position (0..1) used by the dashboard canvas
    public double X { get; set; }
    public double Y { get; set; }
    // True when a live route to the gateway exists
    public bool Reachable { get; set; }
    public int? HopsToGateway { get; set; }
    // Next hop towards the gateway along the minimum spanning tree
    public string? NextHop { get; set; }
}

// One edge of the mesh graph (a wireless link)
public class MeshLinkInfo
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    // Transmit energy cost of the hop (edge weight used by Prim / Kruskal)
    public double PowerCost { get; set; }
    public int SignalDbm { get; set; }
    // False when the link or either endpoint is down
    public bool IsUp { get; set; } = true;
    // True when the link belongs to the minimum spanning tree
    public bool InMst { get; set; }
}

public class TopologySnapshot
{
    public string GatewayId { get; set; } = string.Empty;
    public string MstAlgorithm { get; set; } = "Prim";
    public double MstTotalPowerCost { get; set; }
    public int MstEdgeCount { get; set; }
    public int NodeCount { get; set; }
    public int UpCount { get; set; }
    public int DownCount { get; set; }
    public int ReachableCount { get; set; }
    public List<MeshNodeInfo> Nodes { get; set; } = new();
    public List<MeshLinkInfo> Links { get; set; } = new();
}

public class RouteHop
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

// Routing path of one node (found by MAC address or identifier)
public class RouteResult
{
    public string SourceId { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
    public string GatewayId { get; set; } = string.Empty;
    public bool Reachable { get; set; }
    public string Message { get; set; } = string.Empty;

    // Most power-efficient route: path through the minimum spanning tree
    public string MstAlgorithm { get; set; } = "Prim";
    public List<RouteHop> MstPath { get; set; } = new();
    public int MstHops { get; set; }
    public double MstPowerCost { get; set; }

    // Fewest-hops route found by breadth-first search (for comparison)
    public List<RouteHop> ShortestHopPath { get; set; } = new();
    public int ShortestHopCount { get; set; }
    public double ShortestHopPowerCost { get; set; }
}

public class TraversalEntry
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Hops { get; set; }
    public string? ParentLabel { get; set; }
}

public class UnreachableEntry
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    // Down / Offline / Isolated 
    public string Reason { get; set; } = string.Empty;
}

// Result of a BFS / DFS: which devices are connected and which are down or cut off
public class TraversalResult
{
    public string RootId { get; set; } = string.Empty;
    public string RootLabel { get; set; } = string.Empty;
    public string Algorithm { get; set; } = "BFS";
    // In the order the algorithm visited them
    public List<TraversalEntry> Connected { get; set; } = new();
    public List<UnreachableEntry> Unreachable { get; set; } = new();
}

public class RerouteInfo
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeLabel { get; set; } = string.Empty;
    public bool StillReachable { get; set; }
    public List<RouteHop> OldPath { get; set; } = new();
    public List<RouteHop> NewPath { get; set; } = new();
    public double OldPowerCost { get; set; }
    public double NewPowerCost { get; set; }
    // e.g. "Hop via NODE-01"
    public string Advice { get; set; } = string.Empty;
}

// For one failed node: who used it as a relay and where they must hop instead
public class FailureAdvice
{
    public string DownNodeId { get; set; } = string.Empty;
    public string DownNodeLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<RerouteInfo> Affected { get; set; } = new();
}

public class NodeStatusRequest
{
    public bool IsUp { get; set; } = true;
}

// Result of a timestamp range query on the AVL tree
public class RangeQueryResult
{
    public string DeviceId { get; set; } = string.Empty;
    public string? PacketType { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }
    public int TotalMatches { get; set; }
    public int Returned { get; set; }
    // Size of the tree that was searched
    public int TotalRecordsInTree { get; set; }
    public int TotalNodesInTree { get; set; }
    // How many tree nodes the query had to look at (vs scanning every record)
    public int NodesVisited { get; set; }
    public int TreeHeight { get; set; }
    public int OptimalHeight { get; set; }
    public double ElapsedMicroseconds { get; set; }
    // Ascending timestamp order
    public List<TelemetryRecord> Records { get; set; } = new();
}

public class TreeStatsEntry
{
    public string DeviceId { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int NodeCount { get; set; }
    public int TreeHeight { get; set; }
    public int OptimalHeight { get; set; }
    public DateTime? OldestUtc { get; set; }
    public DateTime? NewestUtc { get; set; }
}
