using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// Generates many seeds in a row with a <see cref="DungeonGenerator"/> and reports success rates, failure reasons,
    /// backtracking, timing and layout rule violations, so generator changes can be checked before and after.
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
        /// The report from the most recent batch, or <c>null</c> before the first run.
        /// </summary>
        private DungeonBatchReport batchReport;

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

            if (batchReport == null || batchReport.Results.Count <= 0) return;

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawSummary();
            DrawFailures();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Generates every seed in the batch with a cancelable progress bar, then logs the report.
        /// </summary>
        private void RunBatch()
        {
            try
            {
                batchReport = DungeonBatchRunner.Run(targetGenerator, runCount, startSeed, checkDeterminism, (runIndex, totalRuns) =>
                    EditorUtility.DisplayCancelableProgressBar("Dungeon Batch Tester", $"Seed {runIndex + 1} of {totalRuns}", (float)runIndex / totalRuns));
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (!keepLastLayout)
                {
                    targetGenerator.ResetEnvironment();
                }
            }

            Debug.Log(batchReport.ToString(), targetGenerator);
        }

        /// <summary>
        /// Draws the success rate, backtracking and timing statistics.
        /// </summary>
        private void DrawSummary()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Runs", batchReport.Results.Count.ToString());
            EditorGUILayout.LabelField("Succeeded", $"{batchReport.SucceededCount} ({batchReport.Percent(batchReport.SucceededCount)})");
            EditorGUILayout.LabelField("Failed", $"{batchReport.FailedCount} ({batchReport.Percent(batchReport.FailedCount)})");
            EditorGUILayout.LabelField("Layout Rule Violations", batchReport.LayoutViolations.Count.ToString());

            if (batchReport.CheckedDeterminism)
            {
                EditorGUILayout.LabelField("Nondeterministic Seeds", batchReport.NondeterministicSeeds.Count.ToString());
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Backtracks (avg / max)", $"{batchReport.AverageBacktracks:0.0} / {batchReport.MaximumBacktracks}");
            EditorGUILayout.LabelField("Time ms (avg / max)", $"{batchReport.AverageMilliseconds:0.0} / {batchReport.MaximumMilliseconds:0.0}");
        }

        /// <summary>
        /// Draws failure reasons and every seed that needs a look, each with a button to replay it.
        /// </summary>
        private void DrawFailures()
        {
            var failedResults = batchReport.Results.Where(batchResult => !batchResult.Succeeded).ToList();

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

            if (0 < batchReport.LayoutViolations.Count)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Layout Rule Violations", EditorStyles.boldLabel);

                foreach (var violationEntry in batchReport.LayoutViolations)
                {
                    DrawSeedRow(violationEntry.Key, string.Join("; ", violationEntry.Value));
                }
            }

            if (0 < batchReport.NondeterministicSeeds.Count)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("Nondeterministic Seeds", EditorStyles.boldLabel);

                foreach (var seed in batchReport.NondeterministicSeeds)
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
    }
}
