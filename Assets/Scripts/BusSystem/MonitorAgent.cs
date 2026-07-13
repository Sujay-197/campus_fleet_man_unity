using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BusSystem
{
    /// <summary>Reports live HUD text and, once, writes per-passenger and summary CSVs at run end.</summary>
    public class MonitorAgent : IAgent
    {
        readonly string _resultsDir;
        bool _wrote;

        public MonitorAgent(string resultsDir)
        {
            _resultsDir = resultsDir;
        }

        public void Tick(Blackboard bb, float dt)
        {
            if (bb.Finished && !_wrote)
            {
                WriteResults(bb);
                _wrote = true;
            }
        }

        public static string FormatHud(Blackboard bb)
        {
            int waiting = bb.Waiting.Count();
            int delivered = bb.Requests.Count(r => r.State == RequestState.Delivered);
            return "Mode: " + bb.Mode + "\n" +
                   "SimTime: " + (bb.SimTime / 3600f).ToString("F2") + "h\n" +
                   "Onboard: " + bb.Bus.OnboardRequestIds.Count + "/" + bb.Bus.Capacity + "\n" +
                   "Waiting: " + waiting + "\n" +
                   "Delivered: " + delivered;
        }

        void WriteResults(Blackboard bb)
        {
            Directory.CreateDirectory(_resultsDir);
            string mode = bb.Mode.ToString();

            var delivered = bb.Requests.Where(r => r.State == RequestState.Delivered).ToList();
            var passLines = new List<string> { "RequestId,OriginStop,DestStop,SpawnTime,BoardTime,AlightTime,Wait,Ride,Total" };
            foreach (var r in delivered)
            {
                float wait = r.BoardTime - r.SpawnTime;
                float ride = r.AlightTime - r.BoardTime;
                float total = r.AlightTime - r.SpawnTime;
                passLines.Add(r.Id + "," + r.OriginStop + "," + r.DestStop + "," +
                    r.SpawnTime.ToString("F1") + "," + r.BoardTime.ToString("F1") + "," + r.AlightTime.ToString("F1") + "," +
                    wait.ToString("F1") + "," + ride.ToString("F1") + "," + total.ToString("F1"));
            }
            File.WriteAllLines(Path.Combine(_resultsDir, mode + "_passengers.csv"), passLines);

            int undelivered = bb.Requests.Count(r => r.State != RequestState.Delivered);
            var summary = bb.Metrics.Summarize(undelivered);
            var sumLines = new List<string>
            {
                "Delivered,Undelivered,AvgWait,P90Wait,AvgRide,AvgTotal,MeanOccupancy,EmptyTravelDistance",
                summary.Delivered + "," + summary.Undelivered + "," +
                    summary.AvgWait.ToString("F2") + "," + summary.P90Wait.ToString("F2") + "," +
                    summary.AvgRide.ToString("F2") + "," + summary.AvgTotal.ToString("F2") + "," +
                    summary.MeanOccupancy.ToString("F2") + "," + summary.EmptyTravelDistance.ToString("F2")
            };
            File.WriteAllLines(Path.Combine(_resultsDir, mode + "_summary.csv"), sumLines);
        }
    }
}
