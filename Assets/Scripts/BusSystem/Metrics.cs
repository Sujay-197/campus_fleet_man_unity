using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    public class MetricsSummary
    {
        public int Delivered;
        public int Undelivered;
        public float AvgWait;
        public float P90Wait;
        public float AvgRide;
        public float AvgTotal;
        public float MeanOccupancy;
        public float EmptyTravelDistance;
    }

    /// <summary>Accumulates per-delivery timings and occupancy samples for one simulation run.</summary>
    public class Metrics
    {
        readonly List<float> _waits = new List<float>();
        readonly List<float> _rides = new List<float>();
        readonly List<float> _totals = new List<float>();
        readonly List<int> _occupancySamples = new List<int>();

        public float EmptyTravelDistance;

        public void RecordDelivery(PassengerRequest r)
        {
            _waits.Add(r.BoardTime - r.SpawnTime);
            _rides.Add(r.AlightTime - r.BoardTime);
            _totals.Add(r.AlightTime - r.SpawnTime);
        }

        public void SampleOccupancy(int count) => _occupancySamples.Add(count);

        public MetricsSummary Summarize(int undelivered)
        {
            return new MetricsSummary
            {
                Delivered = _waits.Count,
                Undelivered = undelivered,
                AvgWait = Average(_waits),
                P90Wait = Percentile(_waits, 0.9f),
                AvgRide = Average(_rides),
                AvgTotal = Average(_totals),
                MeanOccupancy = _occupancySamples.Count == 0 ? 0f : (float)_occupancySamples.Average(),
                EmptyTravelDistance = EmptyTravelDistance
            };
        }

        static float Average(List<float> xs) => xs.Count == 0 ? 0f : xs.Sum() / xs.Count;

        static float Percentile(List<float> xs, float p)
        {
            if (xs.Count == 0) return 0f;
            var sorted = xs.OrderBy(x => x).ToList();
            int idx = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1);
            return sorted[idx];
        }
    }
}
