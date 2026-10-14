using StarStoneStudio.DungeonGeneration.Attributes;
using StarStoneStudio.DungeonGeneration.Utils;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Defines the dataset used for procedural dungeon generation.
    /// This includes the theme's name, available module categories, and the individual models that can be spawned.
    /// </summary>
    [CreateAssetMenu(fileName = "Dungeon_Theme", menuName = "Dungeon Theme")]
    public class DungeonTheme : ScriptableObject
    {
        [Required]
        [SerializeField]
        [Tooltip("The display name of the dungeon theme")]
        private string themeTitle;

        [SerializeField]
        [Tooltip("Weighted attaches modules to any open doorway until the module count is reached. Path First builds Start → main path → Boss → End and grows branches off the main path.")]
        private LayoutMode layoutMode;

        [Required]
        [Min(0)]
        [SerializeField]
        [Tooltip("The maximum number of modules that can exist within this theme (weighted layouts only)")]
        private int maximumModules;

        [Required]
        [Min(0)]
        [SerializeField]
        [Tooltip("The minimum number of modules that can exist within this theme (weighted layouts only)")]
        private int minimumModules;

        [SerializeField]
        [Tooltip("How many MainPath rooms sit between Start and Boss. Connectors are not counted.")]
        private IntRange mainPathLength = new(3, 5);

        [SerializeField]
        [Tooltip("How many optional branches grow off main-path rooms. Required Branch categories may add extra branches.")]
        private IntRange branchCount = new(1, 3);

        [SerializeField]
        [Tooltip("How many Branch rooms each branch contains. Connectors are not counted.")]
        private IntRange branchDepth = new(1, 2);

        [SerializeField]
        [Tooltip("How many Connector modules appear in a row between two rooms. The End room attaches straight to the Boss room when the connection rules allow it.")]
        private IntRange connectorsBetweenRooms = new(1, 2);

        [SerializeField]
        [Tooltip("Optional rules controlling which categories may connect at a doorway. Leave empty to allow every connection.")]
        private ConnectionRules connectionRules;

        [SerializeField]
        [Tooltip("Defines which module assets belong to which module category")]
        private ModuleData[] moduleData;

        /// <summary>
        /// The display name of the dungeon theme.
        /// </summary>
        public string ThemeTitle => themeTitle;

        /// <summary>
        /// How this theme assembles its modules.
        /// </summary>
        public LayoutMode LayoutMode => layoutMode;

        /// <summary>
        /// The maximum number of modules that can exist within this theme.
        /// </summary>
        public int MaximumModules => maximumModules;

        /// <summary>
        /// The minimum number of modules that can exist within this theme.
        /// </summary>
        public int MinimumModules => minimumModules;

        /// <summary>
        /// How many MainPath rooms sit between Start and Boss in a path-first layout.
        /// </summary>
        public IntRange MainPathLength => mainPathLength;

        /// <summary>
        /// How many optional branches grow off main-path rooms in a path-first layout.
        /// </summary>
        public IntRange BranchCount => branchCount;

        /// <summary>
        /// How many Branch rooms each branch contains in a path-first layout.
        /// </summary>
        public IntRange BranchDepth => branchDepth;

        /// <summary>
        /// How many Connector modules appear in a row between two rooms in a path-first layout.
        /// </summary>
        public IntRange ConnectorsBetweenRooms => connectorsBetweenRooms;

        /// <summary>
        /// The rules controlling which categories may connect, or <c>null</c> to allow every connection.
        /// </summary>
        public ConnectionRules ConnectionRules => connectionRules;

        public ModuleData[] ModuleData => moduleData;

        /// <summary>
        /// Whether modules from the two categories may connect at a doorway under this theme's <see cref="ConnectionRules"/>.
        /// </summary>
        public bool CanConnect(ModuleCategory moduleCategory, ModuleCategory otherCategory)
        {
            if (connectionRules == null) return true;
            if (moduleCategory == null || otherCategory == null) return true;

            return connectionRules.CanConnect(moduleCategory.CategoryTitle, otherCategory.CategoryTitle);
        }

        /// <summary>
        /// Returns every category in this theme with the given role.
        /// </summary>
        public List<ModuleCategory> CategoriesWithRole(ModuleRole moduleRole)
        {
            return moduleData
                .Select(moduleElement => moduleElement.ModuleCategory)
                .Where(moduleCategory => moduleCategory != null && moduleCategory.Role == moduleRole)
                .ToList();
        }

        #if UNITY_EDITOR

        /// <summary>
        /// Validates this theme's configuration and logs errors to help catch setup issues early.
        /// </summary>
        public void OnValidate()
        {
            if (moduleData == null) return;

            var isWeighted = layoutMode == LayoutMode.Weighted;
            var isPathFirst = layoutMode == LayoutMode.PathFirst;

            var missingRoles = string.Empty;
            var rolesMissing = isPathFirst && MissingPathRoles(out missingRoles);

            ObjectValidator.AssertConditions(
                this,
                (moduleData.Length <= 0, $"<b>{nameof(moduleData)}</b> must contain at least one element."),
                (ContainsImpossibleRequirements(), $"All <b>{nameof(moduleData)}</b> entries must have sufficient unique modules to satisfy their spawn requirements."),
                (ContainsUnassignedCategories(), $"All <b>{nameof(moduleData)}</b> entries must have a valid list of <b>{nameof(ModuleAsset)}</b>."),
                (ContainsDuplicateTitles(), $"Every module category needs a unique title; connection rules refer to categories by title."),
                (isWeighted && maximumModules < minimumModules, $"<b>{nameof(maximumModules)}</b> must be greater than <b>{nameof(minimumModules)}</b>"),
                (isWeighted && ContainsInvalidLimits(), $"One or more <b>{nameof(moduleData)}</b> entries have spawn limits that exceed this theme's bounds or have a minimum above their maximum."),
                (isWeighted && ContainsInvalidWeights(), $"All <b>{nameof(moduleData)}</b> entries' cumulative spawn rate should roughly equal 1."),
                (isWeighted && ValidateRequiredModules(), $"The minimum number of required modules must not exceed <b>{maximumModules}</b>."),
                (rolesMissing,$"Path-first themes need at least one category for each of these roles: <b>{missingRoles}</b>."),
                (isPathFirst && ContainsInvalidRanges(), $"Every path setting needs a minimum that does not exceed its maximum, and branch depth must be at least 1.")
            );

            if (isPathFirst)
            {
                WarnAboutConnectionRules();
            }
        }

        /// <summary>
        /// Checks whether any spawn-limited module category asks for more modules than this theme allows, or has its minimum above its maximum.
        /// </summary>
        /// <returns>
        /// <c>true</c> if one or more module categories have invalid spawn limits; otherwise, <c>false</c>.
        /// </returns>
        private bool ContainsInvalidLimits()
        {
            return moduleData
                .Select(moduleElement => moduleElement.ModuleCategory)
                .Any(moduleCategory => moduleCategory != null &&
                                       moduleCategory.SpawnLimits &&
                                       (maximumModules < moduleCategory.SpawnMinimum || moduleCategory.SpawnMaximum < moduleCategory.SpawnMinimum));
        }

        /// <summary>
        /// Validates the cumulative spawn weights of both <see cref="ModuleAsset"/> and <see cref="ModuleCategory"/>.
        /// </summary>
        /// <returns>
        /// <c>true</c> if any category's assets or the combined categories have a total spawn weight outside the valid range; otherwise, <c>false</c>.
        /// </returns>
        private bool ContainsInvalidWeights()
        {
            var categoryWeight = 0f;

            foreach (var moduleElement in moduleData)
            {
                var assetWeight = moduleElement.ModuleAssets.Sum(moduleAsset => moduleAsset.SpawnRate);
                if (assetWeight is < 0.99f or > 1f) return true;

                categoryWeight += moduleElement.ModuleCategory.SpawnRate;
            }

            return categoryWeight is < 0.99f or > 1f;
        }

        /// <summary>
        /// Determines whether any <see cref="ModuleCategory"/> in <see cref="moduleData"/> has spawn limits that make it impossible to satisfy.
        /// </summary>
        /// <remarks>
        /// A <see cref="ModuleCategory"/> is considered to be impossible if:
        /// <list type="bullet">
        /// <item><description>The <see cref="ModuleCategory.SpawnLimits"/> flag is set.</description></item>
        /// <item>
        ///     <description>
        ///         The number of unique <see cref="ModuleAsset"/> entries that can only spawn once is less than the <see cref="ModuleCategory"/>'s <see cref="ModuleCategory.SpawnMinimum"/> requirement.
        ///     </description>
        /// </item>
        /// </list>
        /// </remarks>
        /// <returns><c>true</c> if one or more <see cref="ModuleCategory"/> have unsatisfiable spawn requirements; otherwise <c>false</c>.</returns>
        private bool ContainsImpossibleRequirements()
        {
            foreach (var moduleElement in moduleData)
            {
                var moduleAssets = moduleElement.ModuleAssets;
                var moduleCategory = moduleElement.ModuleCategory;
                if (moduleAssets == null || moduleCategory == null) continue;

                var uniqueCount = moduleAssets.Count(moduleAsset => moduleAsset.SpawnOnce);

                var allUnique = moduleAssets.Length == uniqueCount;
                var spaceUnavailable = uniqueCount < moduleCategory.SpawnMinimum;

                if (allUnique && moduleCategory.SpawnLimits && spaceUnavailable) return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether there are module categories that do not have a valid list of <see cref="ModuleAsset"/>.
        /// </summary>
        /// <returns>
        /// <c>true</c> if one or more categories do not contain a valid list of <see cref="ModuleAsset"/>; otherwise, <c>false</c>.
        /// </returns>
        private bool ContainsUnassignedCategories()
        {
            return moduleData.Any(moduleElement => moduleElement.ModuleAssets == null || moduleElement.ModuleAssets.Length <= 0);
        }

        /// <summary>
        /// Determines whether two module categories share a title, which would make connection rules ambiguous.
        /// </summary>
        private bool ContainsDuplicateTitles()
        {
            var categoryTitles = moduleData
                .Select(moduleElement => moduleElement.ModuleCategory?.CategoryTitle?.Trim().ToLowerInvariant())
                .Where(categoryTitle => !string.IsNullOrEmpty(categoryTitle))
                .ToList();

            return categoryTitles.Count != categoryTitles.Distinct().Count();
        }

        /// <summary>
        /// Determines whether the total number of required modules in <see cref="moduleData"/> exceeds <see cref="maximumModules"/>.
        /// </summary>
        /// <remarks>
        /// When a <see cref="ModuleCategory"/> defines <see cref="ModuleCategory.SpawnLimits"/>, its <see cref="ModuleCategory.SpawnMinimum"/> value is used to calculate the total required count.
        /// </remarks>
        /// <returns><c>true</c> if the required modules can't fit in the theme; otherwise, <c>false</c>.</returns>
        private bool ValidateRequiredModules()
        {
            var minimumRequired = 0;

            foreach (var moduleElement in moduleData)
            {
                var moduleCategory = moduleElement.ModuleCategory;
                if (moduleCategory.SpawnRequired)
                {
                    minimumRequired += moduleCategory.SpawnLimits ? moduleCategory.SpawnMinimum : 1;
                }
            }

            return maximumModules < minimumRequired;
        }

        /// <summary>
        /// Determines whether any role a path-first layout depends on has no category.
        /// </summary>
        /// <param name="missingRoles">A comma-separated list of the missing roles.</param>
        private bool MissingPathRoles(out string missingRoles)
        {
            var requiredRoles = new[] { ModuleRole.Start, ModuleRole.MainPath, ModuleRole.Boss, ModuleRole.End };
            var missing = requiredRoles.Where(requiredRole => CategoriesWithRole(requiredRole).Count <= 0).ToList();

            missingRoles = string.Join(", ", missing);
            return 0 < missing.Count;
        }

        /// <summary>
        /// Determines whether any path setting has its minimum above its maximum, or allows branches with no rooms.
        /// </summary>
        private bool ContainsInvalidRanges()
        {
            return !mainPathLength.IsValid ||
                   !branchCount.IsValid ||
                   !branchDepth.IsValid ||
                   !connectorsBetweenRooms.IsValid ||
                   branchDepth.Minimum < 1;
        }

        /// <summary>
        /// Logs warnings for connection rules that name categories missing from this theme, and for structural
        /// categories that no rule lets connect to anything. These are warnings rather than errors because the rules
        /// asset may be shared between themes.
        /// </summary>
        private void WarnAboutConnectionRules()
        {
            if (connectionRules == null) return;

            var themeCategories = moduleData
                .Select(moduleElement => moduleElement.ModuleCategory)
                .Where(moduleCategory => moduleCategory != null)
                .ToList();

            foreach (var connectionRule in connectionRules.Rules)
            {
                foreach (var ruleTitle in new[] { connectionRule.FirstCategory, connectionRule.SecondCategory })
                {
                    if (themeCategories.Any(moduleCategory => ConnectionRule.SameTitle(moduleCategory.CategoryTitle, ruleTitle))) continue;

                    Debug.LogWarning($"[{name}] Connection rule refers to category '{ruleTitle}', which is not in this theme.", this);
                }
            }

            foreach (var moduleCategory in themeCategories)
            {
                var isStructural = moduleCategory.SpawnRequired || moduleCategory.Role is ModuleRole.Start or ModuleRole.MainPath or ModuleRole.Boss or ModuleRole.End;
                if (!isStructural) continue;
                if (themeCategories.Any(otherCategory => CanConnect(moduleCategory, otherCategory))) continue;

                Debug.LogWarning($"[{name}] Category '{moduleCategory.CategoryTitle}' is required but no connection rule lets it connect to anything.", this);
            }
        }

        #endif
    }
}
