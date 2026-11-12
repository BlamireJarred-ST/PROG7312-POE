using SmartX.Shared.Models;

namespace SmartX.API.DataStructures;

// Self-balancing binary search tree (AVL tree) of timestamped telemetry, keyed on the timestamp.
public sealed class TelemetryAvlTree
{
    private sealed class Node
    {
        public DateTime Key;
        public readonly List<TelemetryRecord> Records = new();
        public Node? Left;
        public Node? Right;
        public int Height = 1;
    }

    private Node? _root;

    // Total telemetry records stored
    public int RecordCount { get; private set; }

    // Number of distinct timestamps (tree nodes)
    public int NodeCount { get; private set; }

    public int Height => HeightOf(_root);

    // Height of a perfectly balanced tree with the same number of nodes
    public int OptimalHeight => NodeCount == 0 ? 0 : (int)Math.Ceiling(Math.Log2(NodeCount + 1));

    public DateTime? OldestTimestamp => MinNode()?.Key;

    public DateTime? NewestTimestamp
    {
        get
        {
            var node = _root;
            if (node == null) return null;
            while (node.Right != null) node = node.Right;
            return node.Key;
        }
    }

    //insert

    public void Insert(TelemetryRecord record)
    {
        _root = Insert(_root, record);
    }

    private Node Insert(Node? node, TelemetryRecord record)
    {
        if (node == null)
        {
            RecordCount++;
            NodeCount++;
            var created = new Node { Key = record.Timestamp };
            created.Records.Add(record);
            return created;
        }

        int comparison = record.Timestamp.CompareTo(node.Key);

        if (comparison < 0)
        {
            node.Left = Insert(node.Left, record);
        }
        else if (comparison > 0)
        {
            node.Right = Insert(node.Right, record);
        }
        else
        {
            // same timestamp: no structural change
            node.Records.Add(record);
            RecordCount++;
            return node;
        }

        return Rebalance(node);
    }

    //remove oldest (keeps memory bounded)

    public void RemoveOldest()
    {
        if (_root != null)
        {
            _root = RemoveMin(_root);
        }
    }

    private Node? RemoveMin(Node node)
    {
        if (node.Left == null)
        {
            RecordCount -= node.Records.Count;
            NodeCount--;
            return node.Right;
        }

        node.Left = RemoveMin(node.Left);
        return Rebalance(node);
    }

    //range query
    public List<TelemetryRecord> Range(DateTime from, DateTime to, out int nodesVisited)
    {
        var results = new List<TelemetryRecord>();
        int visited = 0;

        Collect(_root, from, to, results, ref visited);

        nodesVisited = visited;
        return results;
    }

    private static void Collect(Node? node, DateTime from, DateTime to, List<TelemetryRecord> results, ref int visited)
    {
        if (node == null)
        {
            return;
        }

        visited++;

        // matches can only be in the left subtree if the range starts before this node
        if (from < node.Key)
        {
            Collect(node.Left, from, to, results, ref visited);
        }

        if (node.Key >= from && node.Key <= to)
        {
            results.AddRange(node.Records);
        }

        // ...and in the right subtree only if the range ends after this node
        if (to > node.Key)
        {
            Collect(node.Right, from, to, results, ref visited);
        }
    }

    //AVL balancing

    private static int HeightOf(Node? node) => node?.Height ?? 0;

    private static void UpdateHeight(Node node)
    {
        node.Height = 1 + Math.Max(HeightOf(node.Left), HeightOf(node.Right));
    }

    private static Node Rebalance(Node node)
    {
        UpdateHeight(node);

        int balance = HeightOf(node.Left) - HeightOf(node.Right);

        if (balance > 1)
        {
            // left-right case: rotate the left child first
            if (HeightOf(node.Left!.Left) < HeightOf(node.Left.Right))
            {
                node.Left = RotateLeft(node.Left);
            }

            return RotateRight(node);
        }

        if (balance < -1)
        {
            // right-left case: rotate the right child first
            if (HeightOf(node.Right!.Right) < HeightOf(node.Right.Left))
            {
                node.Right = RotateRight(node.Right);
            }

            return RotateLeft(node);
        }

        return node;
    }

    private static Node RotateRight(Node y)
    {
        Node x = y.Left!;

        y.Left = x.Right;
        x.Right = y;

        UpdateHeight(y);
        UpdateHeight(x);

        return x;
    }

    private static Node RotateLeft(Node x)
    {
        Node y = x.Right!;

        x.Right = y.Left;
        y.Left = x;

        UpdateHeight(x);
        UpdateHeight(y);

        return y;
    }

    private Node? MinNode()
    {
        var node = _root;
        if (node == null) return null;
        while (node.Left != null) node = node.Left;
        return node;
    }
}
