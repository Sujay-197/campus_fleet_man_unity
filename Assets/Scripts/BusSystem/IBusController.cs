using System.Collections.Generic;

namespace BusSystem
{
    /// <summary>
    /// Decides the bus's next route over the road graph. v1 covers all roads; the
    /// future agentic fleet brain implements this same interface to route to stops.
    /// </summary>
    public interface IBusController
    {
        /// <summary>A route as a sequence of node ids beginning at currentNode (length >= 2).</summary>
        List<int> NextRoute(RoadGraph graph, int currentNode);
    }
}
