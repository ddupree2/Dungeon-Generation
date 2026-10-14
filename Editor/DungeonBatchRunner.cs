using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// The outcome of a batch of generation passes: every result plus the seeds that need a closer look.
    /// </summary>
    public class DungeonBatchReport
    {
        /// <summary>
        /// One result per generated seed, in run order.
        /// </summary>
        public readonly List<GenerationResult> Results = new();

        /// <summary>
        /// Seeds that produced a different layout when generated a second time.
        /// </summary>
        public readonly List<int> NondeterministicSeeds = new();

        /// <summary>
        /// Layout rule violations found by <see cref="DungeonLayoutValidator"/>, keyed by seed.
        /// </summary>
        public readonly Dictionary<int, List<string>> LayoutViolations = new();

        /// <summary>
        /// Whether the determinism check ran for this batch.
        /// </summary>
        public bool CheckedDeterminism { get; internal set; }

        public int SucceededCount => Results.Count(batchResult => batchResult.Succeeded);

        public int FailedCount => Results.Count - SucceededCount;

        /// <summary>
        /// Whether every pass succeeded with a valid, reproducible layout.
        /// </summary>
        public bool AllPassed => FailedCount == 0 && LayoutViolations.Count == 0 && NondeterministicSeeds.Count == 0;

        public double AverageBacktracks => Results.Count > 0 ? Results.Average(batchResult => batchResult.BacktrackCount) : 0;

        public int MaximumBacktracks => Results.Count > 0 ? Results.Max(batchResult => batchResult.BacktrackCount) : 0;

        public double AverageMilliseconds => Results.Count > 0 ? Results.Average(batchResult => batchResult.ElapsedMilliseconds) : 0;

        public double MaximumMilliseconds => Results.Count > 0 ? Results.Max(batchResult => batchResult.ElapsedMilliseconds) : 0;

        public string Percent(int count)
        {
            return Results.Count <= 0 ? "0%" : ((float)count / Results.Count).ToString("0.#%", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A plain-text report listing the summary and every seed that failed, broke a layout rule or wasn't reproducible.
        /// </summary>
        public override string ToString()
        {
            var reportBuilder = new StringBuilder();

            reportBuilder.AppendLine($"[Dungeon Batch Tester] {SucceededCount}/{Results.Count} succeeded ({Percent(SucceededCount)}), " +
                                     $"{LayoutViolations.Count} layouts with rule violations" +
                                     (CheckedDeterminism ? $", {NondeterministicSeeds.Count} nondeterministic" : string.Empty));
            reportBuilder.AppendLine($"Backtracks avg {AverageBacktracks.ToString("0.0", CultureInfo.InvariantCulture)}, max {MaximumBacktracks}; " +
                                     $"time avg {AverageMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms, max {MaximumMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms");

            foreach (var errorGroup in Results.Where(batchResult => !batchResult.Succeeded).GroupBy(batchResult => batchResult.Error))
            {
                reportBuilder.AppendLine($"  {errorGroup.Key} x{errorGroup.Count()}: {GenerationResult.Describe(errorGroup.Key)}");
            }

            foreach (var failedResult in Results.Where(batchResult => !batchResult.Succeeded))
            {
                reportBuilder.AppendLine($"  FAILED seed {failedResult.Seed}: {failedResult.Error}");
            }

            foreach (var violationEntry in LayoutViolations)
            {
                reportBuilder.AppendLine($"  RULES seed {violationEntry.Key}: {string.Join("; ", violationEntry.Value)}");
            }

            foreach (var seed in NondeterministicSeeds)
            {
                reportBuilder.AppendLine($"  NONDETERMINISTIC seed {seed}");
            }

            return reportBuilder.ToString();
        }
    }

    /// <summary>
    /// Runs a <see cref="DungeonGenerator"/> over many seeds and checks every layout. Used by the batch tester window and
    /// callable from scripts (for example <c>-executeMethod</c> in batch mode).
    /// </summary>
    public static class DungeonBatchRunner
    {
        /// <summary>
        /// Generates <paramref name="runCount"/> seeds and checks each successful layout.
        /// </summary>
        /// <param name="dungeonGenerator">The generator to run. Its current layout is replaced.</param>
        /// <param name="runCount">How many seeds to generate.</param>
        /// <param name="startSeed">0 for random seeds; otherwise seeds <c>startSeed</c>, <c>startSeed + 1</c>, ...</param>
        /// <param name="checkDeterminism">Whether to generate each successful seed twice and compare the layouts.</param>
        /// <param name="reportProgress">Called before each run with (run index, run count); return <c>true</c> to cancel.</param>
        /// <returns>The batch report.</returns>
        public static DungeonBatchReport Run(DungeonGenerator dungeonGenerator, int runCount, int startSeed, bool checkDeterminism, Func<int, int, bool> reportProgress = null)
        {
            var batchReport = new DungeonBatchReport { CheckedDeterminism = checkDeterminism };
            var seedSource = new Random();

            for (var runIndex = 0; runIndex < runCount; runIndex++)
            {
                if (reportProgress != null && reportProgress(runIndex, runCount)) break;

                var seed = startSeed != 0 ? startSeed + runIndex : seedSource.Next(1, int.MaxValue);
                if (seed == 0) seed = 1;

                var generationResult = dungeonGenerator.GenerateDungeon(seed, true);
                batchReport.Results.Add(generationResult);

                if (!generationResult.Succeeded) continue;

                var violations = DungeonLayoutValidator.Validate(dungeonGenerator);
                if (0 < violations.Count)
                {
                    batchReport.LayoutViolations[seed] = violations;
                }

                if (!checkDeterminism) continue;

                var firstLayout = DungeonLayoutValidator.Fingerprint(dungeonGenerator);
                dungeonGenerator.GenerateDungeon(seed, true);

                if (firstLayout != DungeonLayoutValidator.Fingerprint(dungeonGenerator))
                {
                    batchReport.NondeterministicSeeds.Add(seed);
                }
            }

            return batchReport;
        }
    }
}
