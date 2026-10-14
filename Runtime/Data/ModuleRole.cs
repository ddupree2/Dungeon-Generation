namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// The part a <see cref="ModuleCategory"/> plays in a <see cref="LayoutMode.PathFirst"/> layout.
    /// </summary>
    public enum ModuleRole
    {
        /// <summary>Not used by path-first generation. Weighted layouts ignore roles entirely.</summary>
        None = 0,

        /// <summary>The single room every layout begins from.</summary>
        Start = 1,

        /// <summary>Rooms that make up the main path between Start and Boss.</summary>
        MainPath = 2,

        /// <summary>The room at the end of the main path. Needs at least two doorways (entrance and exit).</summary>
        Boss = 3,

        /// <summary>The final room, attached to the Boss room's remaining doorway.</summary>
        End = 4,

        /// <summary>Optional rooms grown in short branches off main-path rooms (shops, treasure, optional fights).</summary>
        Branch = 5,

        /// <summary>Hallways and other pieces placed between rooms. Not counted as rooms for path length or depth.</summary>
        Connector = 6
    }

    /// <summary>
    /// How a <see cref="DungeonTheme"/> assembles its modules.
    /// </summary>
    public enum LayoutMode
    {
        /// <summary>
        /// The original generator: attaches weighted categories to any open doorway until the module count is reached.
        /// Produces blob-shaped layouts with no guaranteed order.
        /// </summary>
        Weighted = 0,

        /// <summary>
        /// Builds a guaranteed Start → main path → Boss → End structure, then grows optional branches off main-path rooms.
        /// Uses category roles, the theme's path settings and its <see cref="ConnectionRules"/>.
        /// </summary>
        PathFirst = 1
    }
}
