using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>
    /// Spawns passenger requests at stops via a Poisson process shaped by PeakProfile (time of day)
    /// and per-stop Weights (space). Weights make demand spatially uneven — a hub attracts and
    /// generates far more trips — which is what gives a coordinating fleet something to exploit
    /// that a fixed loop cannot.
    /// </summary>
    public class DemandAgent : IAgent
    {
        readonly List<int> _stopNodes;
        readonly List<float> _weights;
        readonly float _baseRatePerStopPerHour;

        public DemandAgent(List<int> stopNodes, List<float> weights, float baseRatePerStopPerHour)
        {
            _stopNodes = stopNodes;
            _weights = weights;
            _baseRatePerStopPerHour = baseRatePerStopPerHour;
        }

        float Weight(int i) => (_weights != null && i < _weights.Count) ? Mathf.Max(0f, _weights[i]) : 1f;

        public void Tick(Blackboard bb, float dt)
        {
            float timeOfDay = (bb.SimTime / 3600f) % 24f;
            float mult = PeakProfile.Multiplier(timeOfDay);

            for (int i = 0; i < _stopNodes.Count; i++)
            {
                float lambda = _baseRatePerStopPerHour * mult * Weight(i) * (dt / 3600f);
                int count = SamplePoisson(bb.Rng, lambda);
                for (int k = 0; k < count; k++) SpawnRequest(bb, i);
            }
        }

        void SpawnRequest(Blackboard bb, int originIdx)
        {
            int destIdx = PickDestination(bb, originIdx);
            if (destIdx < 0) return;

            bb.Requests.Add(new PassengerRequest
            {
                Id = bb.NextRequestId(),
                OriginStop = originIdx,
                OriginNode = _stopNodes[originIdx],
                DestStop = destIdx,
                DestNode = _stopNodes[destIdx],
                SpawnTime = bb.SimTime,
                State = RequestState.Waiting
            });

            bb.Activity.Add(ActivityFeed.Kind.Requested, originIdx, destIdx, bb.SimTime);
        }

        /// <summary>Weight-proportional choice among all stops except the origin (deterministic scan).</summary>
        int PickDestination(Blackboard bb, int originIdx)
        {
            if (_stopNodes.Count < 2) return -1;

            float total = 0f;
            for (int i = 0; i < _stopNodes.Count; i++)
                if (i != originIdx) total += Weight(i);

            if (total <= 0f)
            {
                // Degenerate weights: fall back to a uniform pick among the others.
                int offset = bb.Rng.Next(_stopNodes.Count - 1);
                return offset >= originIdx ? offset + 1 : offset;
            }

            double roll = bb.Rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < _stopNodes.Count; i++)
            {
                if (i == originIdx) continue;
                acc += Weight(i);
                if (roll < acc) return i;
            }
            return originIdx == _stopNodes.Count - 1 ? 0 : _stopNodes.Count - 1; // float-rounding guard
        }

        // Knuth's algorithm; fine for the small lambda values used per tick here.
        static int SamplePoisson(System.Random rng, float lambda)
        {
            if (lambda <= 0f) return 0;
            float L = Mathf.Exp(-lambda);
            int k = 0;
            float p = 1f;
            do { k++; p *= (float)rng.NextDouble(); } while (p > L);
            return k - 1;
        }
    }
}
