using System;
using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// A short, self-pruning feed of passenger events for the on-screen activity bar.
    /// Three anti-flood mechanisms keep it readable during peak demand:
    ///   * identical consecutive events coalesce into one counted line
    ///     (a busy route shows "3 requested AB1 -> AB3", not three lines),
    ///   * the feed is capped to <see cref="MaxLines"/> lines (oldest dropped), and
    ///   * each line expires <see cref="TtlSeconds"/> sim-seconds after its last event,
    ///     so the bar clears itself during quiet stretches.
    /// Pure C# and keyed on stop indices, so it stays headless-safe and deterministic;
    /// the renderer resolves indices to building names (AB1..AB4) at draw time.
    /// </summary>
    public class ActivityFeed
    {
        public enum Kind { Requested, PickedUp, Dropped }

        public const int MaxLines = 6;
        public const float TtlSeconds = 3600f; // sim-seconds a line lingers after its last event

        public class Entry
        {
            public Kind Kind;
            public int A;        // Requested: origin stop; PickedUp/Dropped: the stop
            public int B;        // Requested: destination stop; otherwise -1
            public int Count;
            public float LastTime;
        }

        readonly List<Entry> _entries = new List<Entry>();

        public void Add(Kind kind, int a, int b, float simTime)
        {
            // Coalesce into an existing matching line, refreshing its recency.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Kind == kind && e.A == a && e.B == b)
                {
                    e.Count++;
                    e.LastTime = simTime;
                    if (i != _entries.Count - 1) { _entries.RemoveAt(i); _entries.Add(e); }
                    return;
                }
            }

            _entries.Add(new Entry { Kind = kind, A = a, B = b, Count = 1, LastTime = simTime });
            while (_entries.Count > MaxLines) _entries.RemoveAt(0);
        }

        /// <summary>Prune expired lines and return the live entries, oldest first.</summary>
        public IReadOnlyList<Entry> Current(float simTime)
        {
            _entries.RemoveAll(e => simTime - e.LastTime > TtlSeconds);
            return _entries;
        }

        public static string Format(Entry e, Func<int, string> name)
        {
            switch (e.Kind)
            {
                case Kind.Requested: return e.Count + "  requested  " + name(e.A) + " -> " + name(e.B);
                case Kind.PickedUp:  return e.Count + "  picked up  @ " + name(e.A);
                default:             return e.Count + "  dropped    @ " + name(e.A);
            }
        }
    }
}
