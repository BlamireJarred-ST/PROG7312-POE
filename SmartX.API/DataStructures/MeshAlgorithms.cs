namespace SmartX.API.DataStructures;

// Graph algorithms for the mesh network. Every algorithm only uses nodes for which nodeUp(id) is true and links whose
// IsUp flag is set, so a failed node or link is automatically routed around.
public static class MeshAlgorithms
{
    // One visited vertex: how many hops from the start and which vertex it was reached from
    public sealed record TraversalStep(string Id, int Hops, string? Parent);

    // ------------------------------------------------------------------ Breadth-first search
    // DATA STRUCTURE: Queue<T> (FIFO) + HashSet<T> of visited nodes.
    // Visits nodes level by level, so the first time a node is reached is via the fewest possible hops.
    // Used to find every device connected to a gateway / subnet and the fewest-hop route to it.
    public static List<TraversalStep> Bfs(MeshGraph graph, string start, Func<string, bool> nodeUp)
    {
        var order = new List<TraversalStep>();

        if (!graph.Contains(start) || !nodeUp(start))
        {
            return order;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
        var queue = new Queue<TraversalStep>();
        queue.Enqueue(new TraversalStep(start, 0, null));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            order.Add(current);

            foreach (var edge in graph.Neighbours(current.Id))
            {
                if (!edge.IsUp) continue;

                string next = edge.Other(current.Id);

                if (!nodeUp(next) || !visited.Add(next)) continue;

                queue.Enqueue(new TraversalStep(next, current.Hops + 1, current.Id));
            }
        }

        return order;
    }

    // ------------------------------------------------------------------ Depth-first search
    // DATA STRUCTURE: Stack<T> (LIFO) + HashSet<T>. Iterative, so a long chain of nodes cannot overflow the call stack.
    // Follows one branch as far as it goes before backtracking. Finds the same connected set as BFS, but the hop
    // counts are along the DFS tree and are not necessarily the shortest.
    public static List<TraversalStep> Dfs(MeshGraph graph, string start, Func<string, bool> nodeUp)
    {
        var order = new List<TraversalStep>();

        if (!graph.Contains(start) || !nodeUp(start))
        {
            return order;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<TraversalStep>();
        stack.Push(new TraversalStep(start, 0, null));

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            if (!visited.Add(current.Id)) continue;

            order.Add(current);

            foreach (var edge in graph.Neighbours(current.Id))
            {
                if (!edge.IsUp) continue;

                string next = edge.Other(current.Id);

                if (!nodeUp(next) || visited.Contains(next)) continue;

                stack.Push(new TraversalStep(next, current.Hops + 1, current.Id));
            }
        }

        return order;
    }

    // ------------------------------------------------------------------ Prim's algorithm
    // DATA STRUCTURE: PriorityQueue<edge, powerCost> (a min-heap) + HashSet<T> of nodes already in the tree.
    // Grows a tree outwards from the root (the gateway): always add the cheapest link that reaches a new node.
    // The result is the minimum spanning tree of the root's connected component: the set of links that connects
    // every reachable node with the smallest possible total transmit power.
    public static List<MeshEdge> Prim(MeshGraph graph, string root, Func<string, bool> nodeUp)
    {
        var tree = new List<MeshEdge>();

        if (!graph.Contains(root) || !nodeUp(root))
        {
            return tree;
        }

        var inTree = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var frontier = new PriorityQueue<MeshEdge, double>();

        void AddFrontier(string id)
        {
            foreach (var edge in graph.Neighbours(id))
            {
                if (edge.IsUp && nodeUp(edge.Other(id)))
                {
                    frontier.Enqueue(edge, edge.PowerCost);
                }
            }
        }

        AddFrontier(root);

        while (frontier.TryDequeue(out var cheapest, out _))
        {
            bool aInTree = inTree.Contains(cheapest.A);
            bool bInTree = inTree.Contains(cheapest.B);

            // both ends already connected: this link would create a cycle
            if (aInTree && bInTree) continue;

            string newNode = aInTree ? cheapest.B : cheapest.A;

            inTree.Add(newNode);
            tree.Add(cheapest);
            AddFrontier(newNode);
        }

        return tree;
    }

    // ------------------------------------------------------------------ Kruskal's algorithm
    // DATA STRUCTURE: edge list sorted by weight + DisjointSet (union-find).
    // Considers links from cheapest to most expensive and keeps a link only if it joins two separate groups of nodes.
    // Returns the tree that contains the root (gateway); it has the same total cost as Prim's result.
    public static List<MeshEdge> Kruskal(MeshGraph graph, string root, Func<string, bool> nodeUp)
    {
        var tree = new List<MeshEdge>();

        if (!graph.Contains(root) || !nodeUp(root))
        {
            return tree;
        }

        var sets = new DisjointSet(graph.Vertices.Select(v => v.Id));

        var usable = graph.Edges
            .Where(e => e.IsUp && nodeUp(e.A) && nodeUp(e.B))
            .OrderBy(e => e.PowerCost);

        var forest = new List<MeshEdge>();

        foreach (var edge in usable)
        {
            if (sets.Union(edge.A, edge.B))
            {
                forest.Add(edge);
            }
        }

        // keep only the tree connected to the gateway
        foreach (var edge in forest)
        {
            if (sets.Connected(edge.A, root))
            {
                tree.Add(edge);
            }
        }

        return tree;
    }
}
