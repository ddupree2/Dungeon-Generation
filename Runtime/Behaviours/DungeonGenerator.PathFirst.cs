using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Path-first generation for <see cref="LayoutMode.PathFirst"/> themes:
    /// <list type="number">
    /// <item><description>Place one Start module at the origin.</description></item>
    /// <item><description>Grow the main path: each MainPath room attaches to the previous main-path room, never to an arbitrary module.</description></item>
    /// <item><description>Attach the Boss room to the last main-path room, then the End room to the Boss room's remaining doorway.</description></item>
    /// <item><description>Grow short branches of Branch rooms off leftover doorways on MainPath rooms.</description></item>
    /// <item><description>Optionally connect touching doorways as loops, then close every remaining doorway.</description></item>
    /// </list>
    /// Rooms are placed as <see cref="PlacementUnit"/>s: the room plus the connectors leading into it, added and removed together.
    /// Each phase is a step iterator that yields after every placement attempt, so <see cref="GenerateDungeonAsync"/> can spread
    /// the work over several frames while the synchronous path runs the same steps to completion.
    /// </summary>
    public partial class DungeonGenerator
    {
        /// <summary>
        /// How many times a whole unit (connectors + room) is retried with fresh random choices before it counts as failed.
        /// </summary>
        private const int UnitAttemptLimit = 5;

        /// <summary>
        /// The Start module of the current path-first layout.
        /// </summary>
        private DungeonModule startModule;

        /// <summary>
        /// A room plus the connectors that lead into it. Placed and removed as one piece, so a branch never ends in a
        /// connector and backtracking never leaves hallway stubs behind.
        /// </summary>
        private sealed class PlacementUnit
        {
            public readonly DungeonModule Room;
            public readonly int ModuleCount;

            public PlacementUnit(DungeonModule room, int moduleCount)
            {
                Room = room;
                ModuleCount = moduleCount;
            }
        }

        /// <summary>
        /// Carries a value out of a step iterator, which can't return one directly.
        /// </summary>
        private sealed class StepResult<TValue>
        {
            public TValue Value;
        }

        /// <summary>
        /// Checks that the theme has every role a path-first layout depends on and sets the backtracking budget.
        /// </summary>
        private void InitializePathFirst()
        {
            startModule = null;

            var requiredRoles = new[] { ModuleRole.Start, ModuleRole.MainPath, ModuleRole.Boss, ModuleRole.End };
            if (requiredRoles.Any(requiredRole => selectedTheme.CategoriesWithRole(requiredRole).Count <= 0))
            {
                generationError = GenerationError.MissingRoleCategory;
                return;
            }

            // Main path rooms, Boss and End, each allowed several backtracks
            backtrackLimit = (selectedTheme.MainPathLength.Maximum + 2) * PlacementLimit;
        }

        /// <summary>
        /// Builds the full path-first layout, one placement attempt per step. Sets <see cref="generationError"/> if any phase fails.
        /// </summary>
        private IEnumerator PathFirstSteps()
        {
            if (!PlaceStartModule())
            {
                generationError = GenerationError.MissingRoleCategory;
                yield break;
            }

            var mainRooms = new List<DungeonModule>();
            var mainPathSteps = MainPathSteps(mainRooms);
            while (mainPathSteps.MoveNext()) yield return null;

            if (generationError != GenerationError.None) yield break;

            var branchSteps = BranchSteps(mainRooms);
            while (branchSteps.MoveNext()) yield return null;

            if (generationError != GenerationError.None) yield break;

            if (enableLoops)
            {
                ConnectLoops();
            }
        }

        /// <summary>
        /// Places a Start module at the generator's origin.
        /// </summary>
        /// <returns><c>true</c> if a Start module was placed.</returns>
        private bool PlaceStartModule()
        {
            var startCategories = AvailableCategories(selectedTheme.CategoriesWithRole(ModuleRole.Start));

            while (0 < startCategories.Count)
            {
                var startCategory = WeightedPick(startCategories, moduleCategory => moduleCategory.SpawnRate);
                var startAsset = SampleAvailableAsset(startCategory);

                if (startAsset == null)
                {
                    startCategories.Remove(startCategory);
                    continue;
                }

                var moduleInstance = InstantiateModule(startAsset.ModulePrefab);
                startModule = moduleInstance.GetComponent<DungeonModule>();

                RecordModule(new HistoryEntry(startAsset, startCategory, startModule, null, null, false));
                RefreshConnections(startModule);

                startModule.IsMainPath = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Grows the main path from the Start module: N MainPath rooms, then Boss, then End. When a unit can't be placed, the
        /// previous main-path unit is removed and placed again (backtracking pops main-path units only; if nothing fits off Start, Start itself is placed again).
        /// </summary>
        /// <param name="mainRooms">Filled with the MainPath rooms that were placed, in order.</param>
        private IEnumerator MainPathSteps(List<DungeonModule> mainRooms)
        {
            var mainPathLength = NextInRange(selectedTheme.MainPathLength.Minimum, selectedTheme.MainPathLength.Maximum + 1);

            var plannedRoles = Enumerable.Repeat(ModuleRole.MainPath, mainPathLength).ToList();
            plannedRoles.Add(ModuleRole.Boss);
            plannedRoles.Add(ModuleRole.End);

            var placedUnits = new Stack<PlacementUnit>();

            while (generationError == GenerationError.None && placedUnits.Count < plannedRoles.Count)
            {
                var plannedRole = plannedRoles[placedUnits.Count];
                var tipModule = 0 < placedUnits.Count ? placedUnits.Peek().Room : startModule;

                // Every main-path room except End needs a doorway left over for the next one; Boss needs one for End
                var unitResult = new StepResult<PlacementUnit>();
                var unitSteps = PlaceUnitSteps(tipModule, plannedRole, true, plannedRole != ModuleRole.End, null, unitResult);
                while (unitSteps.MoveNext()) yield return null;

                var placedUnit = unitResult.Value;
                if (placedUnit != null)
                {
                    placedUnits.Push(placedUnit);
                    continue;
                }

                if (backtrackLimit <= backtrackAttempts)
                {
                    generationError = GenerationError.MainPathFailed;
                    break;
                }

                backtrackAttempts++;

                if (0 < placedUnits.Count)
                {
                    RemoveUnit(placedUnits.Pop());
                    continue;
                }

                // Nothing fits off the Start module (e.g. every retry picked a hallway that turns back into it): place it again
                PopModule();
                if (!PlaceStartModule())
                {
                    generationError = GenerationError.MissingRoleCategory;
                    break;
                }
            }

            mainRooms.AddRange(placedUnits
                .Reverse()
                .Select(placedUnit => placedUnit.Room)
                .Where(placedRoom => placedRoom.Role == ModuleRole.MainPath));
        }

        /// <summary>
        /// Grows short branches of Branch rooms off open doorways on MainPath rooms. Required Branch categories are placed first
        /// and may add branches beyond the theme's branch count. A branch that can't grow further simply stops; it never needs
        /// backtracking because each unit is placed or removed as a whole.
        /// </summary>
        /// <param name="mainRooms">The MainPath rooms branches may grow from.</param>
        private IEnumerator BranchSteps(List<DungeonModule> mainRooms)
        {
            var branchCategories = selectedTheme.CategoriesWithRole(ModuleRole.Branch);
            if (branchCategories.Count <= 0) yield break;

            // Required Branch categories, repeated by their minimum spawn count
            var requiredCategories = new List<ModuleCategory>();
            foreach (var branchCategory in branchCategories.Where(branchCategory => branchCategory.SpawnRequired))
            {
                var requiredCount = branchCategory.SpawnLimits ? Math.Max(1, branchCategory.SpawnMinimum) : 1;
                requiredCategories.AddRange(Enumerable.Repeat(branchCategory, requiredCount));
            }

            var branchTarget = NextInRange(selectedTheme.BranchCount.Minimum, selectedTheme.BranchCount.Maximum + 1);
            var branchesGrown = 0;
            var branchAttempts = 0;
            var branchAttemptLimit = (branchTarget + requiredCategories.Count + 1) * UnitAttemptLimit;

            while (generationError == GenerationError.None && (branchesGrown < branchTarget || 0 < requiredCategories.Count))
            {
                if (branchAttemptLimit <= branchAttempts++) break;

                var rootRooms = mainRooms.Where(mainRoom => 0 < mainRoom.ConnectableEntrances.Count).ToList();
                if (rootRooms.Count <= 0) break;

                var tipModule = rootRooms[randomSource.Next(0, rootRooms.Count)];
                var branchDepth = Math.Max(1, NextInRange(selectedTheme.BranchDepth.Minimum, selectedTheme.BranchDepth.Maximum + 1));
                var roomsPlaced = 0;

                for (var depthIndex = 0; depthIndex < branchDepth; depthIndex++)
                {
                    var requiredCategory = 0 < requiredCategories.Count ? requiredCategories[0] : null;
                    var needsOpenDoorway = depthIndex < branchDepth - 1;

                    var unitResult = new StepResult<PlacementUnit>();
                    var unitSteps = PlaceUnitSteps(tipModule, ModuleRole.Branch, false, needsOpenDoorway, requiredCategory, unitResult);
                    while (unitSteps.MoveNext()) yield return null;

                    var placedUnit = unitResult.Value;
                    if (placedUnit == null) break;

                    // Any placed room satisfies one outstanding requirement for its category
                    var satisfiedIndex = requiredCategories.FindIndex(requiredEntry => requiredEntry.Equals(placedUnit.Room.Category));
                    if (0 <= satisfiedIndex)
                    {
                        requiredCategories.RemoveAt(satisfiedIndex);
                    }

                    tipModule = placedUnit.Room;
                    roomsPlaced++;
                }

                if (0 < roomsPlaced)
                {
                    branchesGrown++;
                }
            }

            if (0 < requiredCategories.Count)
            {
                generationError = GenerationError.RequiredBranchFailed;
            }
        }

        /// <summary>
        /// Connects doorways on placed modules that already line up with another module's open doorway, where the connection
        /// rules allow it. Runs after the structure is complete, so loops never need to be undone.
        /// </summary>
        private void ConnectLoops()
        {
            foreach (var placedModule in placedModules.ToList())
            {
                ResolveSecondaryConnections(placedModule, placedModule.Category);
                RefreshConnections(placedModule);
            }
        }

        /// <summary>
        /// Attaches a room with the given role to <paramref name="tipModule"/>, with connectors in between as the theme and
        /// connection rules require. The whole unit is retried up to <see cref="UnitAttemptLimit"/> times.
        /// </summary>
        /// <param name="tipModule">The module the unit attaches to.</param>
        /// <param name="targetRole">The role of the room to place.</param>
        /// <param name="isMainPath">Whether the unit is part of the main path.</param>
        /// <param name="needsOpenDoorway">Whether the room must keep a free doorway for whatever attaches to it next.</param>
        /// <param name="requiredCategory">If set, the room must come from this category.</param>
        /// <param name="unitResult">Receives the placed unit, or <c>null</c> if it could not be placed.</param>
        private IEnumerator PlaceUnitSteps(DungeonModule tipModule, ModuleRole targetRole, bool isMainPath, bool needsOpenDoorway, ModuleCategory requiredCategory, StepResult<PlacementUnit> unitResult)
        {
            var targetCategories = requiredCategory != null
                ? new List<ModuleCategory> { requiredCategory }
                : selectedTheme.CategoriesWithRole(targetRole);

            var connectorCategories = selectedTheme.CategoriesWithRole(ModuleRole.Connector);

            for (var unitAttempt = 0; unitAttempt < UnitAttemptLimit; unitAttempt++)
            {
                if (tipModule.ConnectableEntrances.Count <= 0) yield break;

                var availableTargets = AvailableCategories(targetCategories);
                if (availableTargets.Count <= 0) yield break;

                var connectorCount = ChooseConnectorCount(tipModule.Category, targetRole, availableTargets, connectorCategories);
                var currentModule = tipModule;
                var placedCount = 0;
                var connectorsPlaced = true;

                for (var connectorIndex = 0; connectorIndex < connectorCount; connectorIndex++)
                {
                    var isLastConnector = connectorIndex == connectorCount - 1;
                    var candidateConnectors = AvailableCategories(connectorCategories)
                        .Where(connectorCategory => CanConnect(currentModule.Category, connectorCategory))
                        .Where(connectorCategory => !isLastConnector || availableTargets.Any(targetCategory => CanConnect(connectorCategory, targetCategory)))
                        .ToList();

                    var connectorResult = new StepResult<DungeonModule>();
                    var connectorSteps = PlacePieceSteps(currentModule, candidateConnectors, true, isMainPath, connectorResult);
                    while (connectorSteps.MoveNext()) yield return null;

                    var placedConnector = connectorResult.Value;
                    if (placedConnector == null)
                    {
                        connectorsPlaced = false;
                        break;
                    }

                    currentModule = placedConnector;
                    placedCount++;
                }

                if (connectorsPlaced)
                {
                    var connectedModule = currentModule;
                    var candidateRooms = availableTargets
                        .Where(targetCategory => CanConnect(connectedModule.Category, targetCategory))
                        .ToList();

                    var roomResult = new StepResult<DungeonModule>();
                    var roomSteps = PlacePieceSteps(currentModule, candidateRooms, needsOpenDoorway, isMainPath, roomResult);
                    while (roomSteps.MoveNext()) yield return null;

                    if (roomResult.Value != null)
                    {
                        unitResult.Value = new PlacementUnit(roomResult.Value, placedCount + 1);
                        yield break;
                    }
                }

                // Remove this attempt's connectors so the unit is all-or-nothing
                for (var removeIndex = 0; removeIndex < placedCount; removeIndex++)
                {
                    PopModule();
                }
            }
        }

        /// <summary>
        /// Chooses how many connectors go between <paramref name="tipCategory"/> and the next room.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item><description>No Connector categories: always 0.</description></item>
        /// <item><description>End attaches straight to the Boss room's doorway when the rules allow it.</description></item>
        /// <item><description>Otherwise a random count within the theme's range, raised to 1 if the rules deny a direct connection.</description></item>
        /// </list>
        /// </remarks>
        private int ChooseConnectorCount(ModuleCategory tipCategory, ModuleRole targetRole, List<ModuleCategory> targetCategories, List<ModuleCategory> connectorCategories)
        {
            if (connectorCategories.Count <= 0) return 0;

            var directAllowed = targetCategories.Any(targetCategory => CanConnect(tipCategory, targetCategory));
            if (targetRole == ModuleRole.End && directAllowed) return 0;

            var connectorRange = selectedTheme.ConnectorsBetweenRooms;
            var connectorCount = NextInRange(connectorRange.Minimum, connectorRange.Maximum + 1);

            if (connectorCount == 0 && !directAllowed)
            {
                connectorCount = Math.Min(1, connectorRange.Maximum);
            }

            return connectorCount;
        }

        /// <summary>
        /// Attaches one module from the candidate categories to an open doorway on <paramref name="tipModule"/>, retrying with
        /// different modules and doorways up to <see cref="PlacementLimit"/> times.
        /// </summary>
        /// <param name="tipModule">The module to attach to.</param>
        /// <param name="candidateCategories">The categories the new module may come from, already filtered by the connection rules.</param>
        /// <param name="needsOpenDoorway">Whether the new module must keep at least one free doorway after connecting.</param>
        /// <param name="isMainPath">Whether the new module is part of the main path.</param>
        /// <param name="pieceResult">Receives the placed module, or <c>null</c> if nothing fit.</param>
        private IEnumerator PlacePieceSteps(DungeonModule tipModule, List<ModuleCategory> candidateCategories, bool needsOpenDoorway, bool isMainPath, StepResult<DungeonModule> pieceResult)
        {
            var remainingCategories = new List<ModuleCategory>(candidateCategories);

            for (var placementAttempt = 0; placementAttempt < PlacementLimit; placementAttempt++)
            {
                if (remainingCategories.Count <= 0 || tipModule.ConnectableEntrances.Count <= 0) yield break;

                var moduleCategory = WeightedPick(remainingCategories, candidateCategory => candidateCategory.SpawnRate);
                var moduleAsset = SampleAvailableAsset(moduleCategory);

                if (moduleAsset == null)
                {
                    remainingCategories.Remove(moduleCategory);
                    continue;
                }

                var moduleInstance = InstantiateModule(moduleAsset.ModulePrefab);
                var instantiatedModule = moduleInstance.GetComponent<DungeonModule>();

                var instantiatedEntrance = instantiatedModule.SelectConnectableEntrance(randomSource);
                var connectedEntrance = tipModule.SelectConnectableEntrance(randomSource);

                // A module that must lead somewhere needs a doorway besides the one it connects through
                var leavesNoDoorway = needsOpenDoorway && instantiatedModule.ConnectableEntrances.Count <= 1;

                if (instantiatedEntrance == null || leavesNoDoorway)
                {
                    DestroyModule(instantiatedModule);
                    yield return null;
                    continue;
                }

                AlignModules(moduleInstance.transform, instantiatedEntrance.EntrancePoint, connectedEntrance.EntrancePoint);

                if (ContainsIntersections(instantiatedModule))
                {
                    DestroyModule(instantiatedModule);
                    yield return null;
                    continue;
                }

                instantiatedModule.RemoveConnectableEntrance(instantiatedEntrance);
                tipModule.RemoveConnectableEntrance(connectedEntrance);

                RefreshConnections(instantiatedModule);
                RefreshConnections(tipModule);

                RecordModule(new HistoryEntry(moduleAsset, moduleCategory, instantiatedModule, connectedEntrance, null, false));
                instantiatedModule.IsMainPath = isMainPath;

                pieceResult.Value = instantiatedModule;
                yield break;
            }
        }

        /// <summary>
        /// Removes every module in a unit, most recent first.
        /// </summary>
        private void RemoveUnit(PlacementUnit placementUnit)
        {
            for (var removeIndex = 0; removeIndex < placementUnit.ModuleCount; removeIndex++)
            {
                PopModule();
            }
        }

        /// <summary>
        /// Filters categories down to those that haven't reached their spawn maximum and still have a usable module.
        /// </summary>
        private List<ModuleCategory> AvailableCategories(IEnumerable<ModuleCategory> moduleCategories)
        {
            return moduleCategories
                .Where(moduleCategory => !moduleCategory.SpawnLimits || categorizedModules[moduleCategory] < moduleCategory.SpawnMaximum)
                .Where(moduleCategory => AvailableAssets(moduleCategory).Any())
                .ToList();
        }

        /// <summary>
        /// Returns the category's modules that can still be placed (spawn-once modules that are already used are excluded).
        /// </summary>
        private IEnumerable<ModuleAsset> AvailableAssets(ModuleCategory moduleCategory)
        {
            if (!categoryAssets.TryGetValue(moduleCategory, out var moduleAssets) || moduleAssets == null)
            {
                return Enumerable.Empty<ModuleAsset>();
            }

            return moduleAssets.Where(moduleAsset => moduleAsset.ModulePrefab != null && !(moduleAsset.SpawnOnce && uniqueModules.Contains(moduleAsset)));
        }

        /// <summary>
        /// Picks one of the category's usable modules, weighted by spawn rate.
        /// </summary>
        /// <returns>The chosen module, or <c>null</c> if none are usable.</returns>
        private ModuleAsset SampleAvailableAsset(ModuleCategory moduleCategory)
        {
            var availableAssets = AvailableAssets(moduleCategory).ToList();
            return 0 < availableAssets.Count ? WeightedPick(availableAssets, moduleAsset => moduleAsset.SpawnRate) : null;
        }

        /// <summary>
        /// Picks an item with probability proportional to its weight, or uniformly if every weight is zero.
        /// Linear rather than an alias table because candidate lists change with every doorway's connection rules.
        /// </summary>
        private TItem WeightedPick<TItem>(IReadOnlyList<TItem> candidateItems, Func<TItem, float> itemWeight)
        {
            var totalWeight = candidateItems.Sum(candidateItem => Math.Max(0f, itemWeight(candidateItem)));
            if (totalWeight <= 0f)
            {
                return candidateItems[randomSource.Next(0, candidateItems.Count)];
            }

            var targetWeight = randomSource.NextDouble() * totalWeight;
            foreach (var candidateItem in candidateItems)
            {
                targetWeight -= Math.Max(0f, itemWeight(candidateItem));
                if (targetWeight < 0) return candidateItem;
            }

            return candidateItems[candidateItems.Count - 1];
        }
    }
}
