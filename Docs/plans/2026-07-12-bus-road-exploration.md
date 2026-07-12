# Bus Road-Exploration System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the `school-bus` autonomously drive the road network, covering every road, on a graph-based foundation an agentic fleet brain can later extend.

**Architecture:** An editor tool auto-builds a `RoadGraph` (boundary nodes + intra-tile edges with polylines) from the road tiles. A kinematic `BusPathFollower` drives the bus along any waypoint list. A swappable `IBusController` decides routes — v1 `ExploreAllController` covers all edges; the future agent implements the same interface. Buildings register as `BusStop`s bound to their nearest node.

**Tech Stack:** Unity 6000.5.3f1, Built-in Render Pipeline, C#, Unity Test Framework (NUnit edit-mode), executed via Unity MCP.

## Global Constraints

- Unity version: **6000.5.3f1**. Render pipeline: **Built-in** (do not introduce URP/HDRP shaders).
- Grid cell size: **~27.85 units** (confirm exact value in Task 3; use the `RoadGraphConfig.CellSize` constant, never a hard-coded literal in logic).
- Namespace for all scripts: **`BusSystem`** (editor code `BusSystem.Editor`).
- Runtime scripts under `Assets/Scripts/BusSystem/`; editor scripts under `Assets/Scripts/BusSystem/Editor/`; tests under `Assets/Tests/EditMode/`.
- Movement is **kinematic** (Transform-based). No physics/NavMesh/A* in v1.
- Node-merge proximity tolerance: **CellSize * 0.4**.
- Commit steps assume git was initialized in Task 0. If git is skipped, skip the commit steps.

## File Structure

**Runtime — `Assets/Scripts/BusSystem/`**
- `BusSystem.asmdef` — runtime assembly definition (namespace BusSystem).
- `RoadGraphConfig.cs` — static tunables (CellSize, MergeToleranceFactor).
- `RoadGraphData.cs` — `[Serializable]` `RoadNode`, `RoadEdge` structs/classes.
- `RoadGraph.cs` — MonoBehaviour: holds nodes/edges, adjacency, `NearestNode`, `GetNeighborEdges`, `OnDrawGizmos`.
- `BusStop.cs` — MonoBehaviour marker: `StopId`, `NearestNodeIndex`.
- `IBusController.cs` — controller interface.
- `ExploreAllController.cs` — plain C# class implementing `IBusController` (unvisited-edge-preferring walk).
- `BusPathFollower.cs` — MonoBehaviour: kinematic waypoint follower with testable `Step(dt)`.
- `BusAgent.cs` — MonoBehaviour on the bus: wires graph + controller + follower, loops routes.

**Editor — `Assets/Scripts/BusSystem/Editor/`**
- `BusSystem.Editor.asmdef` — editor assembly (references BusSystem).
- `RoadGraphBuilder.cs` — menu tool: scans `Roads`, emits connection points, merges nodes, builds edge polylines, binds `BusStop`s.

**Tests — `Assets/Tests/EditMode/`**
- `BusSystem.Tests.asmdef` — edit-mode test assembly (references BusSystem, nunit, UnityEngine.TestRunner).
- `RoadGraphTests.cs`
- `ExploreAllControllerTests.cs`
- `BusPathFollowerTests.cs`

---

### Task 0 (optional): Initialize git

**Files:** Create `.gitignore` at project root `D:/unity/projects/My project/.gitignore`.

- [ ] **Step 1: Add Unity .gitignore**

Create `.gitignore` with the standard Unity entries:
```
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]serSettings/
*.csproj
*.sln
.vscode/
```

- [ ] **Step 2: Init and first commit**
```bash
cd "D:/unity/projects/My project"
git init
git add .gitignore Assets ProjectSettings Packages Docs
git commit -m "chore: initialize git for Unity project"
```
Expected: repo created, initial commit succeeds.

---

### Task 1: Hierarchy grouping

**Files:** None (scene edit via MCP `Unity_ManageGameObject`).

**Interfaces:**
- Produces: root parents `Roads`, `Buildings`, `Vehicles` containing the tiles / apartments / bus respectively.

- [ ] **Step 1: Create the three empty parents at origin**

Via MCP, create empty GameObjects `Roads`, `Buildings`, `Vehicles` at position (0,0,0).

- [ ] **Step 2: Reparent objects**

- All `straightRoad*`, `curvedRoad*`, `intersectionRoad*` → child of `Roads`.
- All `cb-apartment-A*` → child of `Buildings`.
- `school-bus` → child of `Vehicles`.
Use `Unity_ManageGameObject action=modify parent=<name>` per object (world position preserved).

- [ ] **Step 3: Verify**

Run `Unity_ManageScene action=GetHierarchy depth=1`.
Expected: root shows `Main Camera`, `Directional Light`, `Global Volume`, `Roads` (~30 children), `Buildings` (4 children), `Vehicles` (1 child). No road/building/bus objects left at root.

- [ ] **Step 4: Commit**
```bash
git add ProjectSettings Assets
git commit -m "chore: group roads, buildings, vehicles under parents"
```

---

### Task 2: RoadGraph data model, config, queries + scaffolding

**Files:**
- Create: `Assets/Scripts/BusSystem/BusSystem.asmdef`
- Create: `Assets/Scripts/BusSystem/RoadGraphConfig.cs`
- Create: `Assets/Scripts/BusSystem/RoadGraphData.cs`
- Create: `Assets/Scripts/BusSystem/RoadGraph.cs`
- Create: `Assets/Tests/EditMode/BusSystem.Tests.asmdef`
- Test: `Assets/Tests/EditMode/RoadGraphTests.cs`

**Interfaces:**
- Produces:
  - `BusSystem.RoadGraphConfig.CellSize` (float), `MergeToleranceFactor` (float), `MergeTolerance` (float => CellSize*factor).
  - `RoadNode { int Id; Vector3 Position; }`
  - `RoadEdge { int Id; int NodeA; int NodeB; List<Vector3> Polyline; bool Visited; }`
  - `RoadGraph : MonoBehaviour` with `List<RoadNode> Nodes; List<RoadEdge> Edges;` and methods:
    - `int NearestNode(Vector3 worldPos)`
    - `IEnumerable<RoadEdge> GetNeighborEdges(int nodeId)`
    - `int AddOrMergeNode(Vector3 pos)` (returns existing node id if within `MergeTolerance`, else adds).

- [ ] **Step 1: Create runtime asmdef**

`BusSystem.asmdef`:
```json
{ "name": "BusSystem", "rootNamespace": "BusSystem", "references": [], "includePlatforms": [], "autoReferenced": true }
```

- [ ] **Step 2: Create test asmdef**

`BusSystem.Tests.asmdef`:
```json
{
  "name": "BusSystem.Tests",
  "rootNamespace": "BusSystem.Tests",
  "references": ["BusSystem", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
  "includePlatforms": ["Editor"],
  "optionalUnityReferences": ["TestAssemblies"],
  "autoReferenced": false
}
```

- [ ] **Step 3: Write config + data model**

`RoadGraphConfig.cs`:
```csharp
namespace BusSystem
{
    public static class RoadGraphConfig
    {
        // Confirmed/adjusted in Task 3.
        public const float CellSize = 27.85f;
        public const float MergeToleranceFactor = 0.4f;
        public static float MergeTolerance => CellSize * MergeToleranceFactor;
    }
}
```

`RoadGraphData.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    [Serializable]
    public class RoadNode
    {
        public int Id;
        public Vector3 Position;
    }

    [Serializable]
    public class RoadEdge
    {
        public int Id;
        public int NodeA;
        public int NodeB;
        public List<Vector3> Polyline = new List<Vector3>();
        [NonSerialized] public bool Visited;
    }
}
```

- [ ] **Step 4: Write the failing test**

`RoadGraphTests.cs`:
```csharp
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BusSystem;

public class RoadGraphTests
{
    private RoadGraph NewGraph()
    {
        var go = new GameObject("g");
        return go.AddComponent<RoadGraph>();
    }

    [Test]
    public void AddOrMergeNode_MergesWithinTolerance()
    {
        var g = NewGraph();
        int a = g.AddOrMergeNode(new Vector3(0, 0, 0));
        int b = g.AddOrMergeNode(new Vector3(RoadGraphConfig.MergeTolerance * 0.5f, 0, 0));
        int c = g.AddOrMergeNode(new Vector3(100, 0, 0));
        Assert.AreEqual(a, b, "close points should merge to one node");
        Assert.AreNotEqual(a, c, "far point is a new node");
        Assert.AreEqual(2, g.Nodes.Count);
        Object.DestroyImmediate(g.gameObject);
    }

    [Test]
    public void NearestNode_ReturnsClosest()
    {
        var g = NewGraph();
        g.AddOrMergeNode(new Vector3(0, 0, 0));
        g.AddOrMergeNode(new Vector3(50, 0, 0));
        Assert.AreEqual(1, g.NearestNode(new Vector3(48, 0, 0)));
        Object.DestroyImmediate(g.gameObject);
    }

    [Test]
    public void GetNeighborEdges_ReturnsEdgesTouchingNode()
    {
        var g = NewGraph();
        int n0 = g.AddOrMergeNode(Vector3.zero);
        int n1 = g.AddOrMergeNode(new Vector3(50, 0, 0));
        int n2 = g.AddOrMergeNode(new Vector3(0, 0, 50));
        g.Edges.Add(new RoadEdge { Id = 0, NodeA = n0, NodeB = n1 });
        g.Edges.Add(new RoadEdge { Id = 1, NodeA = n0, NodeB = n2 });
        var neighbors = g.GetNeighborEdges(n0).Select(e => e.Id).OrderBy(x => x).ToArray();
        Assert.AreEqual(new[] { 0, 1 }, neighbors);
        Object.DestroyImmediate(g.gameObject);
    }
}
```

- [ ] **Step 5: Run test, verify it fails**

Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All (or via MCP menu trigger).
Expected: FAIL — `RoadGraph` has no `AddOrMergeNode`/`NearestNode`/`GetNeighborEdges`.

- [ ] **Step 6: Implement RoadGraph**

`RoadGraph.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    public class RoadGraph : MonoBehaviour
    {
        public List<RoadNode> Nodes = new List<RoadNode>();
        public List<RoadEdge> Edges = new List<RoadEdge>();

        public int AddOrMergeNode(Vector3 pos)
        {
            float tol = RoadGraphConfig.MergeTolerance;
            for (int i = 0; i < Nodes.Count; i++)
                if ((Nodes[i].Position - pos).sqrMagnitude <= tol * tol)
                    return Nodes[i].Id;
            var node = new RoadNode { Id = Nodes.Count, Position = pos };
            Nodes.Add(node);
            return node.Id;
        }

        public int NearestNode(Vector3 worldPos)
        {
            int best = -1; float bestSq = float.MaxValue;
            for (int i = 0; i < Nodes.Count; i++)
            {
                float d = (Nodes[i].Position - worldPos).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = Nodes[i].Id; }
            }
            return best;
        }

        public IEnumerable<RoadEdge> GetNeighborEdges(int nodeId)
        {
            foreach (var e in Edges)
                if (e.NodeA == nodeId || e.NodeB == nodeId)
                    yield return e;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            foreach (var n in Nodes) Gizmos.DrawSphere(n.Position, 1.2f);
            Gizmos.color = Color.yellow;
            foreach (var e in Edges)
                for (int i = 0; i + 1 < e.Polyline.Count; i++)
                    Gizmos.DrawLine(e.Polyline[i], e.Polyline[i + 1]);
        }
    }
}
```

- [ ] **Step 7: Run tests, verify pass**

Expected: all 3 RoadGraphTests PASS.

- [ ] **Step 8: Commit**
```bash
git add Assets/Scripts/BusSystem Assets/Tests
git commit -m "feat: RoadGraph data model, config, queries + tests"
```

---

### Task 3: Topology investigation (confirm connection rules)

**Files:** None (MCP investigation). Output: verified constants written into `RoadGraphConfig.cs` and the rule table in `RoadGraphBuilder` (Task 4).

**Interfaces:**
- Produces (documented facts for Task 4):
  - Exact `CellSize`.
  - Straight tile: road runs along local **±forward (Z)**? Confirm.
  - Curve tile: which two local sides it links and its handedness.
  - Intersection tile: number of arms (4-way vs T) and their local directions.

- [ ] **Step 1: Dump all road tile transforms**

Via MCP, read Transform (position, eulerAngles) for every child of `Roads`, plus the mesh local bounds of one `straightRoad`, `curvedRoad`, `intersectionRoad` (`Unity_ManageGameObject get_component MeshFilter`/renderer bounds).

- [ ] **Step 2: Derive CellSize + axes**

Compute median spacing between adjacent tile centers along X and Z → `CellSize`. Confirm straight's long bounds axis (road length) = local Z. Note curve's `right`/`forward` vectors vs where its neighbors sit to fix handedness. Note intersection neighbor pattern.

- [ ] **Step 3: Record findings**

Update `RoadGraphConfig.CellSize` to the measured value. Write the confirmed rule table as a comment block at the top of `RoadGraphBuilder.cs` (Task 4). No test; this task de-risks Task 4.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/BusSystem/RoadGraphConfig.cs
git commit -m "chore: confirm road grid cell size + connection rules"
```

---

### Task 4: RoadGraphBuilder (editor tool) + gizmo verification

**Files:**
- Create: `Assets/Scripts/BusSystem/Editor/BusSystem.Editor.asmdef`
- Create: `Assets/Scripts/BusSystem/Editor/RoadGraphBuilder.cs`

**Interfaces:**
- Consumes: `RoadGraph.AddOrMergeNode`, `RoadGraph.Edges`, `RoadGraphConfig`.
- Produces: menu **`Bus System/Build Road Graph`** that fills a `RoadGraph` component (creates a `RoadGraph` GameObject if absent) with nodes + edge polylines. Curve edges use a quadratic bezier through the tile center; intersection tiles add a center node with a spoke edge per present arm.

- [ ] **Step 1: Create editor asmdef**

`BusSystem.Editor.asmdef`:
```json
{ "name": "BusSystem.Editor", "rootNamespace": "BusSystem.Editor", "references": ["BusSystem"], "includePlatforms": ["Editor"], "autoReferenced": true }
```

- [ ] **Step 2: Implement the builder**

`RoadGraphBuilder.cs` (adjust the three `*Directions` per Task 3 findings):
```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using BusSystem;

namespace BusSystem.Editor
{
    // Connection rules (confirmed in Task 3):
    //  straight     : links local +Z and -Z (road runs along local Z).
    //  curve        : links local +Z (forward) and +X (right)  [flip if Task 3 says -X].
    //  intersection : links +Z, -Z, +X, -X (4-way); arms with no neighbor are dropped.
    public static class RoadGraphBuilder
    {
        const float Half = 0.5f;

        [MenuItem("Bus System/Build Road Graph")]
        public static void Build()
        {
            var roadsRoot = GameObject.Find("Roads");
            if (roadsRoot == null) { Debug.LogError("No 'Roads' object found."); return; }

            var graphGo = GameObject.Find("RoadGraph") ?? new GameObject("RoadGraph");
            var graph = graphGo.GetComponent<RoadGraph>() ?? graphGo.AddComponent<RoadGraph>();
            graph.Nodes.Clear(); graph.Edges.Clear();

            float cell = RoadGraphConfig.CellSize;
            int edgeId = 0;

            foreach (Transform tile in roadsRoot.transform)
            {
                string n = tile.name.ToLower();
                Vector3 c = tile.position;
                Vector3 fwd = tile.forward * (cell * Half);
                Vector3 right = tile.right * (cell * Half);

                if (n.Contains("straight"))
                {
                    int a = graph.AddOrMergeNode(c + fwd);
                    int b = graph.AddOrMergeNode(c - fwd);
                    AddEdge(graph, ref edgeId, a, b, new List<Vector3> {
                        graph.Nodes[a].Position, c, graph.Nodes[b].Position });
                }
                else if (n.Contains("curved"))
                {
                    Vector3 p1 = c + fwd, p2 = c + right;   // flip 'right' sign if Task 3 requires
                    int a = graph.AddOrMergeNode(p1);
                    int b = graph.AddOrMergeNode(p2);
                    AddEdge(graph, ref edgeId, a, b, Bezier(p1, c, p2, 6));
                }
                else if (n.Contains("intersection"))
                {
                    int center = graph.AddOrMergeNode(c);
                    foreach (var dir in new[] { fwd, -fwd, right, -right })
                    {
                        Vector3 arm = c + dir;
                        // Only keep an arm if a neighbouring tile endpoint exists there.
                        if (!HasNeighbor(roadsRoot.transform, tile, arm, cell)) continue;
                        int a = graph.AddOrMergeNode(arm);
                        AddEdge(graph, ref edgeId, center, a, new List<Vector3> {
                            graph.Nodes[center].Position, graph.Nodes[a].Position });
                    }
                }
            }

            EditorUtility.SetDirty(graph);
            Debug.Log($"[RoadGraph] Built {graph.Nodes.Count} nodes, {graph.Edges.Count} edges.");
        }

        static bool HasNeighbor(Transform roads, Transform self, Vector3 armWorld, float cell)
        {
            float tol = cell * 0.6f;
            foreach (Transform t in roads)
            {
                if (t == self) continue;
                if ((t.position - armWorld).magnitude <= tol) return true;
            }
            return false;
        }

        static void AddEdge(RoadGraph g, ref int id, int a, int b, List<Vector3> poly)
        {
            if (a == b) return;
            g.Edges.Add(new RoadEdge { Id = id++, NodeA = a, NodeB = b, Polyline = poly });
        }

        static List<Vector3> Bezier(Vector3 p0, Vector3 ctrl, Vector3 p1, int segs)
        {
            var pts = new List<Vector3>();
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                pts.Add(Mathf.Pow(1 - t, 2) * p0 + 2 * (1 - t) * t * ctrl + t * t * p1);
            }
            return pts;
        }
    }
}
```

- [ ] **Step 3: Build and verify via gizmos**

Run menu `Bus System/Build Road Graph` (via MCP `Unity_ManageMenuItem`/`RunCommand`). Then capture the Scene view top-down (`Unity_SceneView_CaptureMultiAngleSceneView`).
Expected: cyan node spheres sit at tile boundaries/intersections; yellow edge lines trace the streets with curves following the bends and no phantom cross-links. Node/edge counts logged (~ matches tile count). If topology is wrong, adjust the `curve`/`intersection` direction rules and rebuild — **do not proceed until the overlay matches the streets.**

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/BusSystem/Editor
git commit -m "feat: RoadGraphBuilder editor tool + gizmo-verified graph"
```

---

### Task 5: BusStop registration

**Files:**
- Create: `Assets/Scripts/BusSystem/BusStop.cs`
- Modify: `Assets/Scripts/BusSystem/Editor/RoadGraphBuilder.cs` (append stop binding after graph build)

**Interfaces:**
- Consumes: `RoadGraph.NearestNode`.
- Produces: `BusStop { int StopId; int NearestNodeIndex; }` on each building; builder binds them.

- [ ] **Step 1: Write BusStop**

`BusStop.cs`:
```csharp
using UnityEngine;

namespace BusSystem
{
    public class BusStop : MonoBehaviour
    {
        public int StopId = -1;
        public int NearestNodeIndex = -1;
    }
}
```

- [ ] **Step 2: Bind stops in the builder**

Append to `RoadGraphBuilder.Build()` before `SetDirty`:
```csharp
var buildingsRoot = GameObject.Find("Buildings");
if (buildingsRoot != null)
{
    int stopId = 0;
    foreach (Transform b in buildingsRoot.transform)
    {
        var stop = b.GetComponent<BusStop>() ?? b.gameObject.AddComponent<BusStop>();
        stop.StopId = stopId++;
        stop.NearestNodeIndex = graph.NearestNode(b.position);
        EditorUtility.SetDirty(stop);
    }
    Debug.Log($"[RoadGraph] Bound {stopId} bus stops.");
}
```

- [ ] **Step 3: Rebuild and verify**

Run `Bus System/Build Road Graph`. Via MCP read a `cb-apartment-A` object's components.
Expected: each building has a `BusStop` with a valid `NearestNodeIndex` (0..Nodes.Count-1) and unique `StopId`. Log shows "Bound 4 bus stops."

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/BusSystem
git commit -m "feat: register buildings as BusStops bound to nearest node"
```

---

### Task 6: BusPathFollower (kinematic steering)

**Files:**
- Create: `Assets/Scripts/BusSystem/BusPathFollower.cs`
- Test: `Assets/Tests/EditMode/BusPathFollowerTests.cs`

**Interfaces:**
- Produces: `BusPathFollower : MonoBehaviour`:
  - `float Speed = 12f; float TurnSpeed = 180f; float StopDuration = 1f;`
  - `void SetRoute(IReadOnlyList<Vector3> waypoints)`
  - `bool Step(float dt)` — advances; returns `true` on the frame the route completes.
  - `event System.Action ReachedEndOfRoute;`
  - `bool HasRoute { get; }`

- [ ] **Step 1: Write the failing test**

`BusPathFollowerTests.cs`:
```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BusSystem;

public class BusPathFollowerTests
{
    [Test]
    public void Step_MovesTowardWaypointsAndCompletes()
    {
        var go = new GameObject("bus");
        go.transform.position = Vector3.zero;
        var f = go.AddComponent<BusPathFollower>();
        f.Speed = 10f; f.TurnSpeed = 720f; f.StopDuration = 0f;
        f.SetRoute(new List<Vector3> { new Vector3(0,0,0), new Vector3(30,0,0) });

        bool done = false;
        for (int i = 0; i < 1000 && !done; i++) done = f.Step(0.05f);

        Assert.IsTrue(done, "route should complete");
        Assert.Less(Vector3.Distance(go.transform.position, new Vector3(30,0,0)), 0.5f);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void Step_ProgressIsMonotonic()
    {
        var go = new GameObject("bus");
        var f = go.AddComponent<BusPathFollower>();
        f.Speed = 5f; f.TurnSpeed = 720f; f.StopDuration = 0f;
        f.SetRoute(new List<Vector3> { Vector3.zero, new Vector3(50,0,0) });
        float prev = -1f;
        for (int i = 0; i < 50; i++)
        {
            f.Step(0.1f);
            float x = go.transform.position.x;
            Assert.GreaterOrEqual(x + 1e-3f, prev, "x must not go backwards");
            prev = x;
        }
        Object.DestroyImmediate(go);
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Expected: FAIL — `BusPathFollower` missing.

- [ ] **Step 3: Implement follower**

`BusPathFollower.cs`:
```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    public class BusPathFollower : MonoBehaviour
    {
        public float Speed = 12f;
        public float TurnSpeed = 180f;
        public float StopDuration = 1f;

        readonly List<Vector3> _route = new List<Vector3>();
        int _index;
        float _stopTimer;
        public bool HasRoute => _index < _route.Count;
        public event Action ReachedEndOfRoute;

        public void SetRoute(IReadOnlyList<Vector3> waypoints)
        {
            _route.Clear();
            if (waypoints != null) _route.AddRange(waypoints);
            _index = 0;
            _stopTimer = 0f;
            if (_route.Count > 0) transform.position = _route[0];
            _index = _route.Count > 1 ? 1 : _route.Count;
        }

        void Update() { Step(Time.deltaTime); }

        // Returns true on the frame the route finishes.
        public bool Step(float dt)
        {
            if (!HasRoute) return false;
            if (_stopTimer > 0f) { _stopTimer -= dt; return false; }

            Vector3 target = _route[_index];
            Vector3 to = target - transform.position;
            to.y = 0f;

            if (to.sqrMagnitude > 1e-4f)
            {
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * dt);
            }

            transform.position = Vector3.MoveTowards(transform.position, target, Speed * dt);

            if (Vector3.Distance(transform.position, target) < 0.05f)
            {
                _index++;
                if (_index >= _route.Count)
                {
                    _stopTimer = StopDuration;
                    ReachedEndOfRoute?.Invoke();
                    return true;
                }
            }
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Expected: both BusPathFollowerTests PASS.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/BusSystem/BusPathFollower.cs Assets/Tests/EditMode/BusPathFollowerTests.cs
git commit -m "feat: kinematic BusPathFollower + tests"
```

---

### Task 7: IBusController + ExploreAllController

**Files:**
- Create: `Assets/Scripts/BusSystem/IBusController.cs`
- Create: `Assets/Scripts/BusSystem/ExploreAllController.cs`
- Test: `Assets/Tests/EditMode/ExploreAllControllerTests.cs`

**Interfaces:**
- Consumes: `RoadGraph`, `RoadEdge.Visited`, `RoadGraph.GetNeighborEdges`.
- Produces:
  - `interface IBusController { List<int> NextRoute(RoadGraph graph, int currentNode); }` — returns a node-id sequence (route) starting at `currentNode`.
  - `ExploreAllController : IBusController` — walks preferring unvisited edges; when all visited, resets flags and continues (endless coverage loop).

- [ ] **Step 1: Write the failing test**

`ExploreAllControllerTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BusSystem;

public class ExploreAllControllerTests
{
    // Square loop: 0-1-2-3-0
    static RoadGraph SquareGraph()
    {
        var g = new GameObject("g").AddComponent<RoadGraph>();
        for (int i = 0; i < 4; i++) g.Nodes.Add(new RoadNode { Id = i, Position = Vector3.right * i });
        int id = 0;
        void E(int a, int b) => g.Edges.Add(new RoadEdge { Id = id++, NodeA = a, NodeB = b });
        E(0,1); E(1,2); E(2,3); E(3,0);
        return g;
    }

    [Test]
    public void NextRoute_EventuallyCoversEveryEdge()
    {
        var g = SquareGraph();
        var c = new ExploreAllController();
        int current = 0;
        var covered = new HashSet<int>();
        for (int step = 0; step < 20 && covered.Count < g.Edges.Count; step++)
        {
            var route = c.NextRoute(g, current);
            Assert.IsNotNull(route);
            Assert.GreaterOrEqual(route.Count, 2);
            for (int i = 0; i + 1 < route.Count; i++)
            {
                var e = g.Edges.First(x =>
                    (x.NodeA == route[i] && x.NodeB == route[i+1]) ||
                    (x.NodeB == route[i] && x.NodeA == route[i+1]));
                covered.Add(e.Id);
            }
            current = route.Last();
        }
        Assert.AreEqual(g.Edges.Count, covered.Count, "all edges covered");
        Object.DestroyImmediate(g.gameObject);
    }
}
```

- [ ] **Step 2: Run test, verify it fails**

Expected: FAIL — types missing.

- [ ] **Step 3: Implement interface + controller**

`IBusController.cs`:
```csharp
using System.Collections.Generic;
namespace BusSystem
{
    public interface IBusController
    {
        // Returns a route as a sequence of node ids beginning at currentNode.
        List<int> NextRoute(RoadGraph graph, int currentNode);
    }
}
```

`ExploreAllController.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace BusSystem
{
    // Greedy walk preferring unvisited edges; resets when everything is visited.
    public class ExploreAllController : IBusController
    {
        const int MaxSteps = 64;

        public List<int> NextRoute(RoadGraph graph, int currentNode)
        {
            if (graph.Edges.All(e => e.Visited))
                foreach (var e in graph.Edges) e.Visited = false;

            var route = new List<int> { currentNode };
            int node = currentNode;

            for (int i = 0; i < MaxSteps; i++)
            {
                var edges = graph.GetNeighborEdges(node).ToList();
                if (edges.Count == 0) break;

                var pick = edges.FirstOrDefault(e => !e.Visited) ?? edges[i % edges.Count];
                pick.Visited = true;
                int next = pick.NodeA == node ? pick.NodeB : pick.NodeA;
                route.Add(next);
                node = next;

                if (graph.Edges.All(e => e.Visited)) break;
            }
            return route;
        }
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Expected: ExploreAllControllerTests PASS.

- [ ] **Step 5: Commit**
```bash
git add Assets/Scripts/BusSystem/IBusController.cs Assets/Scripts/BusSystem/ExploreAllController.cs Assets/Tests/EditMode/ExploreAllControllerTests.cs
git commit -m "feat: IBusController + ExploreAllController + coverage test"
```

---

### Task 8: BusAgent orchestrator + end-to-end verification

**Files:**
- Create: `Assets/Scripts/BusSystem/BusAgent.cs`

**Interfaces:**
- Consumes: `RoadGraph`, `BusPathFollower`, `IBusController`, `ExploreAllController`.
- Produces: `BusAgent : MonoBehaviour` on `school-bus` — on start snaps to nearest node, asks controller for a route, expands node ids → waypoint polyline, feeds `BusPathFollower`; on `ReachedEndOfRoute` requests the next route (endless).

- [ ] **Step 1: Implement BusAgent**

`BusAgent.cs`:
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    [RequireComponent(typeof(BusPathFollower))]
    public class BusAgent : MonoBehaviour
    {
        public RoadGraph Graph;
        BusPathFollower _follower;
        IBusController _controller;
        int _currentNode;

        void Awake()
        {
            _follower = GetComponent<BusPathFollower>();
            _controller = new ExploreAllController();   // future: swap for agentic brain
        }

        void Start()
        {
            if (Graph == null) Graph = FindObjectOfType<RoadGraph>();
            if (Graph == null || Graph.Nodes.Count == 0) { Debug.LogError("BusAgent: no RoadGraph."); enabled = false; return; }
            _currentNode = Graph.NearestNode(transform.position);
            _follower.ReachedEndOfRoute += QueueNextRoute;
            QueueNextRoute();
        }

        void OnDestroy() { if (_follower != null) _follower.ReachedEndOfRoute -= QueueNextRoute; }

        void QueueNextRoute()
        {
            var nodeRoute = _controller.NextRoute(Graph, _currentNode);
            if (nodeRoute == null || nodeRoute.Count < 2) return;
            _follower.SetRoute(ExpandToWaypoints(nodeRoute));
            _currentNode = nodeRoute[nodeRoute.Count - 1];
        }

        List<Vector3> ExpandToWaypoints(List<int> nodeRoute)
        {
            var pts = new List<Vector3> { Graph.Nodes[nodeRoute[0]].Position };
            for (int i = 0; i + 1 < nodeRoute.Count; i++)
            {
                var edge = FindEdge(nodeRoute[i], nodeRoute[i + 1]);
                if (edge == null) { pts.Add(Graph.Nodes[nodeRoute[i + 1]].Position); continue; }
                var poly = new List<Vector3>(edge.Polyline);
                if (poly.Count > 0 && (poly[0] - Graph.Nodes[nodeRoute[i]].Position).sqrMagnitude >
                                       (poly[poly.Count - 1] - Graph.Nodes[nodeRoute[i]].Position).sqrMagnitude)
                    poly.Reverse();
                for (int k = 1; k < poly.Count; k++) pts.Add(poly[k]);
            }
            return pts;
        }

        RoadEdge FindEdge(int a, int b)
        {
            foreach (var e in Graph.Edges)
                if ((e.NodeA == a && e.NodeB == b) || (e.NodeA == b && e.NodeB == a)) return e;
            return null;
        }
    }
}
```

- [ ] **Step 2: Attach and wire in the scene**

Via MCP: add `BusPathFollower` + `BusAgent` to `school-bus`; set `BusAgent.Graph` to the `RoadGraph` object.

- [ ] **Step 3: End-to-end verification in Play mode**

Via MCP: `Unity_ManageEditor action=Play`. Capture the game/scene view every few seconds (a handful of captures) and `Unity_ReadConsole` for errors.
Expected: the bus drives along roads following lanes/curves, turns at intersections, and over time traverses **all** roads (watch the yellow gizmo edges get covered / observe the bus reach every street). Console clean. Then `action=Stop`.

- [ ] **Step 4: Commit**
```bash
git add Assets/Scripts/BusSystem/BusAgent.cs Assets
git commit -m "feat: BusAgent wires graph+controller+follower; bus explores all roads"
```

---

## Self-Review

**Spec coverage:**
- Hierarchy grouping → Task 1. ✓
- RoadGraph auto-build + gizmo verification → Tasks 2/3/4. ✓
- BusStops bound to nearest node → Task 5. ✓
- Kinematic BusPathFollower → Task 6. ✓
- IBusController seam + ExploreAllController → Task 7. ✓
- BusAgent orchestration + explore-all end-to-end → Task 8. ✓
- Non-goals (no physics/NavMesh/A*/multi-bus) respected. ✓
- Error cases: off-graph start (Task 8 snap), empty route (follower guards + agent guard), wrong topology (Task 4 gate). ✓

**Placeholder scan:** No TBD/TODO left; the one intentional variability (curve/intersection direction sign) is resolved by Task 3 before Task 4 uses it, with explicit fallback instructions. ✓

**Type consistency:** `NextRoute(RoadGraph,int)→List<int>`, `SetRoute(IReadOnlyList<Vector3>)`, `Step(float)→bool`, `ReachedEndOfRoute`, `NearestNode`, `GetNeighborEdges`, `AddOrMergeNode` used identically across tasks. ✓
