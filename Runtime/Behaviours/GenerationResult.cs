namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Represents the possible errors that can occur during dungeon generation.
    /// </summary>
    public enum GenerationError
    {
        None = 0,
        InsufficientSpace = 101,
        InsufficientRequiredSpace = 201,
        InvalidHistory = 301,
        InvalidBacktracking = 302,
        IterationLimitExceeded = 401,
        CategoryPlacementFailed = 402,
        RequiredSamplingFailed = 403,
        NoOptionalCategories = 404,
        MissingRoleCategory = 501,
        MainPathFailed = 502,
        RequiredBranchFailed = 503,
        UnknownError = 999
    }

    /// <summary>
    /// The outcome of a single <see cref="DungeonGenerator"/> pass, including the seed needed to replay it.
    /// </summary>
    public readonly struct GenerationResult
    {
        /// <summary>
        /// The seed the layout was generated from. Passing it back to <see cref="DungeonGenerator.GenerateDungeon(int, bool)"/> reproduces the layout.
        /// </summary>
        public readonly int Seed;

        /// <summary>
        /// The error that stopped generation, or <see cref="GenerationError.None"/> on success.
        /// </summary>
        public readonly GenerationError Error;

        /// <summary>
        /// The title of the <see cref="DungeonTheme"/> selected for this pass.
        /// </summary>
        public readonly string ThemeTitle;

        /// <summary>
        /// The number of modules placed when generation finished.
        /// </summary>
        public readonly int ModuleCount;

        /// <summary>
        /// The number of modules this pass was aiming for.
        /// </summary>
        public readonly int ModuleLimit;

        /// <summary>
        /// The number of times a placed module was undone to make room.
        /// </summary>
        public readonly int BacktrackCount;

        /// <summary>
        /// Wall-clock time spent generating, in milliseconds.
        /// </summary>
        public readonly double ElapsedMilliseconds;

        /// <summary>
        /// Whether the pass placed every module without error.
        /// </summary>
        public bool Succeeded => Error == GenerationError.None;

        public GenerationResult(int seed, GenerationError error, string themeTitle, int moduleCount, int moduleLimit, int backtrackCount, double elapsedMilliseconds)
        {
            Seed = seed;
            Error = error;
            ThemeTitle = themeTitle;
            ModuleCount = moduleCount;
            ModuleLimit = moduleLimit;
            BacktrackCount = backtrackCount;
            ElapsedMilliseconds = elapsedMilliseconds;
        }

        /// <summary>
        /// Returns a human-readable description of a <see cref="GenerationError"/>.
        /// </summary>
        /// <param name="generationError">The error to describe.</param>
        /// <returns>A short explanation of what went wrong.</returns>
        public static string Describe(GenerationError generationError)
        {
            return generationError switch
            {
                GenerationError.None => "No error.",
                GenerationError.InsufficientSpace => "Insufficient space for module placement.",
                GenerationError.InsufficientRequiredSpace => "Insufficient space to place all required modules.",
                GenerationError.InvalidHistory => "No generation history available for backtracking.",
                GenerationError.InvalidBacktracking => "Backtracking limit exceeded.",
                GenerationError.IterationLimitExceeded => "Generation loop limit exceeded. Optional categories may be capped below the module count.",
                GenerationError.CategoryPlacementFailed => "Could not place enough modules for a spawn-limited category.",
                GenerationError.RequiredSamplingFailed => "Could not reserve enough modules for a required category. It may not have enough spawn-once modules.",
                GenerationError.NoOptionalCategories => "The theme has no optional categories to fill the remaining module slots.",
                GenerationError.MissingRoleCategory => "Path-first themes need categories with the Start, MainPath, Boss and End roles, each with at least one usable module.",
                GenerationError.MainPathFailed => "Could not fit the main path (Start → main path → Boss → End), even after backtracking.",
                GenerationError.RequiredBranchFailed => "Could not place every required Branch category off the main path.",
                _ => "Unknown error."
            };
        }
    }
}
