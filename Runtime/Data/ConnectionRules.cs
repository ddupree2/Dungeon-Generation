using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// What happens to a pair of categories that no <see cref="ConnectionRule"/> mentions.
    /// </summary>
    public enum ConnectionPolicy
    {
        /// <summary>Any two categories may connect unless a rule denies it.</summary>
        AllowUnlessDenied = 0,

        /// <summary>Two categories may only connect if a rule allows it.</summary>
        DenyUnlessAllowed = 1
    }

    /// <summary>
    /// Whether a <see cref="ConnectionRule"/> allows or denies its pair.
    /// </summary>
    public enum ConnectionRuleType
    {
        Allow = 0,
        Deny = 1
    }

    /// <summary>
    /// Allows or denies a doorway connection between two module categories. Rules are symmetric:
    /// Room–Hallway also covers Hallway–Room.
    /// </summary>
    [Serializable]
    public struct ConnectionRule
    {
        [SerializeField]
        [Tooltip("The title of the first module category, as written in the theme.")]
        private string firstCategory;

        [SerializeField]
        [Tooltip("The title of the second module category, as written in the theme. May be the same as the first.")]
        private string secondCategory;

        [SerializeField]
        [Tooltip("Whether modules from these two categories may connect at a doorway.")]
        private ConnectionRuleType ruleType;

        public ConnectionRule(string firstCategory, string secondCategory, ConnectionRuleType ruleType)
        {
            this.firstCategory = firstCategory;
            this.secondCategory = secondCategory;
            this.ruleType = ruleType;
        }

        public string FirstCategory => firstCategory;

        public string SecondCategory => secondCategory;

        public ConnectionRuleType RuleType => ruleType;

        /// <summary>
        /// Whether this rule applies to the given pair of category titles, in either order.
        /// </summary>
        public bool Matches(string categoryTitle, string otherCategoryTitle)
        {
            return (SameTitle(firstCategory, categoryTitle) && SameTitle(secondCategory, otherCategoryTitle)) ||
                   (SameTitle(firstCategory, otherCategoryTitle) && SameTitle(secondCategory, categoryTitle));
        }

        /// <summary>
        /// Compares category titles, ignoring case and surrounding whitespace so small typos in rules don't silently fail.
        /// </summary>
        public static bool SameTitle(string firstTitle, string secondTitle)
        {
            return string.Equals(firstTitle?.Trim(), secondTitle?.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Controls which module categories may connect to each other at a doorway (for example, deny Room–Room so rooms are
    /// always reached through hallways). Referenced by a <see cref="DungeonTheme"/>.
    /// </summary>
    /// <remarks>
    /// Rules control what can touch, not how many. Sequence settings such as connectors between rooms live on the theme.
    /// </remarks>
    [CreateAssetMenu(fileName = "Connection_Rules", menuName = "Dungeon Connection Rules")]
    public class ConnectionRules : ScriptableObject
    {
        [SerializeField]
        [Tooltip("What happens to category pairs that no rule mentions.")]
        private ConnectionPolicy defaultPolicy;

        [SerializeField]
        [Tooltip("Category pairs that are explicitly allowed or denied. If a pair is both allowed and denied, deny wins.")]
        private List<ConnectionRule> rules = new();

        public ConnectionPolicy DefaultPolicy => defaultPolicy;

        public IReadOnlyList<ConnectionRule> Rules => rules;

        /// <summary>
        /// Whether modules from the two categories may connect at a doorway.
        /// </summary>
        /// <param name="categoryTitle">The title of one category.</param>
        /// <param name="otherCategoryTitle">The title of the other category.</param>
        /// <returns><c>true</c> if the connection is allowed.</returns>
        public bool CanConnect(string categoryTitle, string otherCategoryTitle)
        {
            var explicitlyAllowed = false;

            foreach (var connectionRule in rules)
            {
                if (!connectionRule.Matches(categoryTitle, otherCategoryTitle)) continue;
                if (connectionRule.RuleType == ConnectionRuleType.Deny) return false;

                explicitlyAllowed = true;
            }

            return explicitlyAllowed || defaultPolicy == ConnectionPolicy.AllowUnlessDenied;
        }
    }
}
