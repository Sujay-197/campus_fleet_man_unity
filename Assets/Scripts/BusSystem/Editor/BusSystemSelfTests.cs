using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BusSystem.EditorTools
{
    /// <summary>
    /// Lightweight, MCP-runnable regression checks for the bus system.
    /// Run via menu "Bus System/Run Self-Tests"; results are logged to the console.
    /// (This project is driven headlessly via MCP, where the Unity Test Runner UI
    /// cannot be invoked, so these stand in for edit-mode unit tests.)
    /// </summary>
    public static class BusSystemSelfTests
    {
        static int _pass, _fail;

        [MenuItem("Bus System/Run Self-Tests")]
        public static void RunAll()
        {
            _pass = 0; _fail = 0;
            RoadGraphTests();
            if (_fail == 0)
                Debug.Log($"[BusSystem SelfTests] ALL PASSED ({_pass} checks).");
            else
                Debug.LogError($"[BusSystem SelfTests] {_fail} FAILED, {_pass} passed.");
        }

        static void Check(bool cond, string name)
        {
            if (cond) { _pass++; Debug.Log($"  PASS: {name}"); }
            else { _fail++; Debug.LogError($"  FAIL: {name}"); }
        }

        static RoadGraph NewGraph(string n)
        {
            var go = new GameObject(n) { hideFlags = HideFlags.HideAndDontSave };
            return go.AddComponent<RoadGraph>();
        }

        static void RoadGraphTests()
        {
            var g = NewGraph("g1");
            int a = g.AddOrMergeNode(new Vector3(0, 0, 0));
            int b = g.AddOrMergeNode(new Vector3(RoadGraphConfig.MergeTolerance * 0.5f, 0, 0));
            int c = g.AddOrMergeNode(new Vector3(100, 0, 0));
            Check(a == b, "RoadGraph: close points merge");
            Check(a != c, "RoadGraph: far point is new node");
            Check(g.Nodes.Count == 2, "RoadGraph: node count == 2");

            var g2 = NewGraph("g2");
            g2.AddOrMergeNode(new Vector3(0, 0, 0));
            g2.AddOrMergeNode(new Vector3(50, 0, 0));
            Check(g2.NearestNode(new Vector3(48, 0, 0)) == 1, "RoadGraph: NearestNode returns closest");

            var g3 = NewGraph("g3");
            int n0 = g3.AddOrMergeNode(Vector3.zero);
            int n1 = g3.AddOrMergeNode(new Vector3(50, 0, 0));
            int n2 = g3.AddOrMergeNode(new Vector3(0, 0, 50));
            g3.Edges.Add(new RoadEdge { Id = 0, NodeA = n0, NodeB = n1 });
            g3.Edges.Add(new RoadEdge { Id = 1, NodeA = n0, NodeB = n2 });
            var nb = g3.GetNeighborEdges(n0).Select(e => e.Id).OrderBy(x => x).ToArray();
            Check(nb.Length == 2 && nb[0] == 0 && nb[1] == 1, "RoadGraph: GetNeighborEdges");

            Object.DestroyImmediate(g.gameObject);
            Object.DestroyImmediate(g2.gameObject);
            Object.DestroyImmediate(g3.gameObject);
        }
    }
}
