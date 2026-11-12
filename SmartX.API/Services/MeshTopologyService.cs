using SmartX.API.DataStructures;
using SmartX.Shared.Models;

namespace SmartX.API.Services;

// Network Topology and Mesh Routing.
//
// The mesh is modelled as a weighted undirected graph (MeshGraph): vertices are the ESP32 nodes and the gateway,
// edges are the wireless links, and an edge weight is the transmit power cost of that hop.
//  * BFS / DFS find which devices are connected to the gateway and which are down or cut off.
//  * Prim's / Kruskal's algorithms compute the minimum spanning tree: the set of links that connects all reachable
//    nodes with the lowest total power. Telemetry is routed up this tree to the gateway (the central server).
// A node counts as "down" when it was failed manually OR its device is Offline in the Part 2 device registry
// (heartbeat timeout / disconnect report), so Parts 2 and 3 stay connected.
public sealed class MeshTopologyService
{
    public const string GatewayId = "AA:BB:CC:00:00:01";

    private const string Gateway = "Gateway";

    // Nodes added for the mesh on top of the three sensors seeded in Parts 1 and 2 (Program.cs registers them)
    public static IReadOnlyList<Sensor> ExtraSensors { get; } = new List<Sensor>
    {
        new() { MacAddress = "AA:BB:CC:DD:EE:04", Room = "Hydroponics Bay", Zone = "Zone 1", NodeId = "NODE-04", Category = SensorCategory.Environmental },
        new() { MacAddress = "AA:BB:CC:DD:EE:05", Room = "Relay Hub",       Zone = "Zone 1", NodeId = "NODE-05", Category = SensorCategory.Environmental },
        new() { MacAddress = "AA:BB:CC:DD:EE:06", Room = "Boiler Room",     Zone = "Zone 2", NodeId = "NODE-06", Category = SensorCategory.PowerConsumption },
        new() { MacAddress = "AA:BB:CC:DD:EE:07", Room = "Storage Hall",    Zone = "Zone 2", NodeId = "NODE-07", Category = SensorCategory.Environmental },
        new() { MacAddress = "AA:BB:CC:DD:EE:08", Room = "Loading Bay",     Zone = "Zone 2", NodeId = "NODE-08", Category = SensorCategory.Actuator }
    };

    private readonly MeshGraph _graph = new();
    private readonly HashSet<string> _failedNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceRegistry _registry;
    private readonly object _lock = new();

    public MeshTopologyService(DeviceRegistry registry)
    {
        _registry = registry;
        BuildDemoNetwork();
    }

    // ------------------------------------------------------------------ demo network

    private void BuildDemoNetwork()
    {
        const string n01 = "AA:BB:CC:DD:EE:FF";
        const string n02 = "AA:BB:CC:DD:EE:02";
        const string n03 = "AA:BB:CC:DD:EE:03";
        const string n04 = "AA:BB:CC:DD:EE:04";
        const string n05 = "AA:BB:CC:DD:EE:05";
        const string n06 = "AA:BB:CC:DD:EE:06";
        const string n07 = "AA:BB:CC:DD:EE:07";
        const string n08 = "AA:BB:CC:DD:EE:08";

        _graph.AddVertex(new MeshVertex(GatewayId, "GATEWAY", Gateway, "Central Server", 0.50, 0.08));
        _graph.AddVertex(new MeshVertex(n01, "NODE-01", "Sensor", "Server Room", 0.14, 0.30));
        _graph.AddVertex(new MeshVertex(n02, "NODE-02", "Sensor", "Pump Room", 0.38, 0.32));
        _graph.AddVertex(new MeshVertex(n03, "NODE-03", "Sensor", "Valve Bay", 0.26, 0.60));
        _graph.AddVertex(new MeshVertex(n04, "NODE-04", "Sensor", "Hydroponics Bay", 0.62, 0.42));
        _graph.AddVertex(new MeshVertex(n05, "NODE-05", "Sensor", "Relay Hub", 0.86, 0.24));
        _graph.AddVertex(new MeshVertex(n06, "NODE-06", "Sensor", "Boiler Room", 0.10, 0.86));
        _graph.AddVertex(new MeshVertex(n07, "NODE-07", "Sensor", "Storage Hall", 0.64, 0.74));
        _graph.AddVertex(new MeshVertex(n08, "NODE-08", "Sensor", "Loading Bay", 0.88, 0.84));

        // wireless links: (from, to, power cost, signal strength in dBm)
        _graph.AddEdge(GatewayId, n02, 2.0, -48);
        _graph.AddEdge(GatewayId, n01, 4.5, -62);
        _graph.AddEdge(GatewayId, n05, 3.0, -55);
        _graph.AddEdge(n01, n02, 3.0, -58);
        _graph.AddEdge(n01, n03, 2.5, -54);
        _graph.AddEdge(n02, n03, 2.2, -52);
        _graph.AddEdge(n02, n04, 3.5, -60);
        _graph.AddEdge(n03, n06, 3.1, -57);
        _graph.AddEdge(n04, n05, 2.6, -56);
        _graph.AddEdge(n04, n07, 2.0, -50);
        _graph.AddEdge(n05, n07, 6.0, -72);
        _graph.AddEdge(n06, n07, 3.6, -61);
        _graph.AddEdge(n07, n08, 2.4, -53);
        _graph.AddEdge(n06, n08, 5.0, -70);
    }

    // ------------------------------------------------------------------ node status

    // Accepts a MAC address or a node identifier such as NODE-02
    public bool SetNodeStatus(string idOrLabel, bool isUp)
    {
        lock (_lock)
        {
            string? id = Resolve(idOrLabel);

            if (id == null)
            {
                return false;
            }

            if (isUp)
            {
                _failedNodes.Remove(id);
            }
            else
            {
                _failedNodes.Add(id);
            }

            return true;
        }
    }

    // MAC address or label -> vertex id
    private string? Resolve(string? idOrLabel)
    {
        if (string.IsNullOrWhiteSpace(idOrLabel))
        {
            return null;
        }

        string text = idOrLabel.Trim();

        foreach (var vertex in _graph.Vertices)
        {
            if (vertex.Id.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                vertex.Label.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return vertex.Id;
            }
        }

        return null;
    }

    // Snapshot of which nodes are up, evaluated once per request
    private Func<string, bool> BuildUpTest()
    {
        var up = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var vertex in _graph.Vertices)
        {
            bool isUp = !_failedNodes.Contains(vertex.Id);

            if (isUp && vertex.Role != Gateway &&
                _registry.GetSnapshot(vertex.Id) is { Status: DeviceStatus.Offline })
            {
                isUp = false;
            }

            up[vertex.Id] = isUp;
        }

        return id => up.TryGetValue(id, out bool value) && value;
    }

    private string StatusText(MeshVertex vertex)
    {
        if (_failedNodes.Contains(vertex.Id))
        {
            return "Down (manual)";
        }

        if (vertex.Role != Gateway && _registry.GetSnapshot(vertex.Id) is { Status: DeviceStatus.Offline })
        {
            return "Offline (no heartbeat)";
        }

        return "Up";
    }

    // ------------------------------------------------------------------ routing table

    // Routes towards the gateway: the minimum spanning tree rooted at the gateway
    private sealed class RoutingTable
    {
        public List<MeshEdge> MstEdges { get; set; } = new();
        public double TotalCost { get; set; }

        // child -> next hop towards the gateway (null for the gateway itself); only reachable nodes are present
        public Dictionary<string, string?> NextHop { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Hops { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsReachable(string id) => NextHop.ContainsKey(id);

        public List<string> PathToGateway(string id)
        {
            var path = new List<string>();
            string? current = id;

            while (current != null)
            {
                path.Add(current);
                current = NextHop[current];
            }

            return path;
        }
    }

    private RoutingTable BuildRouting(Func<string, bool> up, string algorithm)
    {
        var table = new RoutingTable();

        if (!up(GatewayId))
        {
            return table;
        }

        List<MeshEdge> mst = algorithm == "Kruskal"
            ? MeshAlgorithms.Kruskal(_graph, GatewayId, up)
            : MeshAlgorithms.Prim(_graph, GatewayId, up);

        table.MstEdges = mst;
        table.TotalCost = mst.Sum(e => e.PowerCost);

        // adjacency of the tree itself
        var treeLinks = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in mst)
        {
            if (!treeLinks.TryGetValue(edge.A, out var listA)) treeLinks[edge.A] = listA = new List<string>();
            if (!treeLinks.TryGetValue(edge.B, out var listB)) treeLinks[edge.B] = listB = new List<string>();

            listA.Add(edge.B);
            listB.Add(edge.A);
        }

        // root the tree at the gateway with a breadth-first walk to learn each node's next hop
        table.NextHop[GatewayId] = null;
        table.Hops[GatewayId] = 0;

        var queue = new Queue<string>();
        queue.Enqueue(GatewayId);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();

            if (!treeLinks.TryGetValue(current, out var children)) continue;

            foreach (string child in children)
            {
                if (table.NextHop.ContainsKey(child)) continue;

                table.NextHop[child] = current;
                table.Hops[child] = table.Hops[current] + 1;
                queue.Enqueue(child);
            }
        }

        return table;
    }

    private static string NormaliseMst(string? algorithm) =>
        string.Equals(algorithm, "kruskal", StringComparison.OrdinalIgnoreCase) ? "Kruskal" : "Prim";

    private string LabelOf(string id) => _graph.TryGetVertex(id, out var vertex) ? vertex.Label : id;

    private List<RouteHop> ToHops(IEnumerable<string> path) =>
        path.Select(id => new RouteHop { Id = id, Label = LabelOf(id) }).ToList();

    private double PathCost(List<string> path)
    {
        double total = 0;

        for (int i = 0; i + 1 < path.Count; i++)
        {
            total += _graph.FindEdge(path[i], path[i + 1])?.PowerCost ?? 0;
        }

        return total;
    }

    private static List<string> PathFromSteps(List<MeshAlgorithms.TraversalStep> steps, string target)
    {
        var parents = steps.ToDictionary(s => s.Id, s => s.Parent, StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        if (!parents.ContainsKey(target))
        {
            return path;
        }

        string? current = target;

        while (current != null)
        {
            path.Add(current);
            current = parents[current];
        }

        return path;
    }

    // ------------------------------------------------------------------ queries used by the endpoints

    public TopologySnapshot GetSnapshot(string? mstAlgorithm)
    {
        lock (_lock)
        {
            string algorithm = NormaliseMst(mstAlgorithm);
            var up = BuildUpTest();
            var table = BuildRouting(up, algorithm);
            var mstEdges = new HashSet<MeshEdge>(table.MstEdges);

            var snapshot = new TopologySnapshot
            {
                GatewayId = GatewayId,
                MstAlgorithm = algorithm,
                MstTotalPowerCost = Math.Round(table.TotalCost, 2),
                MstEdgeCount = table.MstEdges.Count,
                ReachableCount = table.NextHop.Count
            };

            foreach (var vertex in _graph.Vertices)
            {
                bool isUp = up(vertex.Id);

                snapshot.Nodes.Add(new MeshNodeInfo
                {
                    Id = vertex.Id,
                    Label = vertex.Label,
                    Role = vertex.Role,
                    Room = vertex.Room,
                    IsUp = isUp,
                    Status = StatusText(vertex),
                    X = vertex.X,
                    Y = vertex.Y,
                    Reachable = table.IsReachable(vertex.Id),
                    HopsToGateway = table.Hops.TryGetValue(vertex.Id, out int hops) ? hops : null,
                    NextHop = table.NextHop.TryGetValue(vertex.Id, out string? next) && next != null ? LabelOf(next) : null
                });

                if (isUp) snapshot.UpCount++;
                else snapshot.DownCount++;
            }

            snapshot.NodeCount = snapshot.Nodes.Count;

            foreach (var edge in _graph.Edges)
            {
                snapshot.Links.Add(new MeshLinkInfo
                {
                    From = edge.A,
                    To = edge.B,
                    PowerCost = edge.PowerCost,
                    SignalDbm = edge.SignalDbm,
                    IsUp = edge.IsUp && up(edge.A) && up(edge.B),
                    InMst = mstEdges.Contains(edge)
                });
            }

            return snapshot;
        }
    }

    // Routing path of one node, found by MAC address or identifier
    public RouteResult? GetRoute(string idOrLabel, string? mstAlgorithm)
    {
        lock (_lock)
        {
            string? id = Resolve(idOrLabel);

            if (id == null || !_graph.TryGetVertex(id, out var vertex))
            {
                return null;
            }

            string algorithm = NormaliseMst(mstAlgorithm);
            var up = BuildUpTest();
            var table = BuildRouting(up, algorithm);

            var result = new RouteResult
            {
                SourceId = vertex.Id,
                SourceLabel = vertex.Label,
                GatewayId = GatewayId,
                MstAlgorithm = algorithm
            };

            if (!up(id))
            {
                result.Message = $"{vertex.Label} is down ({StatusText(vertex)}); it cannot send telemetry.";
                return result;
            }

            if (!table.IsReachable(id))
            {
                result.Message = $"{vertex.Label} is up but has no live path to the gateway (isolated).";
                return result;
            }

            // most power-efficient route: through the minimum spanning tree
            var mstPath = table.PathToGateway(id);
            result.Reachable = true;
            result.MstPath = ToHops(mstPath);
            result.MstHops = mstPath.Count - 1;
            result.MstPowerCost = Math.Round(PathCost(mstPath), 2);

            // fewest-hops route: breadth-first search from the gateway
            var bfsPath = PathFromSteps(MeshAlgorithms.Bfs(_graph, GatewayId, up), id);
            result.ShortestHopPath = ToHops(bfsPath);
            result.ShortestHopCount = Math.Max(0, bfsPath.Count - 1);
            result.ShortestHopPowerCost = Math.Round(PathCost(bfsPath), 2);

            result.Message = $"{vertex.Label} reaches the gateway in {result.MstHops} hop(s) using {result.MstPowerCost} power units.";
            return result;
        }
    }

    // Which devices are connected to a root (the gateway or any node) and which are down or cut off
    public TraversalResult? Traverse(string? rootIdOrLabel, string? algorithm)
    {
        lock (_lock)
        {
            string? rootId = string.IsNullOrWhiteSpace(rootIdOrLabel) ? GatewayId : Resolve(rootIdOrLabel);

            if (rootId == null || !_graph.TryGetVertex(rootId, out var root))
            {
                return null;
            }

            bool useDfs = string.Equals(algorithm, "dfs", StringComparison.OrdinalIgnoreCase);
            var up = BuildUpTest();

            var steps = useDfs
                ? MeshAlgorithms.Dfs(_graph, rootId, up)
                : MeshAlgorithms.Bfs(_graph, rootId, up);

            var result = new TraversalResult
            {
                RootId = root.Id,
                RootLabel = root.Label,
                Algorithm = useDfs ? "DFS" : "BFS"
            };

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in steps)
            {
                visited.Add(step.Id);

                result.Connected.Add(new TraversalEntry
                {
                    Id = step.Id,
                    Label = LabelOf(step.Id),
                    Hops = step.Hops,
                    ParentLabel = step.Parent == null ? null : LabelOf(step.Parent)
                });
            }

            foreach (var vertex in _graph.Vertices)
            {
                if (visited.Contains(vertex.Id)) continue;

                result.Unreachable.Add(new UnreachableEntry
                {
                    Id = vertex.Id,
                    Label = vertex.Label,
                    Reason = up(vertex.Id) ? "Isolated (no live path)" : StatusText(vertex)
                });
            }

            return result;
        }
    }

    // For every failed node: which nodes relied on it as a relay, and where they must hop instead
    public List<FailureAdvice> GetFailureAdvice(string? mstAlgorithm)
    {
        lock (_lock)
        {
            string algorithm = NormaliseMst(mstAlgorithm);
            var up = BuildUpTest();
            var current = BuildRouting(up, algorithm);
            var baseline = BuildRouting(_ => true, algorithm); // the healthy network, for comparison

            var advice = new List<FailureAdvice>();

            foreach (var down in _graph.Vertices.Where(v => !up(v.Id)))
            {
                var item = new FailureAdvice
                {
                    DownNodeId = down.Id,
                    DownNodeLabel = down.Label,
                    Reason = StatusText(down)
                };

                foreach (var node in _graph.Vertices)
                {
                    if (node.Id == down.Id || node.Role == Gateway || !baseline.IsReachable(node.Id))
                    {
                        continue;
                    }

                    var oldPath = baseline.PathToGateway(node.Id);

                    // only nodes whose healthy route went through the failed node are affected
                    if (!oldPath.Contains(down.Id, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var info = new RerouteInfo
                    {
                        NodeId = node.Id,
                        NodeLabel = node.Label,
                        OldPath = ToHops(oldPath),
                        OldPowerCost = Math.Round(PathCost(oldPath), 2)
                    };

                    if (up(node.Id) && current.IsReachable(node.Id))
                    {
                        var newPath = current.PathToGateway(node.Id);

                        info.StillReachable = true;
                        info.NewPath = ToHops(newPath);
                        info.NewPowerCost = Math.Round(PathCost(newPath), 2);
                        info.Advice = $"Hop via {LabelOf(newPath[1])}";
                    }
                    else
                    {
                        info.StillReachable = false;
                        info.Advice = up(node.Id)
                            ? "No alternative path: isolated from the gateway"
                            : "Node is also down";
                    }

                    item.Affected.Add(info);
                }

                advice.Add(item);
            }

            return advice;
        }
    }
}
