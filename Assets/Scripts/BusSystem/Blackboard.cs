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
        public RunMode Mode;
        public bool Finished;

        int _nextRequestId;
        public int NextRequestId() => _nextRequestId++;

        public IEnumerable<PassengerRequest> Waiting => Requests.Where(r => r.State == RequestState.Waiting);
        public IEnumerable<PassengerRequest> WaitingAt(int stopNode) => Waiting.Where(r => r.OriginNode == stopNode);
    }
}
