namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// A doorway connection between two placed <see cref="DungeonModule"/> instances.
    /// </summary>
    public readonly struct ModuleConnection
    {
        public readonly DungeonModule FirstModule;
        public readonly DungeonModule SecondModule;

        public ModuleConnection(DungeonModule firstModule, DungeonModule secondModule)
        {
            FirstModule = firstModule;
            SecondModule = secondModule;
        }

        /// <summary>
        /// Whether either end of this connection is the given module.
        /// </summary>
        public bool Involves(DungeonModule dungeonModule)
        {
            return FirstModule == dungeonModule || SecondModule == dungeonModule;
        }
    }
}
