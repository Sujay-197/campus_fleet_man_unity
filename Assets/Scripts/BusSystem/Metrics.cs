using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BusSystem
{
    /// <summary>Per-bus rollup, used for the utilization-balance figure and the per-bus CSV.</summary>
    public class BusMetrics
    {
        public int BusId;
        public int Delivered;
        public float EmptyTravelDistance;
        public float MeanOccupancy;
    }

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
        public int BusCount;
        /// <summary>Coefficient of variation of per-bus delivered counts: 0 = perfectly balanced.</summary>
        public float DeliveredCoV;
    }

    /// <summary>
    /// Accumulates per-delivery timings and occupancy for one simulation run, both fleet-wide and
    /// per bus. The per-bus split exists to expose fleet *imbalance* — greedy one-shot assignment
    /// plus hold-in-place idling can load one bus far more than another, and DeliveredCoV measures
    /// exactly that rather than hiding it inside a fleet average.
    /// </summary>
    public class Metrics
    {
        readonly List<float> _waits = new List<float>();
        readonly List<float> _rides = new List<float>();
        readonly List<float> _totals = new List<float>();

        // Indexed by bus id (a list, not a dictionary, so iteration order is deterministic).
        readonly List<int> _deliveredPerBus = new List<int>();
        readonly List<float> _emptyTravelPerBus = new List<float>();
        readonly List<long> _occSumPerBus = new List<long>();
        readonly List<int> _occCountPerBus = new List<int>();

        public void EnsureBuses(int count)
        {
            while (_deliveredPerBus.Count < count)
            {
                _deliveredPerBus.Add(0);
                _emptyTravelPerBus.Add(0f);
                _occSumPerBus.Add(0L);
                _occCountPerBus.Add(0);
            }
        }

        public void RecordDelivery(PassengerRequest r, int busId)
        {
            _waits.Add(r.BoardTime - r.SpawnTime);
            _rides.Add(r.AlightTime - r.BoardTime);
            _totals.Add(r.AlightTime - r.SpawnTime);
            if (busId >= 0)
            {
                EnsureBuses(busId + 1);
                _deliveredPerBus[busId]++;
            }
        }

        public void SampleOccupancy(int busId, int count)
        {
            EnsureBuses(busId + 1);
            _occSumPerBus[busId] += count;
            _occCountPerBus[busId]++;
        }

        public void AddEmptyTravel(int busId, float distance)
        {
            EnsureBuses(busId + 1);
            _emptyTravelPerBus[busId] += distance;
        }

        public List<BusMetrics> PerBus()
        {
            var list = new List<BusMetrics>();
            for (int i = 0; i < _deliveredPerBus.Count; i++)
            {
                list.Add(new BusMetrics
                {
                    BusId = i,
                    Delivered = _deliveredPerBus[i],
                    EmptyTravelDistance = _emptyTravelPerBus[i],
                    MeanOccupancy = _occCountPerBus[i] == 0 ? 0f : (float)_occSumPerBus[i] / _occCountPerBus[i]
                });
            }
            return list;
        }

        public MetricsSummary Summarize(int undelivered)
        {
            long occSum = _occSumPerBus.Sum();
            int occCount = _occCountPerBus.Sum();

            return new MetricsSummary
            {
                Delivered = _waits.Count,
                Undelivered = undelivered,
                AvgWait = Average(_waits),
                P90Wait = Percentile(_waits, 0.9f),
                AvgRide = Average(_rides),
                AvgTotal = Average(_totals),
                MeanOccupancy = occCount == 0 ? 0f : (float)occSum / occCount,
                EmptyTravelDistance = _emptyTravelPerBus.Sum(),
                BusCount = _deliveredPerBus.Count,
                DeliveredCoV = CoefficientOfVariation(_deliveredPerBus)
            };
        }

        /// <summary>Population stddev ÷ mean. 0 when perfectly even (or nothing delivered).</summary>
        static float CoefficientOfVariation(List<int> xs)
        {
            if (xs.Count == 0) return 0f;
            float mean = (float)xs.Average();
            if (mean <= 0f) return 0f;
            float varSum = xs.Sum(x => (x - mean) * (x - mean));
            float stddev = Mathf.Sqrt(varSum / xs.Count);
            return stddev / mean;
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
