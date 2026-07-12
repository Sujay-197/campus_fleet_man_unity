using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusSystem.EditorTools
{
    /// <summary>
    /// Builds a RoadGraph from the road tiles under the "Roads" object.
    ///
    /// Straight and intersection tiles have reliable, grid-aligned geometry, so they
    /// form the backbone: straights contribute two endpoints along their (possibly
    /// scaled) local Z axis; intersections contribute a center node plus one arm per
    /// side that meets a neighbouring endpoint. Curve tiles have off-center pivots and
    /// don't fit a simple offset model, so each curve is snapped to the two nearest
    /// dangling road-ends and joined with an arc through the curve's mesh center.
    /// </summary>
    public static class RoadGraphBuilder
    {
        [MenuItem("Bus System/Build Road Graph")]
        public static void Build()
        {
            var roadsRoot = GameObject.Find("Roads");
            if (roadsRoot == null) { Debug.LogError("[RoadGraph] No 'Roads' object found."); return; }

            float cell = RoadGraphConfig.CellSize;
            float halfCell = cell * 0.5f;
            float tol = RoadGraphConfig.MergeTolerance;

            var graphGo = GameObject.Find("RoadGraph") ?? new GameObject("RoadGraph");
            var graph = graphGo.GetComponent<RoadGraph>() ?? graphGo.AddComponent<RoadGraph>();
            graph.Nodes.Clear(); graph.Edges.Clear();
            int edgeId = 0;

            var straights = new List<Transform>();
            var intersections = new List<Transform>();
            var curves = new List<Transform>();
            foreach (Transform t in roadsRoot.transform)
            {
                string n = t.name.ToLower();
                if (n.Contains("straight")) straights.Add(t);
                else if (n.Contains("intersection")) intersections.Add(t);
                else if (n.Contains("curved")) curves.Add(t);
            }

            // Reliable endpoints: gather straight ends and intersection arm points so
            // intersection arms can be pruned to those that actually meet a neighbour.
            var reliablePts = new List<Vector3>();
            foreach (var s in straights)
            {
                float halfLen = RoadGraphConfig.TileLength * Mathf.Abs(s.localScale.z) * 0.5f;
                reliablePts.Add(s.position + s.forward * halfLen);
                reliablePts.Add(s.position - s.forward * halfLen);
            }
            foreach (var it in intersections)
                foreach (var d in Dirs(it, halfCell))
                    reliablePts.Add(it.position + d);

            // --- Straights: one edge per tile between its two ends ---
            foreach (var s in straights)
            {
                float halfLen = RoadGraphConfig.TileLength * Mathf.Abs(s.localScale.z) * 0.5f;
                int a = graph.AddOrMergeNode(s.position + s.forward * halfLen);
                int b = graph.AddOrMergeNode(s.position - s.forward * halfLen);
                AddEdge(graph, ref edgeId, a, b,
                    new List<Vector3> { graph.Nodes[a].Position, s.position, graph.Nodes[b].Position });
            }

            // --- Intersections: center + a spoke to each side that meets a neighbour ---
            foreach (var it in intersections)
            {
                int center = graph.AddOrMergeNode(it.position);
                foreach (var d in Dirs(it, halfCell))
                {
                    Vector3 arm = it.position + d;
                    if (!HasReliableNear(reliablePts, arm, tol, it.position)) continue;
                    int a = graph.AddOrMergeNode(arm);
                    AddEdge(graph, ref edgeId, center, a,
                        new List<Vector3> { graph.Nodes[center].Position, graph.Nodes[a].Position });
                }
            }

            // --- Curves: join the two nearest dangling road-ends via an arc ---
            float searchR = cell * 1.2f;
            foreach (var c in curves)
            {
                Vector3 ctr = MeshCenter(c);
                // candidate nodes near the curve, prefer low-degree (dangling) ends
                var near = graph.Nodes
                    .Select(nd => new { nd, d = (nd.Position - c.position).magnitude })
                    .Where(x => x.d <= searchR)
                    .OrderBy(x => Degree(graph, x.nd.Id))   // dangling first
                    .ThenBy(x => x.d)
                    .ToList();
                if (near.Count < 2) continue;
                var first = near[0].nd;
                // second: nearest node that is a real distance from the first
                var secondSel = near.Skip(1).FirstOrDefault(x => (x.nd.Position - first.Position).magnitude > halfCell);
                if (secondSel == null) continue;
                var second = secondSel.nd;
                AddEdge(graph, ref edgeId, first.Id, second.Id,
                    Bezier(first.Position, ctr, second.Position, 8));
            }

            // --- Bind buildings as bus stops to their nearest node ---
            var buildingsRoot = GameObject.Find("Buildings");
            int stopCount = 0;
            if (buildingsRoot != null)
            {
                foreach (Transform b in buildingsRoot.transform)
                {
                    var stop = b.GetComponent<BusStop>() ?? b.gameObject.AddComponent<BusStop>();
                    stop.StopId = stopCount++;
                    stop.NearestNodeIndex = graph.NearestNode(b.position);
                    EditorUtility.SetDirty(stop);
                }
            }

            EditorUtility.SetDirty(graph);
            Debug.Log($"[RoadGraph] Built {graph.Nodes.Count} nodes, {graph.Edges.Count} edges " +
                      $"({straights.Count} straight, {intersections.Count} intersection, {curves.Count} curve tiles); " +
                      $"bound {stopCount} bus stops.");
        }

        static IEnumerable<Vector3> Dirs(Transform t, float half)
        {
            yield return t.forward * half;
            yield return -t.forward * half;
            yield return t.right * half;
            yield return -t.right * half;
        }

        static bool HasReliableNear(List<Vector3> pts, Vector3 p, float tol, Vector3 exclude)
        {
            float tolSq = tol * tol;
            foreach (var q in pts)
            {
                if ((q - exclude).sqrMagnitude <= tolSq) continue; // skip the intersection's own center-ish
                if ((q - p).sqrMagnitude <= tolSq) return true;
            }
            return false;
        }

        static int Degree(RoadGraph g, int nodeId)
        {
            int d = 0;
            foreach (var e in g.Edges) if (e.NodeA == nodeId || e.NodeB == nodeId) d++;
            return d;
        }

        static Vector3 MeshCenter(Transform t)
        {
            var r = t.GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.center : t.position;
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
                float u = 1 - t;
                pts.Add(u * u * p0 + 2 * u * t * ctrl + t * t * p1);
            }
            return pts;
        }
    }
}
