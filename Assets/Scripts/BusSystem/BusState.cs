using System.Collections.Generic;

namespace BusSystem
{
    public class BusState
    {
        /// <summary>Stable index into Blackboard.Buses; used for per-bus metrics and CSV rows.</summary>
        public int Id;
        public int CurrentNode;
        public int Capacity;
        public List<int> OnboardRequestIds = new List<int>();
        public List<PlanTask> Plan = new List<PlanTask>();
    }
}
