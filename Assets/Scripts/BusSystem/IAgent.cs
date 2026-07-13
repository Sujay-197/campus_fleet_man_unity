namespace BusSystem
{
    /// <summary>An autonomous agent: perceive (read Blackboard) → decide → act (write Blackboard).</summary>
    public interface IAgent
    {
        void Tick(Blackboard bb, float dt);
    }
}
