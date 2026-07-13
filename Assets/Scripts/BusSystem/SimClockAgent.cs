namespace BusSystem
{
    /// <summary>Advances the simulation clock and flags the run finished after a fixed duration.</summary>
    public class SimClockAgent : IAgent
    {
        readonly float _durationSeconds;

        public SimClockAgent(float durationHours)
        {
            _durationSeconds = durationHours * 3600f;
        }

        public void Tick(Blackboard bb, float dt)
        {
            bb.SimTime += dt;
            if (bb.SimTime >= _durationSeconds) bb.Finished = true;
        }
    }
}
