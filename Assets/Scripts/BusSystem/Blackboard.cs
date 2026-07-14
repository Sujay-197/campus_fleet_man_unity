using System.Collections.Generic;
using System.Linq;

namespace BusSystem
{
    public enum RunMode { Dynamic, FixedRoute }

    /// <summary>Shared world state every agent perceives from and acts upon.</summary>
    public class Blackboard
    {
        public float SimTime;
        public System.Random Rng;
        public RoadGraph Graph;
        public List<PassengerRequest> Requests = new List<PassengerRequest>();
        public BusState Bus = new BusState();
        public Metrics Metrics = new Metrics();
        public ActivityFeed Activity = new ActivityFeed();
        public RunMode Mode;
        public bool Finished;

        // Human-readable stop labels (building names AB1..AB4), indexed by stop index —
        // the same ordering as PassengerRequest.OriginStop/DestStop.
        public List<string> StopNames = new List<string>();
        public string StopName(int stopIndex) =>
            (stopIndex >= 0 && stopIndex < StopNames.Count) ? StopNames[stopIndex] : ("S" + stopIndex);

        int _nextRequestId;
        public int NextRequestId() => _nextRequestId++;

        public IEnumerable<PassengerRequest> Waiting => Requests.Where(r => r.State == RequestState.Waiting);
        public IEnumerable<PassengerRequest> WaitingAt(int stopNode) => Waiting.Where(r => r.OriginNode == stopNode);
    }
}
