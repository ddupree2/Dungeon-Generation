using StarStoneStudio.DungeonGeneration.Attributes;
using UnityEditor;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// Provides shared visual settings and assets for <see cref="RequiredAttribute"/> validation UI.
    /// </summary>
    internal static class RequiredVisuals
    {
        public const float IconSize = 15;
        public static readonly Texture RequiredIcon = EditorGUIUtility.FindTexture("console.erroricon");
    }
}
