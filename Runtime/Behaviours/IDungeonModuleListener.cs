namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Implemented by game scripts on room prefabs (room controllers, encounter spawners, etc.) that should only start once
    /// the dungeon is final.
    /// </summary>
    /// <remarks>
    /// Every placement attempt instantiates the full prefab and destroys it if it doesn't fit, so <c>Awake</c> and <c>Start</c>
    /// also run on rooms that get thrown away. Room scripts should do nothing in <c>Awake</c> or <c>Start</c> and do their setup in
    /// <see cref="Activate"/> instead. Spawning rooms inactive is not an alternative: inactive colliders are ignored by the overlap check.
    /// </remarks>
    public interface IDungeonModuleListener
    {
        /// <summary>
        /// Called once on every listener in each final module's hierarchy after generation succeeds in Play Mode.
        /// </summary>
        /// <param name="dungeonModule">The placed module this listener belongs to. Its depth, role and main-path flag are set.</param>
        /// <param name="dungeonGenerator">The generator that built the layout.</param>
        void Activate(DungeonModule dungeonModule, DungeonGenerator dungeonGenerator);
    }
}
