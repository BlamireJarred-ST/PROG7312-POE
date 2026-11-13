namespace SmartX.API.DataStructures;

// A node of the mesh network (an ESP32 sensor node or the gateway)
public sealed record MeshVertex(string Id, string Label, string Role, string Room, double X, double Y);

// A wireless link between two nodes (undirected, weighted)
public sealed class MeshEdge
{
    public MeshEdge(string a, string b, double powerCost, int signalDbm)
    {
        A = a;
        B = b;
        PowerCost = powerCost;
        SignalDbm = signalDbm;
    }

    public string A { get; }
    public string B { get; }

    // Edge weight: transmit energy cost of one hop (used by Prim's and Kruskal's algorithms)
    public double PowerCost { get; }

    // Received signal strength, shown on the dashboard
    public int SignalDbm { get; }

    // Links can be failed individually
    public bool IsUp { get; set; } = true;

    // The endpoint that is not 'id'
    public string Other(string id) => string.Equals(id, A, StringComparison.OrdinalIgnoreCase) ? B : A;
}

// The physical IoT mesh modelled as an undirected weighted graph.
public sealed class MeshGraph
{
    private readonly Dictionary<string, MeshVertex> _vertices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MeshEdge>> _adjacency = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MeshEdge> _edges = new();

    public IReadOnlyCollection<MeshVertex> Vertices => _vertices.Values;

    public IReadOnlyList<MeshEdge> Edges => _edges;

    public bool Contains(string id) => _vertices.ContainsKey(id);

    public bool TryGetVertex(string id, out MeshVertex vertex)
    {
        if (_vertices.TryGetValue(id, out var found))
        {
            vertex = found;
            return true;
        }

        vertex = null!;
        return false;
    }

    public void AddVertex(MeshVertex vertex)
    {
        if (_vertices.TryAdd(vertex.Id, vertex))
        {
            _adjacency[vertex.Id] = new List<MeshEdge>();
        }
    }

    public MeshEdge AddEdge(string a, string b, double powerCost, int signalDbm)
    {
        if (!_vertices.ContainsKey(a) || !_vertices.ContainsKey(b))
        {
            throw new ArgumentException("Both ends of an edge must be existing vertices.");
        }

        var edge = new MeshEdge(a, b, powerCost, signalDbm);

        _edges.Add(edge);
        _adjacency[a].Add(edge);
        _adjacency[b].Add(edge);

        return edge;
    }

    public IReadOnlyList<MeshEdge> Neighbours(string id) =>
        _adjacency.TryGetValue(id, out var list) ? list : Array.Empty<MeshEdge>();

    public MeshEdge? FindEdge(string a, string b)
    {
        foreach (var edge in Neighbours(a))
        {
            if (string.Equals(edge.Other(a), b, StringComparison.OrdinalIgnoreCase))
            {
                return edge;
            }
        }

        return null;
    }
}
