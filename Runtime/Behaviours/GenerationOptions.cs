namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Options for a generation pass. The default value generates normally: logs the result, fires callbacks and activates
    /// room listeners, with a frame budget of <see cref="DefaultMillisecondsPerFrame"/> for asynchronous passes.
    /// </summary>
    public struct GenerationOptions
    {
        /// <summary>
        /// The frame budget used when <see cref="MillisecondsPerFrame"/> is not set.
        /// </summary>
        public const float DefaultMillisecondsPerFrame = 4f;

        /// <summary>
        /// Asynchronous passes only: roughly how much time to spend generating each frame before yielding. A single placement
        /// attempt is never split, so a frame can run slightly over. Values of 0 or less use <see cref="DefaultMillisecondsPerFrame"/>.
        /// </summary>
        public float MillisecondsPerFrame;

        /// <summary>
        /// Disables the renderers and lights of new modules until the layout is complete, so a layout built over several frames
        /// never appears half-finished. Colliders stay active because the overlap check needs them.
        /// </summary>
        public bool HideUntilComplete;

        /// <summary>
        /// Skips calling <see cref="IDungeonModuleListener.Activate"/> when the pass succeeds. Call
        /// <see cref="DungeonGenerator.ActivateModules"/> later, for example when the player reaches a floor built in advance.
        /// </summary>
        public bool DeferActivation;

        /// <summary>
        /// Skips logging, the success/failure callbacks, the <see cref="DungeonGenerator.GenerationCompleted"/> event and listener
        /// activation. Used by batch testing.
        /// </summary>
        public bool Silent;

        /// <summary>
        /// The frame budget to use, with the default applied.
        /// </summary>
        public float ResolvedMillisecondsPerFrame => MillisecondsPerFrame > 0f ? MillisecondsPerFrame : DefaultMillisecondsPerFrame;
    }
}
