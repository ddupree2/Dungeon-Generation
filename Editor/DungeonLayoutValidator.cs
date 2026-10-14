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

            return violations;
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
