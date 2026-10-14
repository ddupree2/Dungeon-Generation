using StarStoneStudio.DungeonGeneration.Attributes;
using StarStoneStudio.DungeonGeneration.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.Events;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Procedurally constructs a dungeon layout at runtime using a selected <see cref="DungeonTheme"/>.
    /// This generator selects module categories and modules based on weighted probabilities and spawns them into the scene to create a randomized, replayable dungeon experience.
    /// </summary>
    [ExecuteAlways]
    public class DungeonGenerator : MonoBehaviour
    {
        private const int PlacementLimit = 25;
        private const int SamplingLimit = 100;
        private const float PlacementThreshold = -1f;
        private const float RequiredChance = 0.5f;

        [SerializeField]
        [Tooltip("The seed used to initialize the random number generation, ensuring reproducible generation results.")]
        private int generationSeed;
        
        [SerializableDictionary("Theme", "Occurrence Rate")]
        [SerializeField]
        [Tooltip("References to the available dungeon theme assets used by this dungeon.")]
        private SerializableDictionary<DungeonTheme, float> possibleThemes;
        
        [Required]
        [SerializeField]
        [Tooltip("Specifies which layers are considered when detecting overlaps with existing modules.")]
        private LayerMask placementLayers;
        
        [SerializeField]
        [Tooltip("Determines whether the generator permits cyclic connections between modules.")]
        private bool enableLoops;
        
        [SerializeField]
        [Tooltip("Called when generation has failed.")]
        private UnityEvent onGenerationFailed;
        
        [SerializeField]
        [Tooltip("Called when generation has completed without error.")]
        private UnityEvent onGenerationSuccess;
        
        /// <summary>
        /// The currently selected <see cref="DungeonTheme"/> for generation.
        /// </summary>
        private DungeonTheme selectedTheme;
        
        /// <summary>
        /// The current number of successfully instantiated modules.
        /// </summary>
        private int moduleCount;
        
        /// <summary>
        /// The maximum number of modules allowed in the current generation pass.
        /// </summary>
        private int moduleLimit;
        
        /// <summary>
        /// A stack storing the historical sequence of module placements during generation.
        /// </summary>
        private Stack<HistoryEntry> moduleHistory;

        /// <summary>
        /// A collection of currently active <see cref="DungeonModule"/> that contain at least one open connection point.
        /// </summary>
        private List<DungeonModule> connectableModules;

        /// <summary>
        /// The alias probability table for sampling <see cref="DungeonTheme"/> values.
        /// </summary>
        private AliasProbability<DungeonTheme> themeProbabilities;
        
        /// <summary>
        /// The alias probability table for sampling <see cref="ModuleCategory"/> values.
        /// </summary>
        private AliasProbability<ModuleCategory> categoryProbabilities;
        
        /// <summary>
        /// The alias probability tables for each <see cref="ModuleCategory"/> used to sample associated <see cref="ModuleAsset"/> values.
        /// </summary>
        private Dictionary<ModuleCategory, AliasProbability<ModuleAsset>> moduleProbabilities;

        /// <summary>
        /// Tracks how many times each <see cref="ModuleCategory"/> has been spawned during generation.
        /// </summary>
        private FrequencyDictionary<ModuleCategory> categorizedModules;

        /// <summary>
        /// A frequency-tracked collection of required <see cref="ModuleAsset"/> and <see cref="ModuleCategory"/> pairs.
        /// </summary>
        private FrequencyDictionary<(ModuleAsset, ModuleCategory)> requiredModules;
        
        /// <summary>
        /// Spawn-once <see cref="ModuleAsset"/> instances that are already placed or reserved as required modules.
        /// </summary>
        private List<ModuleAsset> uniqueModules;
        
        /// <summary>
        /// Tracks the number of backtracking attempts made during generation.
        /// </summary>
        private int backtrackAttempts;

        /// <summary>
        /// Defines the maximum number of backtracking attempts permitted during generation.
        /// </summary>
        private int backtrackLimit;
        
        /// <summary>
        /// Represents the error encountered during the generation process.
        /// </summary>
        private GenerationError generationError;

        /// <summary>
        /// Ensures <see cref="DungeonTheme"/> probabilities are initialized before use.
        /// </summary>
        private void Awake()
        {
            InitializeThemes();
        }
        
        /// <summary>
        /// Creates the <see cref="AliasProbability{TObject}"/> table from the current theme weights provided by <see cref="possibleThemes"/>.
        /// </summary>
        private void InitializeThemes()
        {
            themeProbabilities = new AliasProbability<DungeonTheme>(
                possibleThemes.Keys.ToList(),
                possibleThemes.Values.ToList()
            );
        }

        /// <summary>
        /// Generates a randomized dungeon by completing the following steps:
        /// <list type="bullet">
        /// <item><description>Picking a random <see cref="DungeonTheme"/> from the available themes</description></item>
        /// <item><description>Setting up probability tables for the weighted category and module selection</description></item>
        /// <item><description>Spawning modules until the target count is reached, respecting required category constraints</description></item>
        /// </list>
        /// </summary>
        public void GenerateDungeon()
        {
            // Clear the current generation iteration (if any) and reset the parameters used by the generator
            ResetEnvironment();
            InitializeGeneration();

            // Every loop below is capped so an unsatisfiable theme fails with an error instead of freezing the editor
            var iterationCount = 0;
            var iterationLimit = moduleLimit * PlacementLimit;

            while (generationError == GenerationError.None && moduleCount < moduleLimit)
            {
                if (iterationLimit <= iterationCount++)
                {
                    generationError = GenerationError.IterationLimitExceeded;
                    break;
                }

                var openSlots = moduleLimit - moduleCount;
                if (openSlots < requiredModules.Count)
                {
                    generationError = GenerationError.InsufficientSpace;
                    break;
                }

                if (openSlots == requiredModules.Count)
                {
                    InstantiateRequiredModule();
                    continue;
                }

                if (0 < requiredModules.Count)
                {
                    var requiredUrgency = Mathf.Clamp01((float)requiredModules.Count / openSlots);
                    var requiredChance = Mathf.Lerp(RequiredChance, 1f, requiredUrgency);
                    var randomChance = UnityEngine.Random.value;

                    if (randomChance <= requiredChance)
                    {
                        InstantiateRequiredModule();
                        continue;
                    }
                }

                if (categoryProbabilities == null)
                {
                    generationError = GenerationError.NoOptionalCategories;
                    break;
                }

                var chosenCategory = categoryProbabilities.Sample();
                if (chosenCategory.SpawnLimits)
                {
                    if (chosenCategory.SpawnMaximum <= categorizedModules[chosenCategory]) continue;

                    var categoryCount = 0;
                    var categoryLimit = UnityEngine.Random.Range(chosenCategory.SpawnMinimum, chosenCategory.SpawnMaximum + 1);

                    if (openSlots < categoryLimit)
                    {
                        categoryLimit = Math.Min(
                            openSlots,
                            chosenCategory.SpawnMaximum - categorizedModules[chosenCategory]
                        );
                    }

                    var anticipatedCount = categorizedModules[chosenCategory] + categoryLimit;
                    if (anticipatedCount > chosenCategory.SpawnMaximum) continue;

                    var categoryAttempts = 0;
                    while (generationError == GenerationError.None && categoryCount < categoryLimit)
                    {
                        if (SamplingLimit <= categoryAttempts++)
                        {
                            generationError = GenerationError.CategoryPlacementFailed;
                            break;
                        }

                        if (InstantiateWeightedModule(chosenCategory))
                        {
                            categoryCount++;
                        }
                    }
                }
                else
                {
                    InstantiateWeightedModule(chosenCategory);
                }
            }

            // Disable any remaining open entrances
            foreach (var connectableModule in connectableModules)
            {
                connectableModule.ToggleConnectableEntrances(false);                
            }
            
            #if UNITY_EDITOR

            if (generationError != GenerationError.None)
            {
                var errorMessage = generationError switch
                {
                    GenerationError.InsufficientSpace => "Insufficient space for module placement.",
                    GenerationError.InsufficientRequiredSpace => "Insufficient space to place all required modules.",
                    GenerationError.InvalidHistory => "No generation history available for backtracking.",
                    GenerationError.InvalidBacktracking => "Backtracking limit exceeded.",
                    GenerationError.IterationLimitExceeded => "Generation loop limit exceeded. Optional categories may be capped below the module count.",
                    GenerationError.CategoryPlacementFailed => "Could not place enough modules for a spawn-limited category.",
                    GenerationError.RequiredSamplingFailed => "Could not reserve enough modules for a required category. It may not have enough spawn-once modules.",
                    GenerationError.NoOptionalCategories => "The theme has no optional categories to fill the remaining module slots.",
                    _ => "Unknown Error"
                };
                
                Debug.LogError($"[{name}] Generation Failed: {errorMessage}");
            }
            else
            {
                Debug.Log($"<color=green>[{name}] <b>Dungeon Generation Complete: {moduleLimit}</b></color>");
            }

            #endif
            
            var generationFailed = generationError != GenerationError.None || moduleCount < moduleLimit;
            if (generationFailed)
            {
                onGenerationFailed.Invoke();
            }
            else
            {
                onGenerationSuccess.Invoke();
            }
        }
        
        /// <summary>
        /// Initializes all data structures, probability tables, and variables required to begin generation.
        /// </summary>
        /// <remarks>
        /// It must be called before any generation logic to ensure all dependencies are properly initialized and cleared.
        /// </remarks>
        private void InitializeGeneration()
        {
            if (generationSeed != 0)
            {
                UnityEngine.Random.InitState(generationSeed);
            }
            
            if (themeProbabilities == null)
            {
                InitializeThemes();
            }
            
            selectedTheme = themeProbabilities.Sample();
            
            moduleCount = 0;
            moduleLimit = UnityEngine.Random.Range(selectedTheme.MinimumModules, selectedTheme.MaximumModules + 1);
            moduleHistory = new Stack<HistoryEntry>();
            connectableModules = new List<DungeonModule>();
            
            // Create alias probability lookup tables for drawing random values in O(1) time
            InitializeCategoryProbabilities();
            InitializeCategorizedModuleProbabilities();

            categorizedModules = new FrequencyDictionary<ModuleCategory>();

            foreach (var moduleElement in selectedTheme.ModuleData)
            {
                categorizedModules[moduleElement.ModuleCategory] = 0;
            }

            InitializeRequiredModules();
            
            // Reset backtracking counters and limits before starting generation
            backtrackAttempts = 0;
            backtrackLimit = moduleLimit * PlacementLimit;
            generationError = GenerationError.None;
        }

        /// <summary>
        /// Creates an <see cref="AliasProbability{TObject}"/> table for all non-required module categories.
        /// </summary>
        /// <returns>An alias probability lookup table for selecting <see cref="ModuleCategory"/> objects.</returns>
        private void InitializeCategoryProbabilities()
        {
            var categoryList = new List<ModuleCategory>();
            var weightsList = new List<float>();

            foreach (var moduleElement in selectedTheme.ModuleData)
            {
                var moduleCategory = moduleElement.ModuleCategory;
                var moduleWeight = moduleCategory.SpawnRate;

                if (moduleCategory.SpawnRequired) continue;

                categoryList.Add(moduleCategory);
                weightsList.Add(moduleWeight);
            }

            // A theme made only of required categories has nothing to sample from
            categoryProbabilities = 0 < categoryList.Count
                ? new AliasProbability<ModuleCategory>(categoryList, weightsList)
                : null;
        }

        /// <summary>
        /// Creates alias probability tables for all modules in the theme, grouped by their category.
        /// </summary>
        /// <returns>
        /// A dictionary mapping <see cref="ModuleCategory"/> to <see cref="AliasProbability{TObject}"/> instances for sampling module entries within that category.
        /// </returns>
        private void InitializeCategorizedModuleProbabilities()
        {
            moduleProbabilities = new Dictionary<ModuleCategory, AliasProbability<ModuleAsset>>();

            // Create alias probability lookup tables for module entries in each module category
            foreach (var moduleElement in selectedTheme.ModuleData)
            {
                moduleProbabilities[moduleElement.ModuleCategory] = new AliasProbability<ModuleAsset>(
                    moduleElement.ModuleAssets,
                    moduleElement.ModuleAssets.Select(categorizedEntry => categorizedEntry.SpawnRate).ToList()
                );
            }
        }

        /// <summary>
        /// Populates the <see cref="requiredModules"/> and <see cref="uniqueModules"/> lists with modules that are marked as required for the current <see cref="selectedTheme"/>.
        /// </summary>
        private void InitializeRequiredModules()
        {
            requiredModules = new FrequencyDictionary<(ModuleAsset, ModuleCategory)>();
            uniqueModules = new List<ModuleAsset>();

            foreach (var moduleElement in selectedTheme.ModuleData)
            {
                var openSlots = moduleLimit - requiredModules.Count;
                if (openSlots <= 0)
                {
                    generationError = GenerationError.InsufficientRequiredSpace;
                    break;
                }

                var moduleCategory = moduleElement.ModuleCategory;
                if (moduleCategory.SpawnRequired == false) continue;
                if (moduleCategory.SpawnLimits)
                {
                    var spawnCount = 0;
                    var spawnLimit = UnityEngine.Random.Range(moduleCategory.SpawnMinimum, moduleCategory.SpawnMaximum + 1);

                    if (openSlots < spawnLimit)
                    {
                        if (openSlots < moduleCategory.SpawnMinimum)
                        {
                            generationError = GenerationError.InsufficientRequiredSpace;
                            break;
                        }

                        spawnLimit = Math.Min(
                            openSlots,
                            moduleCategory.SpawnMaximum - categorizedModules[moduleCategory]
                        );
                    }

                    var samplingAttempts = 0;
                    while (spawnCount < spawnLimit)
                    {
                        if (SamplingLimit <= samplingAttempts++)
                        {
                            generationError = GenerationError.RequiredSamplingFailed;
                            return;
                        }

                        var moduleAsset = SampleWeightedModule(moduleCategory);
                        if (moduleAsset == null) continue;

                        RegisterRequiredModule(moduleAsset, moduleCategory);
                        spawnCount++;
                    }
                }
                else
                {
                    var moduleAsset = SampleWeightedModule(moduleCategory);
                    if (moduleAsset == null) continue;

                    RegisterRequiredModule(moduleAsset, moduleCategory);
                }
            }
        }
        
        /// <summary>
        /// Registers a module as required by incrementing its count in <see cref="requiredModules"/>.
        /// </summary>
        /// <param name="moduleAsset">The <see cref="ModuleAsset"/> being registered.</param>
        /// <param name="moduleCategory">The <see cref="ModuleCategory"/> of the module being registered.</param>
        private void RegisterRequiredModule(ModuleAsset moduleAsset, ModuleCategory moduleCategory)
        {
            // Reserve spawn-once modules up front so weighted sampling can't place them a second time
            if (moduleAsset.SpawnOnce)
            {
                uniqueModules.Add(moduleAsset);
            }

            if (requiredModules.ContainsKey((moduleAsset, moduleCategory)) == false)
            {
                requiredModules[(moduleAsset, moduleCategory)] = 0;    
            }
                    
            requiredModules[(moduleAsset, moduleCategory)]++;
        }
        
        /// <summary>
        /// Attempts to instantiate a required module from the remaining <see cref="requiredModules"/> pool.
        /// </summary>
        /// <returns>
        /// <c>true</c> if a required module was successfully instantiated; otherwise, <c>false</c>.
        /// </returns>
        private bool InstantiateRequiredModule()
        {
            var requiredModule = SampleRequiredModule();
            if (requiredModule is null) return false;
        
            var (moduleAsset, moduleCategory) = requiredModule.Value;
            
            return TryPlacingModule(moduleAsset, moduleCategory, true);
        }

        /// <summary>
        /// Attempts to instantiate a non-required module from the provided weighted category pool.
        /// </summary>
        /// <param name="moduleCategory">The <see cref="ModuleCategory"/> from which to sample a weighted module.</param>
        /// <returns>
        /// <c>true</c> if a weighted module was successfully instantiated; otherwise, <c>false</c>.
        /// </returns>
        private bool InstantiateWeightedModule(ModuleCategory moduleCategory)
        {
            var moduleAsset = SampleWeightedModule(moduleCategory);
            if (moduleAsset is null) return false;

            return TryPlacingModule(moduleAsset, moduleCategory, false);
        }

        /// <summary>
        /// Randomly selects a required module from the remaining pool of required modules.
        /// </summary>
        /// <returns>
        /// A tuple containing the <see cref="ModuleAsset"/> and its <see cref="ModuleCategory"/> if available; otherwise, <c>null</c>.
        /// </returns>
        private (ModuleAsset, ModuleCategory)? SampleRequiredModule()
        {
            var pendingModules = requiredModules.Where(requiredPair => 0 < requiredPair.Value).ToList();
            if (pendingModules.Count <= 0) return null;

            var targetIndex = UnityEngine.Random.Range(0, pendingModules.Count);
            return pendingModules[targetIndex].Key;
        }

        /// <summary>
        /// Samples a module from the alias probability table for the specified category.
        /// </summary>
        /// <param name="moduleCategory">The <see cref="ModuleCategory"/> from which to sample the module.</param>
        /// <returns>
        /// The sampled <see cref="ModuleAsset"/> if valid; otherwise, <c>null</c> if the asset cannot be used.
        /// </returns>
        /// <remarks>
        /// Sampling does not consume spawn-once modules; they are marked as used in <see cref="RecordModule"/> once actually placed.
        /// </remarks>
        private ModuleAsset SampleWeightedModule(ModuleCategory moduleCategory)
        {
            var chosenModule = moduleProbabilities[moduleCategory].Sample();
            if (chosenModule.SpawnOnce && uniqueModules.Contains(chosenModule)) return null;

            return chosenModule;
        }

        /// <summary>
        /// Attempts to place the specified <see cref="ModuleAsset"/> within a limited number of attempts.
        /// </summary>
        /// <param name="moduleAsset">The <see cref="ModuleAsset"/> to place.</param>
        /// <param name="moduleCategory">The <see cref="ModuleCategory"/> associated with the module.</param>
        /// <param name="isRequired">Indicates whether the module is part of the required set.</param>
        /// <returns><c>true</c> if the module was successfully instantiated and placed; otherwise, <c>false</c>.</returns>
        private bool TryPlacingModule(ModuleAsset moduleAsset, ModuleCategory moduleCategory, bool isRequired)
        {
            var placementAttempts = 0;
            while (generationError == GenerationError.None && placementAttempts < PlacementLimit)
            {
                if (TryInstantiatingModule(moduleAsset, moduleCategory, isRequired)) return true;
                placementAttempts++;
            }

            // Backtracking frees up space but places nothing, so the caller must retry
            if (generationError == GenerationError.None && 0 < moduleHistory.Count)
            {
                TryBacktracking();
            }

            return false;
        }
        
        /// <summary>
        /// Attempts to instantiate and position a module in the current dungeon layout.
        /// </summary>
        /// <param name="moduleAsset">The <see cref="ModuleAsset"/> to instantiate.</param>
        /// <param name="moduleCategory">The <see cref="ModuleCategory"/> associated with the module.</param>
        /// <param name="isRequired">Indicates whether the module is part of the required set.</param>
        /// <returns>
        /// <c>true</c> if the module was successfully instantiated and placed; otherwise, <c>false</c>.
        /// </returns>
        private bool TryInstantiatingModule(ModuleAsset moduleAsset, ModuleCategory moduleCategory, bool isRequired)
        {
            if (0 < moduleCount && connectableModules.Count <= 0)
            {
                TryBacktracking();
                return false;
            }

            var moduleInstance = Instantiate(moduleAsset.ModulePrefab, transform);

            var instantiatedModule = moduleInstance.GetComponent<DungeonModule>();
            var instantiatedEntrance = instantiatedModule.SelectConnectableEntrance();

            if (connectableModules.Count <= 0)
            {
                connectableModules.Add(instantiatedModule);

                RecordModule(new HistoryEntry(moduleAsset, moduleCategory, instantiatedModule, null, null, isRequired));
                return true;
            }

            var connectedModule = connectableModules[UnityEngine.Random.Range(0, connectableModules.Count)];
            var connectedEntrance = connectedModule.SelectConnectableEntrance();

            AlignModules(moduleInstance.transform, instantiatedEntrance.EntrancePoint, connectedEntrance.EntrancePoint);

            if (ContainsIntersections(instantiatedModule))
            {
                // DestroyModule(moduleInstance);
                DestroyModule(instantiatedModule);
                return false;
            }

            instantiatedModule.RemoveConnectableEntrance(instantiatedEntrance);
            connectedModule.RemoveConnectableEntrance(connectedEntrance);

            var secondaryEntrances = enableLoops ? ResolveSecondaryConnections(instantiatedModule) : null;

            RefreshConnections(instantiatedModule);
            RefreshConnections(connectedModule);

            RecordModule(new HistoryEntry(moduleAsset, moduleCategory, instantiatedModule, connectedEntrance, secondaryEntrances, isRequired));
            return true;
        }

        /// <summary>
        /// Aligns a module's transform to connect its entrance with another module's entrance.
        /// </summary>
        /// <param name="instantiatedObject">The <see cref="Transform"/> of the module being positioned.</param>
        /// <param name="instantiatedEntrance">The <see cref="Transform"/> of the entrance point on the instantiated module.</param>
        /// <param name="connectableEntrance">The <see cref="Transform"/> of the entrance point on the existing module.</param>
        private static void AlignModules(Transform instantiatedObject, Transform instantiatedEntrance, Transform connectableEntrance)
        {
            var rotationOffset = Mathf.DeltaAngle(instantiatedEntrance.eulerAngles.y, connectableEntrance.eulerAngles.y) + 180f;

            instantiatedObject.Rotate(0, rotationOffset, 0);
            instantiatedObject.position = instantiatedObject.position - instantiatedEntrance.position + connectableEntrance.position;

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Determines whether the provided <see cref="DungeonModule"/> intersect with any existing geometry.
        /// </summary>
        /// <param name="targetModule">The <see cref="DungeonModule"/> whose colliders will be tested for intersections.</param>
        /// <returns>
        /// <c>true</c> if the <see cref="DungeonModule"/>'s colliders overlap with any other colliders in the <see cref="placementLayers"/>; otherwise, <c>false</c>.
        /// </returns>
        private bool ContainsIntersections(DungeonModule targetModule)
        {
            foreach (var moduleBounds in targetModule.ModuleBounds)
            {
                var hitContacts = Physics.OverlapBox(moduleBounds.bounds.center, moduleBounds.bounds.extents, Quaternion.identity, placementLayers);
                
                foreach (var hitContact in hitContacts)
                {
                    var hitModule = hitContact.transform.GetComponentInParent<DungeonModule>();
                    if (hitModule != targetModule) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Iterates through the given <see cref="DungeonModule"/>'s available entrances and resolves secondary connections to other modules where possible.
        /// </summary>
        /// <param name="targetModule">
        /// The <see cref="DungeonModule"/> whose entrances should be evaluated for secondary connections.
        /// </param>
        /// <returns>The entrances on other modules that were closed, so backtracking can reopen them.</returns>
        private List<DungeonPassage> ResolveSecondaryConnections(DungeonModule targetModule)
        {
            var closedEntrances = new List<DungeonPassage>();

            // Create a copy of the module's connectable entrances to prevent errors since we will be dynamically updating this list
            foreach (var originEntrance in targetModule.ConnectableEntrances.ToList())
            {
                var connectableEntrance = DetectConnectableEntrance(originEntrance);
                if (connectableEntrance is null) continue;

                var connectableModule = connectableEntrance.DungeonModule;

                targetModule.RemoveConnectableEntrance(originEntrance);
                connectableModule.RemoveConnectableEntrance(connectableEntrance);
                closedEntrances.Add(connectableEntrance);

                RefreshConnections(connectableModule);
            }

            return closedEntrances;
        }

        /// <summary>
        /// Detects a <see cref="DungeonPassage"/> that can connect to the given <see cref="DungeonPassage"/>.
        /// A compatible entrance is one that is physically aligned and facing roughly opposite to the origin entrance.
        /// </summary>
        /// <param name="originEntrance">The <see cref="DungeonPassage"/> from which to check for a connectable partner.</param>
        /// <returns>
        /// A <see cref="DungeonPassage"/> representing the connectable entrance on another <see cref="DungeonModule"/>.
        /// </returns>
        private static DungeonPassage DetectConnectableEntrance(DungeonPassage originEntrance)
        {
            var raycastModule = new Ray(originEntrance.EntrancePoint.position, originEntrance.EntrancePoint.forward);

            if (Physics.Raycast(raycastModule, out var raycastHit, 0.05f))
            {
                var hitModule = raycastHit.collider.GetComponentInParent<DungeonModule>();
                if (hitModule == null) return null;

                var originDirection = originEntrance.EntrancePoint.forward;
                foreach (var candidateEntrance in hitModule.ConnectableEntrances)
                {
                    var candidateDirection = candidateEntrance.EntrancePoint.forward;
                    if (Vector3.Dot(candidateDirection, originDirection) < PlacementThreshold) return candidateEntrance;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Updates <see cref="connectableModules"/> to include or exclude a <see cref="DungeonModule"/> based on its available entrances.
        /// </summary>
        /// <param name="targetModule">The <see cref="DungeonModule"/> whose connection state is being updated.</param>
        private void RefreshConnections(DungeonModule targetModule)
        {
            if (0 < targetModule.ConnectableEntrances.Count)
            {
                if (connectableModules.Contains(targetModule)) return;
                connectableModules.Add(targetModule);
            }
            else
            {
                connectableModules.Remove(targetModule);
            }
        }

        /// <summary>
        /// Records the result of a successful module instantiation to the <see cref="moduleHistory"/>.
        /// </summary>
        /// <param name="historyEntry">The <see cref="HistoryEntry"/> describing the spawned module.</param>
        private void RecordModule(HistoryEntry historyEntry)
        {
            moduleCount++;
            moduleHistory.Push(historyEntry);

            if (historyEntry.IsRequired)
            {
                requiredModules[(historyEntry.ModuleAsset, historyEntry.ModuleCategory)]--;
            }
            else if (historyEntry.ModuleAsset.SpawnOnce)
            {
                // Required spawn-once modules were already reserved in RegisterRequiredModule
                uniqueModules.Add(historyEntry.ModuleAsset);
            }

            categorizedModules[historyEntry.ModuleCategory]++;
        }

        /// <summary>
        /// Attempts to backtrack the generation process by removing the most recently placed module.
        /// </summary>
        /// <returns>
        /// <c>true</c> if backtracking succeeded; otherwise, <c>false</c>.
        /// </returns>
        private bool TryBacktracking()
        {
            if (moduleHistory.Count <= 0)
            {
                generationError = GenerationError.InvalidHistory;
                return false;
            }

            if (backtrackLimit <= backtrackAttempts)
            {
                generationError = GenerationError.InvalidBacktracking;
                return false;
            }

            var historyEntry = moduleHistory.Pop();

            moduleCount--;
            categorizedModules[historyEntry.ModuleCategory]--;
            
            ReopenEntrance(historyEntry.ConnectedEntrance);

            if (historyEntry.SecondaryEntrances != null)
            {
                foreach (var secondaryEntrance in historyEntry.SecondaryEntrances)
                {
                    ReopenEntrance(secondaryEntrance);
                }
            }

            if (historyEntry.IsRequired)
            {
                // Return it to the required pool; spawn-once modules stay reserved for the retry
                requiredModules[(historyEntry.ModuleAsset, historyEntry.ModuleCategory)]++;
            }
            else if (historyEntry.ModuleAsset.SpawnOnce)
            {
                uniqueModules.Remove(historyEntry.ModuleAsset);
            }

            DestroyModule(historyEntry.DungeonModule);
            
            backtrackAttempts++;
            return true;
        }

        /// <summary>
        /// Returns a previously connected <see cref="DungeonPassage"/> to its module's pool of connectable entrances.
        /// </summary>
        /// <param name="dungeonPassage">The <see cref="DungeonPassage"/> to reopen, if any.</param>
        private void ReopenEntrance(DungeonPassage dungeonPassage)
        {
            if (dungeonPassage is null) return;

            var connectedModule = dungeonPassage.DungeonModule;
            if (connectedModule == null) return;

            connectedModule.RegisterConnectableEntrance(dungeonPassage);
            RefreshConnections(connectedModule);
        }

        /// <summary>
        /// Destroys the specified <see cref="UnityEngine.Object"/>.
        /// </summary>
        /// <param name="targetObject">The <see cref="UnityEngine.Object"/> to be destroyed.</param>
        private void DestroyModule(DungeonModule targetObject)
        {
            connectableModules?.Remove(targetObject);

            // Destroy is deferred to the end of the frame in Play Mode; deactivating removes the colliders
            // from physics queries immediately so later overlap checks don't hit discarded modules
            targetObject.gameObject.SetActive(false);

            #if UNITY_EDITOR
            
            if (Application.isPlaying == false)
            {
                DestroyImmediate(targetObject.gameObject);
                return;
            }
            
            #endif
            
            Destroy(targetObject.gameObject);
        }
        
        /// <summary>
        /// Generates a NavMesh for the current <see cref="GameObject"/>.
        /// Ensures that a <see cref="NavMeshSurface"/> exists on the <see cref="GameObject"/> and builds the NavMesh.
        /// </summary>
        public void GenerateNavMesh()
        {
            if (transform.childCount <= 0)
            {
                #if UNITY_EDITOR
                
                Debug.LogError($"[{name}] NavMesh generation failed due to missing child objects.", gameObject);
                
                #endif
                
                return;
            }
            
            var navMeshSurface = gameObject.GetComponent<NavMeshSurface>();
            if (navMeshSurface == null)
            {
                navMeshSurface = gameObject.AddComponent<NavMeshSurface>();
            }

            navMeshSurface.BuildNavMesh();
        }
        
        /// <summary>
        /// Resets the environment by destroying all generated child objects and removing any associated <see cref="NavMeshSurface"/> data.
        /// </summary>
        public void ResetEnvironment()
        {
            for (var itemIndex = transform.childCount - 1; 0 <= itemIndex; itemIndex--)
            {
                var targetModule = transform.GetChild(itemIndex).gameObject.GetComponent<DungeonModule>();
                if (targetModule == null) continue;

                DestroyModule(targetModule);
            }

            var navMeshSurface = gameObject.GetComponent<NavMeshSurface>();
            if (navMeshSurface != null)
            {
                navMeshSurface.RemoveData();
            }
        }
        
        /// <summary>
        /// Represents the possible errors that can occur during dungeon generation.
        /// </summary>
        private enum GenerationError
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
            UnknownError = 999
        }

        /// <summary>
        /// Represents a single step in the history for the <see cref="DungeonGenerator"/> used for backtracking or undoing module placements.
        /// </summary>
        private readonly struct HistoryEntry
        {
            public readonly ModuleAsset ModuleAsset;
            public readonly ModuleCategory ModuleCategory;
            public readonly DungeonModule DungeonModule;
            public readonly DungeonPassage ConnectedEntrance;
            public readonly List<DungeonPassage> SecondaryEntrances;
            public readonly bool IsRequired;

            public HistoryEntry(ModuleAsset moduleAsset, ModuleCategory moduleCategory, DungeonModule dungeonModule, DungeonPassage connectedEntrance, List<DungeonPassage> secondaryEntrances, bool isRequired)
            {
                ModuleAsset = moduleAsset;
                ModuleCategory = moduleCategory;
                DungeonModule = dungeonModule;
                ConnectedEntrance = connectedEntrance;
                SecondaryEntrances = secondaryEntrances;
                IsRequired = isRequired;
            }
        }
    }
}