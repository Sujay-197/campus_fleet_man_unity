namespace BusSystem
{
    /// <summary>
    /// Tunable constants for road-graph construction. Values confirmed empirically
    /// from the scene's road tiles (Task 3).
    /// </summary>
    public static class RoadGraphConfig
    {
        // World spacing between adjacent grid-aligned tile centers.
        public const float CellSize = 27.85f;

        // A single (unscaled) road tile's length along its local Z axis.
        // Straight tiles may be scaled along Z, so endpoint offsets multiply this
        // by the tile's local scale.z.
        public const float TileLength = 28.1811f;

        // Two connection points closer than CellSize * this factor merge into one node.
        public const float MergeToleranceFactor = 0.4f;

        public static float MergeTolerance => CellSize * MergeToleranceFactor;
    }
}
