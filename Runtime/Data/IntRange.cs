using System;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// An inclusive range of whole numbers, such as "between 3 and 5 rooms".
    /// </summary>
    [Serializable]
    public struct IntRange
    {
        [Min(0)]
        [SerializeField]
        private int minimum;

        [Min(0)]
        [SerializeField]
        private int maximum;

        public IntRange(int minimum, int maximum)
        {
            this.minimum = minimum;
            this.maximum = maximum;
        }

        /// <summary>
        /// The smallest allowed value.
        /// </summary>
        public int Minimum => minimum;

        /// <summary>
        /// The largest allowed value.
        /// </summary>
        public int Maximum => maximum;

        /// <summary>
        /// Whether the range is non-negative and its minimum does not exceed its maximum.
        /// </summary>
        public bool IsValid => 0 <= minimum && minimum <= maximum;

        /// <summary>
        /// Whether <paramref name="value"/> falls inside the range.
        /// </summary>
        public bool Contains(int value)
        {
            return minimum <= value && value <= maximum;
        }

        public override string ToString()
        {
            return $"{minimum}-{maximum}";
        }
    }
}
