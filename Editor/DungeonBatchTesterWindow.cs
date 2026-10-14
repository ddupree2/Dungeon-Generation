using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// Generates many seeds in a row with a <see cref="DungeonGenerator"/> and reports success rates, failure reasons,
    /// backtracking and timing, so generator changes can be checked before and after.
    /// </summary>
    internal class DungeonBatchTesterWindow : EditorWindow
    {
        private const int DefaultRunCount = 200;

        [SerializeField] private DungeonGenerator targetGenerator;
        [SerializeField] private int runCount = DefaultRunCount;
        [SerializeField] private int startSeed;
        [SerializeField] private bool checkDeterminism;
        [SerializeField] private bool keepLastLayout;

        /// <summary>
        /// The results of the most recent batch, one per seed.
        /// </summary>
        private List<GenerationResult> batchResults = new();

        /// <summary>
        /// Seeds that produced a different layout when generated a second time.
        /// </summary>
        private List<int> nondeterministicSeeds = new();

        /// <summary>
        /// Layout rule violations reported by <see cref="DungeonLayoutValidator"/>, keyed by seed.
        /// </summary>
        private Dictionary<int, List<string>> layoutViolations = new();

        private Vector2 scrollPosition;

        [MenuItem("Tools/Dungeon Generation/Batch Tester")]
        private static void OpenWindow()
        {
            Open(Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<DungeonGenerator>() : null);
        }

        /// <summary>
        /// Opens the batch tester targeting the given <see cref="DungeonGenerator"/>.
        /// </summary>
        /// <param name="dungeonGenerator">The generator to test, or <c>null</c> to keep the current target.</param>
        public static void Open(DungeonGenerator dungeonGenerator)
        {
            var testerWindow = GetWindow<DungeonBatchTesterWindow>("Dungeon Batch Tester");
            if (dungeonGenerator != null)
            {
                testerWindow.targetGenerator = dungeonGenerator;
            }

            testerWindow.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Batch Settings", EditorStyles.boldLabel);

            targetGenerator = (DungeonGenerator)EditorGUILayout.ObjectField("Generator", targetGenerator, typeof(DungeonGenerator), true);
            runCount = Mathf.Max(1, EditorGUILayout.IntField("Runs", runCount));
            startSeed = EditorGUILayout.IntField(new GUIContent("Start Seed", "0 uses random seeds; any other value tests startSeed, startSeed + 1, ..."), startSeed);
            checkDeterminism = EditorGUILayout.Toggle(new GUIContent("Check Determinism", "Generates every seed twice and compares the layouts. Doubles the run time."), checkDeterminism);
            keepLastLayout = EditorGUILayout.Toggle(new GUIContent("Keep Last Layout", "Leave the final layout in the scene instead of clearing it."), keepLastLayout);

            EditorGUILayout.Space(5);
            using (new EditorGUI.DisabledScope(targetGenerator == null))
            {
                if (GUILayout.Button("Run Batch", GUILayout.Height(28)))
                {
                    RunBatch();
                }
            }

            if (targetGenerator == null)
            {
                EditorGUILayout.HelpBox("Assign a DungeonGenerator from the scene.", MessageType.Info);
                return;
            }

            if (batchResults.Count <= 0) return;

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawSummary();
            DrawFailures();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Generates every seed in the batch, collecting results and checking layouts.
        /// </summary>
        private void RunBatch()
        {
            batchResults = new List<GenerationResult>(runCount);
            nondeterministicSeeds = new List<int>();
            layoutViolations = new Dictionary<int, List<string>>();

            var seedSource = new System.Random();

            try
            {
                for (var runIndex = 0; runIndex < runCount; runIndex++)
                {
                    var progress = (float)runIndex / runCount;
                    if (EditorUtility.DisplayCancelableProgressBar("Dungeon Batch Tester", $"Seed {runIndex + 1} of {runCount}", progress)) break;

                    var seed = startSeed != 0 ? startSeed + runIndex : seedSource.Next(1, int.MaxValue);
                    if (seed == 0) seed = 1;

                    var generationResult = targetGenerator.GenerateDungeon(seed, true);
                    batchResults.Add(generationResult);

                    if (!generationResult.Succeeded) continue;

                    var violations = DungeonLayoutValidator.Validate(targetGenerator);
                    if (0 < violations.Count)
                    {
                        layoutViolations[seed] = violations;
                    }

                    if (checkDeterminism)
                    {
                        var firstLayout = DungeonLayoutValidator.Fingerprint(targetGenerator);
                        targetGenerator.GenerateDungeon(seed, true);

                        if (firstLayout != DungeonLayoutValidator.Fingerprint(targetGenerator))
                        {
                            nondeterministicSeeds.Add(seed);
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (!keepLastLayout)
                {
                    targetGenerator.ResetEnvironment();
                }
            }

            Debug.Log(BuildReport(), targetGenerator);
        }

        /// <summary>
        /// Draws the success rate, backtracking and timing statistics.
        /// </summary>
        private void DrawSummary()
        {
            var succeededCount = batchResults.Count(batchResult => batchResult.Succeeded);
            var failedCount = batchResults.Count - succeededCount;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Runs", batchResults.Count.ToString());
            EditorGUILayout.LabelField("Succeeded", $"{succeededCount} ({Percent(succeededCount)})");
            EditorGUILayout.LabelField("Failed", $"{failedCount} ({Percent(failedCount)})");
            EditorGUILayout.LabelField("Layout Rule Violations", layoutViolations.Count.ToString());

            if (checkDeterminism)
            {
                EditorGUILayout.LabelField("Nondeterministic Seeds", nondeterministicSeeds.Count.ToString());
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Backtracks (avg / max)", $"{batchResults.Average(batchResult => batchResult.BacktrackCount):0.0} / {batchResults.Max(batchResult => batchResult.BacktrackCount)}");
            EditorGUILayout.LabelField("Time ms (avg / max)", $"{batchResults.Average(batchResult => batchResult.ElapsedMilliseconds):0.0} / {batchResults.Max(batchResult => batchResult.ElapsedMilliseconds):0.0}");
        }

        /// <summary>
        /// Draws failure reasons and the seeds that failed, each with a button to replay it.
        /// </summary>
        private void DrawFailures()
        {
            var failedResults = batchResults.Where(batchResult => !batchResult.Succeeded).ToList();

            if (0 < failedResults.Count)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Failure Reasons", EditorStyles.boldLabel);

                foreach (var errorGroup in failedResults.GroupBy(batchResult => batchResult.Error).OrderByDescending(errorGroup => errorGroup.Count()))
                {
                    EditorGUILayout.LabelField($"{errorGroup.Key} ({errorGroup.Count()})", GenerationResult.Describe(errorGroup.Key), EditorStyles.wordWrappedLabel);
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Failed Seeds", EditorStyles.boldLabel);

                foreach (var failedResult in failedResults)
                {
                    DrawSeedRow(failedResult.Seed, failedResult.Error.ToString());
                }
            }

            if (0 < layoutViolations.Count)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Layout Rule Violations", EditorStyles.boldLabel);

                foreach (var (seed, violations) in layoutViolations)
                {
                    DrawSeedRow(seed, string.Join("; ", violations));
                }
            }

            if (0 < nondeterministicSeeds.Count)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Nondeterministic Seeds", EditorStyles.boldLabel);

                foreach (var seed in nondeterministicSeeds)
                {
                    DrawSeedRow(seed, "Layout differed on the second generation");
                }
            }
        }

        /// <summary>
        /// Draws a seed with a description and a button that regenerates it in the scene.
        /// </summary>
        private void DrawSeedRow(int seed, string description)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(seed.ToString(), GUILayout.Width(100), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);

                if (GUILayout.Button("Replay", GUILayout.Width(60)))
                {
                    targetGenerator.GenerateDungeon(seed);
                    Selection.activeGameObject = targetGenerator.gameObject;
                }
            }
        }

        /// <summary>
        /// Builds a plain-text report of the batch for the console, including every seed that needs a look.
        /// </summary>
        private string BuildReport()
        {
            var succeededCount = batchResults.Count(batchResult => batchResult.Succeeded);
            var reportBuilder = new StringBuilder();

            reportBuilder.AppendLine($"[Dungeon Batch Tester] {succeededCount}/{batchResults.Count} succeeded ({Percent(succeededCount)}), " +
                                     $"{layoutViolations.Count} layout rule violations" +
                                     (checkDeterminism ? $", {nondeterministicSeeds.Count} nondeterministic" : string.Empty));
            reportBuilder.AppendLine($"Backtracks avg {batchResults.Average(batchResult => batchResult.BacktrackCount).ToString("0.0", CultureInfo.InvariantCulture)}, max {batchResults.Max(batchResult => batchResult.BacktrackCount)}; " +
                                     $"time avg {batchResults.Average(batchResult => batchResult.ElapsedMilliseconds).ToString("0.0", CultureInfo.InvariantCulture)} ms, max {batchResults.Max(batchResult => batchResult.ElapsedMilliseconds).ToString("0.0", CultureInfo.InvariantCulture)} ms");

            foreach (var failedResult in batchResults.Where(batchResult => !batchResult.Succeeded))
            {
                reportBuilder.AppendLine($"  FAILED seed {failedResult.Seed}: {failedResult.Error}");
            }

            foreach (var (seed, violations) in layoutViolations)
            {
                reportBuilder.AppendLine($"  RULES seed {seed}: {string.Join("; ", violations)}");
            }

            foreach (var seed in nondeterministicSeeds)
            {
                reportBuilder.AppendLine($"  NONDETERMINISTIC seed {seed}");
            }

            return reportBuilder.ToString();
        }

        private string Percent(int count)
        {
            return batchResults.Count <= 0 ? "0%" : ((float)count / batchResults.Count).ToString("0.#%", CultureInfo.InvariantCulture);
        }
    }
}
