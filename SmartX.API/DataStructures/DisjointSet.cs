namespace SmartX.API.DataStructures;

// Union-Find (disjoint set) used by Kruskal's algorithm to detect cycles in near-constant time.
// Path compression + union by rank.
public sealed class DisjointSet
{
    private readonly Dictionary<string, string> _parent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _rank = new(StringComparer.OrdinalIgnoreCase);

    public DisjointSet(IEnumerable<string> items)
    {
        foreach (string item in items)
        {
            _parent[item] = item;
            _rank[item] = 0;
        }
    }

    public string Find(string item)
    {
        string root = item;

        while (!string.Equals(_parent[root], root, StringComparison.OrdinalIgnoreCase))
        {
            root = _parent[root];
        }

        // path compression
        while (!string.Equals(_parent[item], root, StringComparison.OrdinalIgnoreCase))
        {
            string next = _parent[item];
            _parent[item] = root;
            item = next;
        }

        return root;
    }

    public bool Connected(string a, string b) =>
        string.Equals(Find(a), Find(b), StringComparison.OrdinalIgnoreCase);

    // Joins the sets of a and b. Returns false if they were already in the same set (the edge would form a cycle).
    public bool Union(string a, string b)
    {
        string rootA = Find(a);
        string rootB = Find(b);

        if (string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_rank[rootA] < _rank[rootB])
        {
            _parent[rootA] = rootB;
        }
        else if (_rank[rootA] > _rank[rootB])
        {
            _parent[rootB] = rootA;
        }
        else
        {
            _parent[rootB] = rootA;
            _rank[rootA]++;
        }

        return true;
    }
}
