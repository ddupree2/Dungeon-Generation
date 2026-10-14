using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Asynchronous generation, deferred room activation and background NavMesh baking, for building the next floor while the
    /// player is still on the current one.
    /// </summary>
    public partial class DungeonGenerator
    {
        /// <summary>
        /// Measures time spent actually generating (paused between frames for asynchronous passes).
        /// </summary>
        private readonly System.Diagnostics.Stopwatch generationTimer = new();

        /// <summary>
        /// Renderers and lights disabled by <see cref="GenerationOptions.HideUntilComplete"/>, re-enabled when the pass ends.
        /// </summary>
        private readonly List<Component> hiddenComponents = new();

        private bool isGenerating;
        private bool hideNewModules;
        private bool modulesActivated;

        /// <summary>
        /// Whether a generation pass is in progress. While it is, new passes and <see cref="ResetEnvironment"/> are refused.
        /// </summary>
        public bool IsGenerating => isGenerating;

        /// <summary>
        /// Whether <see cref="IDungeonModuleListener.Activate"/> has been called for the current layout.
        /// </summary>
        public bool ModulesActivated => modulesActivated;

        /// <summary>
        /// Generates a dungeon over several frames, spending about <see cref="GenerationOptions.MillisecondsPerFrame"/> per frame,
        /// so a floor can be built in the background without a hitch. Produces exactly the same layout as
        /// <see cref="GenerateDungeon(int, GenerationOptions)"/> for the same seed. Play Mode only.
        /// </summary>
        /// <example>
        /// <code>
        /// var result = await nextFloor.GenerateDungeonAsync(seed, new GenerationOptions { HideUntilComplete = true, DeferActivation = true });
        /// await nextFloor.BuildNavMeshAsync();
        /// // ...when the player arrives:
        /// nextFloor.ActivateModules();
        /// </code>
        /// </example>
        /// <param name="seed">The seed to generate from, or <c>0</c> to pick a new random seed.</param>
        /// <param name="options">Frame budget, visibility, activation and logging options.</param>
        /// <param name="cancellationToken">Cancels the pass and clears the partial layout. Destroying the generator also cancels it.</param>
        /// <returns>The <see cref="GenerationResult"/> describing the outcome, including the seed that was used.</returns>
        /// <exception cref="OperationCanceledException">The pass was cancelled; the partial layout has been cleared.</exception>
        public async Awaitable<GenerationResult> GenerateDungeonAsync(int seed = 0, GenerationOptions options = default, CancellationToken cancellationToken = default)
        {
            if (RejectWhileGenerating(seed, out var rejectedResult)) return rejectedResult;

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, destroyCancellationToken);
            var generationSteps = GenerationSteps(seed, options);
            var frameBudget = options.ResolvedMillisecondsPerFrame;

            generationTimer.Reset();

            try
            {
                while (true)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    if (!RunFrame(generationSteps, frameBudget)) break;

                    await Awaitable.NextFrameAsync(linkedCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Disposing runs the steps' cleanup (clears the generating flag, restores hidden renderers)
                ((IDisposable)generationSteps).Dispose();

                if (this != null)
                {
                    ClearLayout();
                }

                throw;
            }

            return LastResult;
        }

        /// <summary>
        /// Runs generation steps until the frame budget is used up or the pass finishes.
        /// </summary>
        /// <returns><c>true</c> if there are steps left for another frame.</returns>
        private bool RunFrame(IEnumerator generationSteps, float frameBudget)
        {
            generationTimer.Start();
            var frameStart = generationTimer.Elapsed.TotalMilliseconds;

            bool hasMoreSteps;
            do
            {
                hasMoreSteps = generationSteps.MoveNext();
            }
            while (hasMoreSteps && generationTimer.Elapsed.TotalMilliseconds - frameStart < frameBudget);

            generationTimer.Stop();
            return hasMoreSteps;
        }

        /// <summary>
        /// Refuses a new pass while one is already running on this generator.
        /// </summary>
        private bool RejectWhileGenerating(int seed, out GenerationResult rejectedResult)
        {
            rejectedResult = default;
            if (!isGenerating) return false;

            Debug.LogWarning($"[{name}] A dungeon is already generating; the new request was ignored.", gameObject);
            rejectedResult = new GenerationResult(seed, GenerationError.GenerationInProgress, null, 0, 0, 0, 0);
            return true;
        }

        /// <summary>
        /// Calls <see cref="IDungeonModuleListener.Activate"/> on every listener in the current layout so room logic can start.
        /// Called automatically after a successful pass unless <see cref="GenerationOptions.DeferActivation"/> was set.
        /// Does nothing outside Play Mode, while generating, after a failed pass, or if the layout was already activated.
        /// </summary>
        public void ActivateModules()
        {
            if (!Application.isPlaying || isGenerating || modulesActivated || !LastResult.Succeeded) return;

            modulesActivated = true;

            foreach (var placedModule in placedModules)
            {
                foreach (var moduleListener in placedModule.GetComponentsInChildren<IDungeonModuleListener>(true))
                {
                    moduleListener.Activate(placedModule, this);
                }
            }
        }

        /// <summary>
        /// Bakes the NavMesh for the current layout in the background. Await the returned operation (or poll
        /// <see cref="AsyncOperation.isDone"/>) before letting agents use it. Only this generator's modules are baked.
        /// </summary>
        /// <returns>The background bake, or <c>null</c> if there is no layout to bake.</returns>
        public AsyncOperation BuildNavMeshAsync()
        {
            if (placedModules.Count <= 0)
            {
                Debug.LogWarning($"[{name}] There is no layout to build a NavMesh for.", gameObject);
                return null;
            }

            var navMeshSurface = EnsureNavMeshSurface();

            navMeshSurface.RemoveData();
            navMeshSurface.navMeshData = new NavMeshData(navMeshSurface.agentTypeID)
            {
                name = gameObject.name,
                position = navMeshSurface.transform.position,
                rotation = navMeshSurface.transform.rotation
            };
            navMeshSurface.AddData();

            return navMeshSurface.UpdateNavMesh(navMeshSurface.navMeshData);
        }

        /// <summary>
        /// Returns the generator's <see cref="NavMeshSurface"/>, adding one that bakes only this generator's children if needed,
        /// so several generators in a scene don't bake each other's floors.
        /// </summary>
        private NavMeshSurface EnsureNavMeshSurface()
        {
            var navMeshSurface = gameObject.GetComponent<NavMeshSurface>();
            if (navMeshSurface != null) return navMeshSurface;

            navMeshSurface = gameObject.AddComponent<NavMeshSurface>();
            navMeshSurface.collectObjects = CollectObjects.Children;

            return navMeshSurface;
        }

        /// <summary>
        /// Instantiates a module under the generator, hiding its renderers and lights if the pass was asked to.
        /// </summary>
        private GameObject InstantiateModule(GameObject modulePrefab)
        {
            var moduleInstance = Instantiate(modulePrefab, transform);

            if (hideNewModules)
            {
                HideComponents(moduleInstance.GetComponentsInChildren<Renderer>());
                HideComponents(moduleInstance.GetComponentsInChildren<Light>());
            }

            return moduleInstance;
        }

        private void HideComponents(Renderer[] renderers)
        {
            foreach (var moduleRenderer in renderers)
            {
                if (!moduleRenderer.enabled) continue;

                moduleRenderer.enabled = false;
                hiddenComponents.Add(moduleRenderer);
            }
        }

        private void HideComponents(Light[] lights)
        {
            foreach (var moduleLight in lights)
            {
                if (!moduleLight.enabled) continue;

                moduleLight.enabled = false;
                hiddenComponents.Add(moduleLight);
            }
        }

        /// <summary>
        /// Re-enables everything hidden by <see cref="GenerationOptions.HideUntilComplete"/>. Components on discarded modules are skipped.
        /// </summary>
        private void RevealHiddenModules()
        {
            foreach (var hiddenComponent in hiddenComponents)
            {
                switch (hiddenComponent)
                {
                    case Renderer moduleRenderer when moduleRenderer != null:
                        moduleRenderer.enabled = true;
                        break;

                    case Light moduleLight when moduleLight != null:
                        moduleLight.enabled = true;
                        break;
                }
            }

            hiddenComponents.Clear();
            hideNewModules = false;
        }
    }
}
