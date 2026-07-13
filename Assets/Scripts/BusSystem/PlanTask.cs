namespace BusSystem
{
    // Kind is planning-time bookkeeping only (used by InsertionPlanner's cost/feasibility
    // reasoning and for debugging); Dispatch executes generically off StopNode alone —
    // at every stop it alights anyone whose destination matches and boards anyone waiting
    // there, regardless of which specific request a task was inserted for. Visit is used
    // by the fixed-route baseline, which has no specific request bound to a stop.
    public enum PlanTaskKind { Pickup, Dropoff, Visit }

    public class PlanTask
    {
        public PlanTaskKind Kind;
        public int RequestId;
        public int StopNode;
    }
}
