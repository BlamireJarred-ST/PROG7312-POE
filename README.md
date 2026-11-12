# PROG7312-POE

## Video Presentation
Part 1 Presentation: https://youtu.be/mrvnyzWNOt4

Part 2 Presentation: https://youtu.be/8whVL1w-LXU

Part 3 Presentation: 

# SmartX

SmartX is a smart sensor monitoring system developed using C#, .NET 10, ASP.NET Core Minimal APIs and WPF.
Sensors/nodes are registered, monitored, controlled and routed through a WPF desktop application and a REST API.

# Project Structure

The solution contains three projects:

- SmartX.Shared – Shared models (sensors, telemetry, device state, commands, topology DTOs) and services
- SmartX.API – ASP.NET Core Minimal API backend: ingestion pipeline, data structures, graph and tree algorithms
- SmartX.Wpf – WPF desktop application: login, navigation menu and the three module views

# Main Features

Part 1 — Sensor Data Ingestion and Telemetry
- Register sensors and validate required details.
- Detect duplicate MAC addresses.
- Retrieve registered sensor information through the API.
- Simulate telemetry for supported sensor readings.
- Process telemetry batches.
- Evaluate telemetry severity and display warning/critical conditions.
- Display system health information.
- Attach sensor-related information and validate deployment structures.

Part 2 — Real-Time Command Stream and History
- Process incoming telemetry through background workers.
- Queue ordinary telemetry in arrival order and prioritise critical packets.
- Maintain current device state and telemetry history.
- Review alerts and suppress duplicate alerts for ongoing error conditions.
- Issue manual override commands.
- Track command history and undo supported commands.
- Search and filter devices and activity.
- Display suggested actions and automated insights.
- Pause and resume telemetry pipeline processing and inspect pipeline status.

  Part 3
- Dashboard that draws the live IoT mesh (nodes and wireless links) and which links form the minimum spanning tree.
- Track the routing path of any node by its MAC address or identifier (for example NODE-04).
- Breadth-first and depth-first traversal: which devices are connected to the gateway, which are down or isolated.
- Failure simulation with re-routing advice: mark a node down and see who must re-route.
- Minimum spanning tree with Prim's or Kruskal's algorithm: the most power-efficient set of links to reach the central server.
- Historical telemetry log for a selected sensor, using an AVL tree for fast time-range queries.

## Requirements
- Windows 10/11
- .NET 10 SDK, and Visual Studio with the ".NET desktop" and "ASP.NET" workloads
- Internet access (login uses Firebase Authentication)
- A test account (email + password) provided with the submission
- Optional: Docker Desktop to run the API in a container

## Run (Visual Studio)
1. Clone the repo 
2. Start the API: `dotnet run --project SmartX.API --launch-profile https`
   (API: https://localhost:7078 - Swagger: https://localhost:7078/swagger)
3. In a second terminal start the app: `dotnet run --project SmartX.Wpf`
   (In Visual Studio you can set Multiple startup projects: SmartX.API and SmartX.Wpf.)
4. Sign in with the test account.

## Run the API in Docker (optional)
1. `docker compose up --build`  (API on http://localhost:8080, Swagger at /swagger)
2. Point the app at it before starting it (PowerShell):
   `$env:SMARTX_API_URL = "http://localhost:8080"; dotnet run --project SmartX.Wpf`

## Login credentials
User@test.com
UserTest123!

## Using the app
- Sensor Data Ingestion and Telemetry: press Start Simulation so telemetry flows.
- Real-Time Command Stream and History: select a device, issue Pump/Relay/Valve commands, press Undo, search and filter devices.
- Network Topology and Mesh Routing.

1. Press Load Demo History to generate 6 hours of readings for every node, live telemetry from the simulation is added as well.
2. Click a node on the graph, or type its MAC address / identifier in Track node and press Track Route. The route to the gateway is highlighted in blue and listed in Routing Path to Gateway, with the most power-efficient route (spanning tree) compared against the fewest-hops route (BFS).
3. Choose Prim or Kruskal: the green links (minimum spanning tree) and the total power cost update. Both give the same total cost.
4. Press Run Traversal (BFS or DFS) to list every device connected to the selected node (or the gateway) and which are down or isolated.
5. Select a node and press Mark Node Down: routes are recalculated, the node turns red and Failure Advice shows which nodes must hop elsewhere. Press Restore Node to bring it back.
6. In Historical Telemetry Log pick a range (last 30 minutes / 1 hour / 6 hours, or a custom date and HH:mm times), optionally a packet type (for example Moisture), and press Query History. The line under the filters shows how many tree nodes the AVL tree had to visit.

- Switching tabs keeps all data; the simulation keeps running.

# Data structures and algorithms (Part 3)

## 1. AVL tree - historical telemetry (`SmartX.API/DataStructures/TelemetryAvlTree.cs`)

**Role.** Every device has its own AVL tree keyed on the reading's timestamp (`TelemetryHistoryIndex` finds the tree by MAC address in a
`Dictionary`). It answers "give me all moisture readings for NODE-04 between 14:00 and 16:00" without scanning every stored reading.

**Why a tree, and why AVL.** A list needs O(n) to filter by time. A binary search tree only follows branches that can contain matches:
O(log n + k) for k results. Telemetry arrives in increasing time order, which turns a plain BST into a linked list (height n), so the tree must balance itself.
An AVL tree rotates after each insert so that the heights of the two subtrees of any node differ by at most one, keeping the height within about 1.44 x log2(n).
Several readings can share one timestamp, so each node holds the list of readings for that instant.

```csharp
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
```

```csharp
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
```

| Operation | Cost |
|---|---|
| Insert | O(log n) |
| Range query | O(log n + k) |
| Remove oldest (memory cap of 50,000 readings per device) | O(log n) |

The API returns `nodesVisited`, `totalNodesInTree`, `treeHeight` and the optimal height with every query (shown under the filters in the WPF app), so the efficiency can be seen directly.
The tree orders results by timestamp, so the log needs no sorting. A packet-type filter (for example `Moisture`) is applied to the matches of the range query.

## 2. Graph - the mesh network (`MeshGraph.cs`)

**Role.** The physical mesh is a weighted undirected graph. Vertices are the ESP32 sensor nodes and the gateway (identified by MAC address),
edges are the wireless links, and an edge weight is the transmit power cost of that hop. It is stored as an **adjacency list**
(`Dictionary<string, List<MeshEdge>>`), so visiting the neighbours of a node costs O(degree) rather than scanning every link in the network.

```csharp
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
```

A node or link that is down is simply skipped by every algorithm, so failures are routed around automatically.

## 3. Breadth-first and depth-first search - connectivity and failures (`MeshAlgorithms.cs`)

**Role.** Identify all devices currently connected to a gateway (or any node), the ones that are down, and the ones that are up but cut off.
BFS uses a `Queue<T>` and visits level by level, so the first time it reaches a node is by the fewest hops. It also gives the fewest-hops route that the dashboard compares with the power-efficient route.
DFS uses a `Stack<T>` (iterative, so a long chain of nodes cannot overflow the call stack) and finds the same connected set, following one branch as far as it goes first.

```csharp
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
```

Cost: O(V + E). **Where to hop when a node is down:** `GetFailureAdvice` builds the routing tree for the healthy network and for the current network.
Every node whose healthy route passed through the failed node is reported with its new route ("Hop via NODE-01") or as isolated when no live path remains.

## 4. Minimum spanning tree - power-efficient routing (`MeshAlgorithms.cs`)

**Role.** Telemetry has to reach the central server through the mesh. The MST connects every reachable node with the smallest possible total transmit power.
Rooted at the gateway, each node's next hop towards the server is its parent in the tree. Both classic algorithms are implemented and give the same total cost.

**Prim's algorithm** grows the tree from the gateway, always adding the cheapest link that reaches a new node. It uses a `PriorityQueue` (min-heap) of candidate links - O(E log V).

```csharp
frontier.Enqueue(edge, edge.PowerCost);                     // candidate links keyed on power cost


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
```

**Kruskal's algorithm** sorts all links by cost and keeps a link only when it joins two separate groups of nodes. A `DisjointSet` (union-find with path compression and union by rank) detects cycles in near-constant time - O(E log E).

```csharp
// keep only the tree connected to the gateway
foreach (var edge in forest)
{
    if (sets.Connected(edge.A, root))
    {
        tree.Add(edge);
    }
}
```

Only up nodes and links are considered, so after a failure the tree is recomputed on the remaining network; a node with no live path is reported as isolated.
A note on the result: the MST minimises the **total** power of the network, so an individual node's route through it can have more hops than its BFS route.
The route panel therefore shows both, to make the trade-off visible.

### Demo network
The API seeds a gateway and eight nodes (NODE-01 to NODE-03 from Parts 1 and 2, plus NODE-04 to NODE-08) with 14 links.
With every node up the minimum spanning tree has 8 links and a total power cost of 19.8 (Prim and Kruskal agree).

# Testing Part 3

Use the WPF app (steps above) or Swagger. The API does not require a token for direct calls; the Firebase login protects the desktop client.
In the examples `API=https://localhost:7078` (add `-k` to `curl` if your certificate is not trusted) or `http://localhost:8080` for Docker.

```bash
curl $API/api/topology?mst=prim                          # nodes, links, which links are in the MST
curl $API/api/topology?mst=kruskal                       # same total power cost
curl $API/api/topology/nodes/NODE-07/route               # route of a node (MAC address also works)
curl "$API/api/topology/traverse?from=GATEWAY&algorithm=bfs"   # connected devices; algorithm=dfs also works
curl -X POST $API/api/topology/nodes/NODE-02/status -H "Content-Type: application/json" -d '{"isUp":false}'
curl $API/api/topology/failures                          # who relied on NODE-02 and where they hop now
curl -X POST $API/api/topology/nodes/NODE-02/status -H "Content-Type: application/json" -d '{"isUp":true}'
curl -X POST "$API/api/history/seed?hours=6"             # generate demo history
curl "$API/api/history/AA:BB:CC:DD:EE:04?type=Moisture&from=2026-01-01T00:00:00Z"   # range query (set from/to to a window)
curl $API/api/history/stats                              # tree size, height and optimal height per device
```

**Expected results**
- *Topology:* 9 nodes, 14 links, 8 MST links, `mstTotalPowerCost` 19.8 for both algorithms.
- *Route of NODE-07:* the spanning-tree route goes GATEWAY - NODE-05 - NODE-04 - NODE-07 (3 hops), while the BFS route is GATEWAY - NODE-05 - NODE-07 (2 hops, higher power cost).
- *Failure of NODE-02:* `/api/topology/failures` lists the nodes that were routed through NODE-02 and a new route for each ("Hop via ..."); the node count in the summary shows 1 down.
- *Isolation:* mark NODE-03, NODE-07 and NODE-08 down; NODE-06 stays up but is reported as `Isolated (no live path)` by the traversal.
- *History:* after seeding, the Moisture readings of NODE-04 show a drop below 20 about 2 to 2.5 hours ago (critical severity); a query for that window returns only those readings and reports how few tree nodes were visited. `history/stats` shows each tree's height next to the optimal height.

# Testing Parts 1 and 2

## Testing the queues, stack, dictionaries and sets
**Simulate a high-priority alert (Queue vs PriorityQueue):**
1. Open the Command Stream tab and press **Pause Pipeline**.
2. Let a few normal readings arrive (the FIFO queue counter rises).
3. Select NODE-02 and press **Simulate Critical Alert** (the Priority queue counter becomes 1).
4. Press **Resume Pipeline** and open the **Pipeline Order** tab.
   Expected: the PRIORITY packet was processed before the STANDARD packets that arrived earlier.

**Undo (Stack):** issue two commands, open **Undo Stack** (newest on top), press **Undo Last Command**.
The device returns to its previous state. Undo with an empty stack shows "Nothing to undo".

**Dictionary / SortedDictionary:** select a device; its state is found by MAC address and its timeline is
drawn in timestamp order even though the critical packet was processed first.

**HashSet (duplicate suppression):** in Swagger call `POST /api/devices/AA:BB:CC:DD:EE:03/disconnect` twice.
The first returns `raised: true`, the second `duplicateSuppressed: true`; `GET /api/alerts` lists the node once.

**Predictive engine:** press **Seed Demo Patterns**, then **Simulate Critical Alert** on NODE-03.
"Pump ON on NODE-02" appears under Suggested Actions with its confidence.

# API summary (Part 3)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/topology?mst=prim\|kruskal` | Mesh graph: nodes, links, MST links and totals |
| GET | `/api/topology/nodes/{id}/route` | Routing path of a node (MAC address or identifier) |
| POST | `/api/topology/nodes/{id}/status` | Fail or restore a node |
| GET | `/api/topology/traverse?from=&algorithm=bfs\|dfs` | Connected devices, down and isolated nodes |
| GET | `/api/topology/failures` | Re-routing advice for failed nodes |
| GET | `/api/history/{mac}?from=&to=&type=&take=` | Time-range query on the AVL tree |
| GET | `/api/history/stats` | Tree statistics per device |
| POST | `/api/history/seed?hours=` | Generate demo history |

