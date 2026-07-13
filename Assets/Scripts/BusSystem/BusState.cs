using System.Collections.Generic;

namespace BusSystem
{
    public class BusState
    {
        public int CurrentNode;
        public int Capacity;
        public List<int> OnboardRequestIds = new List<int>();
        public List<PlanTask> Plan = new List<PlanTask>();
    }
}
