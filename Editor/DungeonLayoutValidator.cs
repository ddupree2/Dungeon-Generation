using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// Checks a generated layout against the generator's rules and produces fingerprints for determinism checks.
    /// Used by <see cref="DungeonBatchTesterWindow"/>.
    /// </summary>
    internal static class DungeonLayoutValidator
    {
        /// <summary>
        /// Amount each bounds collider is shrunk by before testing for overlaps, so modules meeting at a doorway don't count.
        /// </summary>
        private const float OverlapTolerance = 0.05f;

        /// <summary>
        /// Returns the active modules generated under the <see cref="DungeonGenerator"/>, in hierarchy order.
        /// </summary>
        public static List<DungeonModule> PlacedModules(DungeonGenerator dungeonGenerator)
        {
            var placedModules = new List<DungeonModule>();

            foreach (Transform childTransform in dungeonGenerator.transform)
            {
                if (!childTransform.gameObject.activeSelf) continue;

                var dungeonModule = childTransform.GetComponent<DungeonModule>();
                if (dungeonModule != null)
                {
                    placedModules.Add(dungeonModule);
                }
            }

            return placedModules;
        }

        /// <summary>
        /// Checks the current layout of a successful generation pass and returns a description of every rule it breaks.
        /// </summary>
        /// <param name="dungeonGenerator">The generator whose layout to check.</param>
        /// <returns>The rule violations found, or an empty list if the layout is valid.</returns>
        public static List<string> Validate(DungeonGenerator dungeonGenerator)
        {
            var violations = new List<string>();
            var placedModules = PlacedModules(dungeonGenerator);

            if (placedModules.Count != dungeonGenerator.LastResult.ModuleCount)
            {
                violations.Add($"Scene has {placedModules.Count} modules but the generator reported {dungeonGenerator.LastResult.ModuleCount}");
            }

            ValidateOverlaps(placedModules, violations);
            ValidateBoundsLayers(dungeonGenerator, placedModules, violations);

            var selectedTheme = dungeonGenerator.SelectedTheme;
            if (selectedTheme == null) return violations;

            ValidateConnectionRules(dungeonGenerator, selectedTheme, violations);
            ValidateDepths(dungeonGenerator, violations);

            if (selectedTheme.LayoutMode == LayoutMode.PathFirst)
            {
                ValidatePathStructure(dungeonGenerator, selectedTheme, violations);
            }

            return violations;
        }

        /// <summary>
        /// Reports bounds colliders that are not on one of the generator's placement layers; the overlap check can't see them.
        /// </summary>
        private static void ValidateBoundsLayers(DungeonGenerator dungeonGenerator, List<DungeonModule> placedModules, List<string> violations)
        {
            var placementLayers = dungeonGenerator.PlacementLayers.value;

            foreach (var placedModule in placedModules)
            {
                var hiddenBounds = placedModule.ModuleBounds.Any(boundsCollider => boundsCollider != null && (placementLayers & (1 << boundsCollider.gameObject.layer)) == 0);
                if (hiddenBounds)
                {
                    violations.Add($"{Describe(placedModule)} has bounds colliders outside the generator's placement layers");
                }
            }
        }

        /// <summary>
        /// Reports any doorway connection, including loops, that the theme's connection rules don't allow.
        /// </summary>
        private static void ValidateConnectionRules(DungeonGenerator dungeonGenerator, DungeonTheme selectedTheme, List<string> violations)
        {
            foreach (var placedModule in dungeonGenerator.PlacedModules)
            {
                var parentModule = placedModule.ParentModule;
                if (parentModule == null) continue;

                if (!selectedTheme.CanConnect(parentModule.Category, placedModule.Category))
                {
                    violations.Add($"Rules deny {Describe(parentModule)} – {Describe(placedModule)}");
                }
            }

            foreach (var loopConnection in dungeonGenerator.LoopConnections)
            {
                if (!selectedTheme.CanConnect(loopConnection.FirstModule.Category, loopConnection.SecondModule.Category))
                {
                    violations.Add($"Rules deny loop {Describe(loopConnection.FirstModule)} – {Describe(loopConnection.SecondModule)}");
                }
            }
        }

        /// <summary>
        /// Reports modules whose depth isn't their parent's depth plus one room (connectors add nothing).
        /// </summary>
        private static void ValidateDepths(DungeonGenerator dungeonGenerator, List<string> violations)
        {
            foreach (var placedModule in dungeonGenerator.PlacedModules)
            {
                var parentModule = placedModule.ParentModule;
                var expectedDepth = parentModule == null ? 0 : parentModule.Depth + (placedModule.Role == ModuleRole.Connector ? 0 : 1);

                if (placedModule.Depth != expectedDepth)
                {
                    violations.Add($"{Describe(placedModule)} has depth {placedModule.Depth}, expected {expectedDepth}");
                }
            }
        }

        /// <summary>
        /// Checks the Start → main path → Boss → End structure, branch placement and connector sequences of a path-first layout.
        /// </summary>
        private static void ValidatePathStructure(DungeonGenerator dungeonGenerator, DungeonTheme selectedTheme, List<string> violations)
        {
            var placedModules = dungeonGenerator.PlacedModules;
            if (placedModules.Count <= 0) return;

            // Start first
            var startCount = placedModules.Count(placedModule => placedModule.Role == ModuleRole.Start);
            if (startCount != 1) violations.Add($"Expected 1 Start module, found {startCount}");
            if (placedModules[0].Role != ModuleRole.Start) violations.Add($"First module is {Describe(placedModules[0])}, not Start");

            foreach (var placedModule in placedModules.Skip(1).Where(placedModule => placedModule.ParentModule == null))
            {
                violations.Add($"{Describe(placedModule)} is not attached to anything");
            }

            // Boss directly before End, End last on the main path
            var bossCount = placedModules.Count(placedModule => placedModule.Role == ModuleRole.Boss);
            var endModules = placedModules.Where(placedModule => placedModule.Role == ModuleRole.End).ToList();
            if (bossCount != 1) violations.Add($"Expected 1 Boss module, found {bossCount}");
            if (endModules.Count != 1) violations.Add($"Expected 1 End module, found {endModules.Count}");

            if (endModules.Count == 1)
            {
                var endModule = endModules[0];
                if (RoomParent(endModule)?.Role != ModuleRole.Boss) violations.Add("End is not attached to the Boss room");
                if (placedModules.Any(placedModule => placedModule.ParentModule == endModule)) violations.Add("Modules are attached after End");

                ValidateMainPath(endModule, selectedTheme, violations);
            }

            var hasConnectors = 0 < selectedTheme.CategoriesWithRole(ModuleRole.Connector).Count;

            foreach (var placedModule in placedModules)
            {
                switch (placedModule.Role)
                {
                    case ModuleRole.MainPath when !placedModule.IsMainPath:
                        violations.Add($"{Describe(placedModule)} is a MainPath room outside the main path");
                        break;

                    case ModuleRole.Branch:
                        ValidateBranchRoom(placedModule, violations);
                        break;

                    case ModuleRole.Connector:
                        // Branches must end in a room: no dead-end hallway stubs
                        if (!placedModules.Any(childModule => childModule.ParentModule == placedModule))
                        {
                            violations.Add($"{Describe(placedModule)} is a connector that leads nowhere");
                        }
                        break;
                }

                if (hasConnectors && placedModule.Role != ModuleRole.Connector && placedModule.ParentModule != null)
                {
                    ValidateConnectorRun(placedModule, selectedTheme, violations);
                }
            }
        }

        /// <summary>
        /// Walks from End back to Start through rooms only and checks the order Start, MainPath..., Boss, End.
        /// </summary>
        private static void ValidateMainPath(DungeonModule endModule, DungeonTheme selectedTheme, List<string> violations)
        {
            var mainPathRooms = new List<DungeonModule>();
            for (var currentRoom = endModule; currentRoom != null; currentRoom = RoomParent(currentRoom))
            {
                mainPathRooms.Insert(0, currentRoom);
            }

            var expectedShape = 3 <= mainPathRooms.Count &&
                                mainPathRooms[0].Role == ModuleRole.Start &&
                                mainPathRooms[mainPathRooms.Count - 2].Role == ModuleRole.Boss &&
                                mainPathRooms.Skip(1).Take(mainPathRooms.Count - 3).All(mainRoom => mainRoom.Role == ModuleRole.MainPath);

            if (!expectedShape)
            {
                violations.Add($"Main path is {string.Join(" → ", mainPathRooms.Select(mainRoom => mainRoom.Role))}, expected Start → MainPath... → Boss → End");
            }

            var mainPathLength = mainPathRooms.Count(mainRoom => mainRoom.Role == ModuleRole.MainPath);
            if (!selectedTheme.MainPathLength.Contains(mainPathLength))
            {
                violations.Add($"Main path has {mainPathLength} MainPath rooms, outside {selectedTheme.MainPathLength}");
            }

            foreach (var mainRoom in mainPathRooms.Where(mainRoom => !mainRoom.IsMainPath))
            {
                violations.Add($"{Describe(mainRoom)} is on the Start → End path but not flagged as main path");
            }
        }

        /// <summary>
        /// Checks that a Branch room hangs off other Branch rooms that lead back to a MainPath room.
        /// </summary>
        private static void ValidateBranchRoom(DungeonModule branchRoom, List<string> violations)
        {
            if (branchRoom.IsMainPath)
            {
                violations.Add($"{Describe(branchRoom)} is a Branch room flagged as main path");
            }

            var rootRoom = RoomParent(branchRoom);
            while (rootRoom != null && rootRoom.Role == ModuleRole.Branch)
            {
                rootRoom = RoomParent(rootRoom);
            }

            if (rootRoom == null || rootRoom.Role != ModuleRole.MainPath)
            {
                violations.Add($"{Describe(branchRoom)} branches off {(rootRoom == null ? "nothing" : Describe(rootRoom))}, not a MainPath room");
            }
        }

        /// <summary>
        /// Checks the number of connectors in a row leading into a room. End may attach straight to Boss.
        /// </summary>
        private static void ValidateConnectorRun(DungeonModule roomModule, DungeonTheme selectedTheme, List<string> violations)
        {
            var connectorRun = 0;
            for (var currentModule = roomModule.ParentModule; currentModule != null && currentModule.Role == ModuleRole.Connector; currentModule = currentModule.ParentModule)
            {
                connectorRun++;
            }

            var connectorRange = selectedTheme.ConnectorsBetweenRooms;
            var directToEnd = connectorRun == 0 && roomModule.Role == ModuleRole.End;

            if (!directToEnd && !connectorRange.Contains(connectorRun))
            {
                violations.Add($"{connectorRun} connectors lead into {Describe(roomModule)}, outside {connectorRange}");
            }
        }

        /// <summary>
        /// Returns the nearest room this module was attached through, skipping connectors.
        /// </summary>
        private static DungeonModule RoomParent(DungeonModule dungeonModule)
        {
            var parentModule = dungeonModule.ParentModule;
            while (parentModule != null && parentModule.Role == ModuleRole.Connector)
            {
                parentModule = parentModule.ParentModule;
            }

            return parentModule;
        }

        private static string Describe(DungeonModule dungeonModule)
        {
            var categoryTitle = dungeonModule.Category?.CategoryTitle ?? "?";
            return $"'{dungeonModule.name}' ({categoryTitle}/{dungeonModule.Role})";
        }

        /// <summary>
        /// Reports any pair of modules whose bounds colliders overlap by more than <see cref="OverlapTolerance"/>.
        /// </summary>
        private static void ValidateOverlaps(List<DungeonModule> placedModules, List<string> violations)
        {
            var moduleBounds = placedModules
                .Select(placedModule => placedModule.ModuleBounds.Where(boundsCollider => boundsCollider != null).Select(ShrunkBounds).ToList())
                .ToList();

            for (var firstIndex = 0; firstIndex < placedModules.Count; firstIndex++)
            {
                for (var secondIndex = firstIndex + 1; secondIndex < placedModules.Count; secondIndex++)
                {
                    var overlaps = moduleBounds[firstIndex].Any(firstBounds => moduleBounds[secondIndex].Any(firstBounds.Intersects));
                    if (overlaps)
                    {
                        violations.Add($"'{placedModules[firstIndex].name}' overlaps '{placedModules[secondIndex].name}'");
                    }
                }
            }
        }

        private static Bounds ShrunkBounds(Collider boundsCollider)
        {
            var colliderBounds = boundsCollider.bounds;
            colliderBounds.Expand(-OverlapTolerance * 2f);
            return colliderBounds;
        }

        /// <summary>
        /// Builds a string that identifies the current layout: each module's prefab name, position and rotation.
        /// Two passes with the same seed must produce the same fingerprint.
        /// </summary>
        /// <param name="dungeonGenerator">The generator whose layout to fingerprint.</param>
        /// <returns>A text fingerprint of the layout.</returns>
        public static string Fingerprint(DungeonGenerator dungeonGenerator)
        {
            var fingerprintBuilder = new StringBuilder();

            foreach (var placedModule in PlacedModules(dungeonGenerator))
            {
                var moduleTransform = placedModule.transform;
                var modulePosition = moduleTransform.position;

                fingerprintBuilder
                    .Append(placedModule.name)
                    .Append('@')
                    .Append(modulePosition.x.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                    .Append(modulePosition.y.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                    .Append(modulePosition.z.ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                    .Append(Mathf.RoundToInt(moduleTransform.eulerAngles.y)).Append(';');
            }

            return fingerprintBuilder.ToString();
        }
    }
}
