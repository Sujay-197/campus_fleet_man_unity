using System.Collections.Generic;
using UnityEngine;

namespace BusSystem
{
    /// <summary>Spawns passenger requests at stops via a Poisson process shaped by PeakProfile.</summary>
    public class DemandAgent : IAgent
    {
        readonly List<int> _stopNodes;
        readonly float _baseRatePerStopPerHour;

        public DemandAgent(List<int> stopNodes, float baseRatePerStopPerHour)
        {
            _stopNodes = stopNodes;
            _baseRatePerStopPerHour = baseRatePerStopPerHour;
        }

        public void Tick(Blackboard bb, float dt)
        {
            float timeOfDay = (bb.SimTime / 3600f) % 24f;
            float mult = PeakProfile.Multiplier(timeOfDay);
            float lambda = _baseRatePerStopPerHour * mult * (dt / 3600f);

            for (int i = 0; i < _stopNodes.Count; i++)
            {
                int count = SamplePoisson(bb.Rng, lambda);
                for (int k = 0; k < count; k++) SpawnRequest(bb, i);
            }
        }

        void SpawnRequest(Blackboard bb, int originIdx)
        {
            int destIdx = originIdx;
            while (destIdx == originIdx) destIdx = bb.Rng.Next(_stopNodes.Count);

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
