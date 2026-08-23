namespace BusSystem
{
    public enum RequestState { Waiting, OnBoard, Delivered }

    /// <summary>A single passenger trip request from an origin stop to a destination stop.</summary>
    public class PassengerRequest
    {
        public int Id;
        public int OriginStop;
        public int OriginNode;
        public int DestStop;
        public int DestNode;
        public float SpawnTime;
        public float BoardTime = -1f;
        public float AlightTime = -1f;
        public RequestState State = RequestState.Waiting;
        // -1 = unassigned. Set once by FleetOptimizerAgent; never reassigned (greedy one-shot).
        public int AssignedBusId = -1;
    }
}
