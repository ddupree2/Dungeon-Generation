using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration.Editor
{
    /// <summary>
    /// Builds placeholder test content for path-first generation: primitive module prefabs for every role, connection rules,
    /// a path-first theme and a configured generator. Lets the generator and batch tester be exercised before real rooms exist.
    /// </summary>
    public static class DungeonTestContentBuilder
    {
        public const string DefaultFolder = "Assets/DungeonTestContent";
        public const string BoundsLayerName = "DungeonBounds";

        private const float WallHeight = 3f;
        private const float DoorWidth = 2.5f;

        /// <summary>
        /// How far bounds colliders sit inside the module footprint on each side, so modules meeting at a doorway don't overlap
        /// while the loop raycast (0.05 units) still reaches a neighbour's bounds.
        /// </summary>
        private const float BoundsInset = 0.04f;

        private enum Side { North, East, South, West }

        private readonly struct ModuleSpec
        {
            public readonly string Name;
            public readonly float Width;
            public readonly float Depth;
            public readonly Side[] Doorways;

            /// <summary>
            /// How far the North doorway sits above the South one. Non-zero makes the module a ramp up to the next level.
            /// </summary>
            public readonly float Rise;

            public ModuleSpec(string name, float width, float depth, params Side[] doorways)
                : this(name, width, depth, 0f, doorways)
            {
            }

            private ModuleSpec(string name, float width, float depth, float rise, Side[] doorways)
            {
                Name = name;
                Width = width;
                Depth = depth;
                Rise = rise;
                Doorways = doorways;
            }

            /// <summary>
            /// A straight ramp from a South doorway at floor level to a North doorway <paramref name="rise"/> units higher.
            /// </summary>
            public static ModuleSpec Stairs(string name, float width, float depth, float rise)
            {
                return new ModuleSpec(name, width, depth, rise, new[] { Side.South, Side.North });
            }
        }

        private readonly struct CategorySpec
        {
            public readonly string Title;
            public readonly ModuleRole Role;
            public readonly float SpawnRate;
            public readonly bool SpawnRequired;
            public readonly Color Color;
            public readonly (ModuleSpec spec, float spawnRate)[] Modules;

            public CategorySpec(string title, ModuleRole role, float spawnRate, bool spawnRequired, Color color, params (ModuleSpec, float)[] modules)
            {
                Title = title;
                Role = role;
                SpawnRate = spawnRate;
                SpawnRequired = spawnRequired;
                Color = color;
                Modules = modules;
            }
        }

        private static readonly CategorySpec[] Categories =
        {
            new("Start", ModuleRole.Start, 1f, false, new Color(0.3f, 0.8f, 0.3f),
                (new ModuleSpec("Start", 8, 8, Side.North, Side.East, Side.West), 1f)),
            new("Room", ModuleRole.MainPath, 1f, false, new Color(0.35f, 0.55f, 0.9f),
                (new ModuleSpec("Room_Square", 8, 8, Side.North, Side.East, Side.South, Side.West), 0.6f),
                (new ModuleSpec("Room_Long", 8, 12, Side.North, Side.South, Side.East), 0.4f)),
            new("Boss", ModuleRole.Boss, 1f, false, new Color(0.85f, 0.25f, 0.25f),
                (new ModuleSpec("Boss", 12, 12, Side.South, Side.North), 1f)),
            new("End", ModuleRole.End, 1f, false, new Color(0.95f, 0.85f, 0.2f),
                (new ModuleSpec("End", 6, 6, Side.South), 1f)),
            new("Treasure", ModuleRole.Branch, 0.6f, false, new Color(0.7f, 0.4f, 0.85f),
                (new ModuleSpec("Treasure_DeadEnd", 6, 6, Side.South), 0.5f),
                (new ModuleSpec("Treasure_Through", 6, 6, Side.South, Side.North), 0.5f)),
            new("Shop", ModuleRole.Branch, 0f, true, new Color(0.2f, 0.8f, 0.8f),
                (new ModuleSpec("Shop", 6, 8, Side.South, Side.North), 1f)),
            new("Hallway", ModuleRole.Connector, 1f, false, new Color(0.6f, 0.6f, 0.6f),
                (new ModuleSpec("Hallway_Straight", 4, 8, Side.South, Side.North), 0.5f),
                (new ModuleSpec("Hallway_Corner", 4, 4, Side.South, Side.East), 0.3f),
                (ModuleSpec.Stairs("Hallway_Stairs", 4, 8, 3f), 0.2f))
        };

        [MenuItem("Tools/Dungeon Generation/Create Test Content")]
        private static void CreateFromMenu()
        {
            var replaceExisting = !AssetDatabase.IsValidFolder(DefaultFolder) || EditorUtility.DisplayDialog(
                "Create Dungeon Test Content",
                $"Replace the existing test content in {DefaultFolder}?\n\nThis also names a free user layer '{BoundsLayerName}' for module bounds if one doesn't exist.",
                "Replace",
                "Cancel");

            if (!replaceExisting) return;

            var dungeonTheme = Build(DefaultFolder);
            var dungeonGenerator = CreateGenerator(dungeonTheme);

            Selection.activeGameObject = dungeonGenerator.gameObject;
            EditorGUIUtility.PingObject(dungeonTheme);
        }

        /// <summary>
        /// Builds (or rebuilds) the test prefabs, materials, connection rules and theme in <paramref name="folderPath"/>.
        /// </summary>
        /// <param name="folderPath">A project folder such as <c>Assets/DungeonTestContent</c>. Its previous contents are replaced.</param>
        /// <returns>The path-first theme referencing the generated content.</returns>
        public static DungeonTheme Build(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.DeleteAsset(folderPath);
            }

            CreateFolder(folderPath);
            CreateFolder($"{folderPath}/Materials");
            CreateFolder($"{folderPath}/Modules");

            var boundsLayer = EnsureLayer(BoundsLayerName);
            var closedMaterial = CreateMaterial($"{folderPath}/Materials/Closed.mat", new Color(0.15f, 0.15f, 0.18f));

            var categoryPrefabs = new List<(CategorySpec category, List<(GameObject prefab, float spawnRate)> prefabs)>();

            foreach (var categorySpec in Categories)
            {
                var categoryMaterial = CreateMaterial($"{folderPath}/Materials/{categorySpec.Title}.mat", categorySpec.Color);
                var prefabs = categorySpec.Modules
                    .Select(moduleEntry => (CreateModulePrefab($"{folderPath}/Modules", moduleEntry.spec, categoryMaterial, closedMaterial, boundsLayer), moduleEntry.spawnRate))
                    .ToList();

                categoryPrefabs.Add((categorySpec, prefabs));
            }

            var connectionRules = CreateConnectionRules($"{folderPath}/Test_ConnectionRules.asset");
            var dungeonTheme = CreateTheme($"{folderPath}/Test_PathFirstTheme.asset", categoryPrefabs, connectionRules);

            AssetDatabase.SaveAssets();
            return dungeonTheme;
        }

        /// <summary>
        /// Creates a <see cref="DungeonGenerator"/> in the active scene that uses the given theme and the bounds layer.
        /// </summary>
        public static DungeonGenerator CreateGenerator(DungeonTheme dungeonTheme)
        {
            var generatorObject = new GameObject("Dungeon Generator (Test)");
            Undo.RegisterCreatedObjectUndo(generatorObject, "Create Dungeon Generator");

            var dungeonGenerator = generatorObject.AddComponent<DungeonGenerator>();
            var serializedGenerator = new SerializedObject(dungeonGenerator);

            var themesProperty = serializedGenerator.FindProperty("possibleThemes");
            var keysProperty = themesProperty.FindPropertyRelative("dictionaryKeys");
            var valuesProperty = themesProperty.FindPropertyRelative("dictionaryValues");

            keysProperty.arraySize = 1;
            valuesProperty.arraySize = 1;
            keysProperty.GetArrayElementAtIndex(0).objectReferenceValue = dungeonTheme;
            valuesProperty.GetArrayElementAtIndex(0).floatValue = 1f;

            var boundsLayer = LayerMask.NameToLayer(BoundsLayerName);
            serializedGenerator.FindProperty("placementLayers").intValue = boundsLayer < 0 ? 1 : 1 << boundsLayer;

            serializedGenerator.ApplyModifiedPropertiesWithoutUndo();

            // The dictionary rebuilds its runtime lookup from the serialized arrays on deserialization
            EditorUtility.SetDirty(dungeonGenerator);
            return dungeonGenerator;
        }

        private static void CreateFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parentFolder = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parentFolder) && !AssetDatabase.IsValidFolder(parentFolder))
            {
                CreateFolder(parentFolder);
            }

            AssetDatabase.CreateFolder(parentFolder, Path.GetFileName(folderPath));
        }

        /// <summary>
        /// Returns the index of the named layer, naming the first free user layer if it doesn't exist yet.
        /// Falls back to the Default layer if every user layer is taken.
        /// </summary>
        private static int EnsureLayer(string layerName)
        {
            var existingLayer = LayerMask.NameToLayer(layerName);
            if (0 <= existingLayer) return existingLayer;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layersProperty = tagManager.FindProperty("layers");

            for (var layerIndex = 8; layerIndex < layersProperty.arraySize; layerIndex++)
            {
                var layerProperty = layersProperty.GetArrayElementAtIndex(layerIndex);
                if (!string.IsNullOrEmpty(layerProperty.stringValue)) continue;

                layerProperty.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return layerIndex;
            }

            Debug.LogWarning($"[{nameof(DungeonTestContentBuilder)}] No free user layer for '{layerName}'; module bounds use the Default layer.");
            return 0;
        }

        private static Material CreateMaterial(string assetPath, Color materialColor)
        {
            var materialShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var material = new Material(materialShader) { color = materialColor };

            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        /// <summary>
        /// Builds a module prefab: a floor, a bounds collider inset from the footprint, and a doorway on each requested side with
        /// an empty open state and a wall as the closed state. The floor (or ramp) has a solid collider on the Default layer so it can be walked on; the bounds collider is a trigger on the bounds layer.
        /// </summary>
        private static GameObject CreateModulePrefab(string folderPath, ModuleSpec moduleSpec, Material floorMaterial, Material closedMaterial, int boundsLayer)
        {
            var moduleRoot = new GameObject(moduleSpec.Name);

            try
            {
                CreateFloor(moduleRoot.transform, moduleSpec, floorMaterial);

                // Bounds cover the full walkable volume, including the extra height a ramp climbs
                var boundsHeight = WallHeight + moduleSpec.Rise;
                var boundsObject = new GameObject("Bounds") { layer = boundsLayer };
                boundsObject.transform.SetParent(moduleRoot.transform, false);

                var boundsCollider = boundsObject.AddComponent<BoxCollider>();
                boundsCollider.center = new Vector3(0f, boundsHeight / 2f, 0f);
                boundsCollider.size = new Vector3(moduleSpec.Width - BoundsInset * 2f, boundsHeight, moduleSpec.Depth - BoundsInset * 2f);
                boundsCollider.isTrigger = true;

                var doorways = moduleSpec.Doorways
                    .Select(doorwaySide => CreateDoorway(moduleRoot.transform, moduleSpec, doorwaySide, closedMaterial))
                    .ToList();

                AddDungeonModule(moduleRoot, boundsCollider, doorways);

                var prefabPath = $"{folderPath}/{moduleSpec.Name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(moduleRoot, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(moduleRoot);
            }
        }

        /// <summary>
        /// Builds a walkable floor with a box collider on the Default layer: a flat slab, or for stairs a ramp up to the North doorway.
        /// </summary>
        private static void CreateFloor(Transform moduleRoot, ModuleSpec moduleSpec, Material floorMaterial)
        {
            var floorObject = CreateVisual(PrimitiveType.Cube, "Floor", moduleRoot, floorMaterial, true);

            if (moduleSpec.Rise <= 0f)
            {
                floorObject.transform.localPosition = new Vector3(0f, -0.1f, 0f);
                floorObject.transform.localScale = new Vector3(moduleSpec.Width, 0.2f, moduleSpec.Depth);
                return;
            }

            // A slab tilted so its top surface runs from the South edge at floor level to the North edge at the rise
            var rampLength = Mathf.Sqrt(moduleSpec.Depth * moduleSpec.Depth + moduleSpec.Rise * moduleSpec.Rise);
            var rampAngle = Mathf.Atan2(moduleSpec.Rise, moduleSpec.Depth) * Mathf.Rad2Deg;
            var rampDown = Quaternion.Euler(-rampAngle, 0f, 0f) * new Vector3(0f, -0.1f, 0f);

            floorObject.transform.localRotation = Quaternion.Euler(-rampAngle, 0f, 0f);
            floorObject.transform.localPosition = new Vector3(0f, moduleSpec.Rise / 2f, 0f) + rampDown;
            floorObject.transform.localScale = new Vector3(moduleSpec.Width, 0.2f, rampLength);
        }

        private static (Transform entrancePoint, GameObject openState, GameObject closedState) CreateDoorway(Transform moduleRoot, ModuleSpec moduleSpec, Side doorwaySide, Material closedMaterial)
        {
            var (localPosition, yaw) = doorwaySide switch
            {
                Side.North => (new Vector3(0f, moduleSpec.Rise, moduleSpec.Depth / 2f), 0f),
                Side.East => (new Vector3(moduleSpec.Width / 2f, 0f, 0f), 90f),
                Side.South => (new Vector3(0f, 0f, -moduleSpec.Depth / 2f), 180f),
                _ => (new Vector3(-moduleSpec.Width / 2f, 0f, 0f), 270f)
            };

            // The entrance point sits on the footprint edge and faces outward
            var entrancePoint = new GameObject($"Doorway_{doorwaySide}").transform;
            entrancePoint.SetParent(moduleRoot, false);
            entrancePoint.localPosition = localPosition;
            entrancePoint.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var openState = new GameObject("Open");
            openState.transform.SetParent(entrancePoint, false);

            var closedState = CreateVisual(PrimitiveType.Cube, "Closed", entrancePoint, closedMaterial);
            closedState.transform.localPosition = new Vector3(0f, WallHeight / 2f, -0.15f);
            closedState.transform.localScale = new Vector3(DoorWidth, WallHeight, 0.2f);

            return (entrancePoint, openState, closedState);
        }

        private static GameObject CreateVisual(PrimitiveType primitiveType, string objectName, Transform parentTransform, Material visualMaterial, bool keepCollider = false)
        {
            var visualObject = GameObject.CreatePrimitive(primitiveType);
            visualObject.name = objectName;
            visualObject.transform.SetParent(parentTransform, false);

            if (!keepCollider)
            {
                Object.DestroyImmediate(visualObject.GetComponent<Collider>());
            }
            visualObject.GetComponent<MeshRenderer>().sharedMaterial = visualMaterial;

            return visualObject;
        }

        /// <summary>
        /// Adds and configures the <see cref="DungeonModule"/>. Logging is muted while the component is added, because its
        /// validation runs before the bounds and doorways can be assigned.
        /// </summary>
        private static void AddDungeonModule(GameObject moduleRoot, Collider boundsCollider, List<(Transform entrancePoint, GameObject openState, GameObject closedState)> doorways)
        {
            DungeonModule dungeonModule;
            var loggingEnabled = Debug.unityLogger.logEnabled;

            try
            {
                Debug.unityLogger.logEnabled = false;
                dungeonModule = moduleRoot.AddComponent<DungeonModule>();
            }
            finally
            {
                Debug.unityLogger.logEnabled = loggingEnabled;
            }

            var serializedModule = new SerializedObject(dungeonModule);

            var boundsProperty = serializedModule.FindProperty("moduleBounds");
            boundsProperty.arraySize = 1;
            boundsProperty.GetArrayElementAtIndex(0).objectReferenceValue = boundsCollider;

            var entrancesProperty = serializedModule.FindProperty("moduleEntrances");
            entrancesProperty.arraySize = doorways.Count;

            for (var doorwayIndex = 0; doorwayIndex < doorways.Count; doorwayIndex++)
            {
                var entranceProperty = entrancesProperty.GetArrayElementAtIndex(doorwayIndex);
                entranceProperty.FindPropertyRelative("entrancePoint").objectReferenceValue = doorways[doorwayIndex].entrancePoint;
                entranceProperty.FindPropertyRelative("openState").objectReferenceValue = doorways[doorwayIndex].openState;
                entranceProperty.FindPropertyRelative("closedState").objectReferenceValue = doorways[doorwayIndex].closedState;
            }

            serializedModule.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Rules matching the roadmap example: rooms are always reached through hallways, and the boss exits straight into the end room.
        /// </summary>
        private static ConnectionRules CreateConnectionRules(string assetPath)
        {
            var connectionRules = ScriptableObject.CreateInstance<ConnectionRules>();
            AssetDatabase.CreateAsset(connectionRules, assetPath);

            var roomTitles = new[] { "Start", "Room", "Boss", "Treasure", "Shop" };
            var ruleEntries = new List<(string, string, ConnectionRuleType)>();

            for (var firstIndex = 0; firstIndex < roomTitles.Length; firstIndex++)
            {
                for (var secondIndex = firstIndex; secondIndex < roomTitles.Length; secondIndex++)
                {
                    ruleEntries.Add((roomTitles[firstIndex], roomTitles[secondIndex], ConnectionRuleType.Deny));
                }
            }

            ruleEntries.Add(("Boss", "End", ConnectionRuleType.Allow));

            var serializedRules = new SerializedObject(connectionRules);
            serializedRules.FindProperty("defaultPolicy").enumValueIndex = (int)ConnectionPolicy.AllowUnlessDenied;

            var rulesProperty = serializedRules.FindProperty("rules");
            rulesProperty.arraySize = ruleEntries.Count;

            for (var ruleIndex = 0; ruleIndex < ruleEntries.Count; ruleIndex++)
            {
                var (firstCategory, secondCategory, ruleType) = ruleEntries[ruleIndex];
                var ruleProperty = rulesProperty.GetArrayElementAtIndex(ruleIndex);

                ruleProperty.FindPropertyRelative("firstCategory").stringValue = firstCategory;
                ruleProperty.FindPropertyRelative("secondCategory").stringValue = secondCategory;
                ruleProperty.FindPropertyRelative("ruleType").enumValueIndex = (int)ruleType;
            }

            serializedRules.ApplyModifiedPropertiesWithoutUndo();
            return connectionRules;
        }

        private static DungeonTheme CreateTheme(string assetPath, List<(CategorySpec category, List<(GameObject prefab, float spawnRate)> prefabs)> categoryPrefabs, ConnectionRules connectionRules)
        {
            var dungeonTheme = ScriptableObject.CreateInstance<DungeonTheme>();
            AssetDatabase.CreateAsset(dungeonTheme, assetPath);

            var serializedTheme = new SerializedObject(dungeonTheme);
            serializedTheme.FindProperty("themeTitle").stringValue = "Path-First Test";
            serializedTheme.FindProperty("layoutMode").enumValueIndex = (int)LayoutMode.PathFirst;
            serializedTheme.FindProperty("maximumModules").intValue = 50;
            serializedTheme.FindProperty("minimumModules").intValue = 1;
            serializedTheme.FindProperty("connectionRules").objectReferenceValue = connectionRules;

            SetRange(serializedTheme.FindProperty("mainPathLength"), 3, 5);
            SetRange(serializedTheme.FindProperty("branchCount"), 1, 3);
            SetRange(serializedTheme.FindProperty("branchDepth"), 1, 2);
            SetRange(serializedTheme.FindProperty("connectorsBetweenRooms"), 1, 2);

            var dataProperty = serializedTheme.FindProperty("moduleData");
            dataProperty.arraySize = categoryPrefabs.Count;

            for (var categoryIndex = 0; categoryIndex < categoryPrefabs.Count; categoryIndex++)
            {
                var (categorySpec, prefabs) = categoryPrefabs[categoryIndex];
                var dataElement = dataProperty.GetArrayElementAtIndex(categoryIndex);

                var categoryProperty = dataElement.FindPropertyRelative("moduleCategory");
                categoryProperty.FindPropertyRelative("categoryTitle").stringValue = categorySpec.Title;
                categoryProperty.FindPropertyRelative("role").enumValueIndex = (int)categorySpec.Role;
                categoryProperty.FindPropertyRelative("spawnLimits").boolValue = false;
                categoryProperty.FindPropertyRelative("spawnMaximum").intValue = 0;
                categoryProperty.FindPropertyRelative("spawnMinimum").intValue = 0;
                categoryProperty.FindPropertyRelative("spawnRequired").boolValue = categorySpec.SpawnRequired;
                categoryProperty.FindPropertyRelative("spawnRate").floatValue = categorySpec.SpawnRate;

                var assetsProperty = dataElement.FindPropertyRelative("moduleAssets");
                assetsProperty.arraySize = prefabs.Count;

                for (var assetIndex = 0; assetIndex < prefabs.Count; assetIndex++)
                {
                    var assetProperty = assetsProperty.GetArrayElementAtIndex(assetIndex);
                    assetProperty.FindPropertyRelative("modulePrefab").objectReferenceValue = prefabs[assetIndex].prefab;
                    assetProperty.FindPropertyRelative("spawnRate").floatValue = prefabs[assetIndex].spawnRate;
                    assetProperty.FindPropertyRelative("spawnOnce").boolValue = false;
                }
            }

            serializedTheme.ApplyModifiedPropertiesWithoutUndo();
            return dungeonTheme;
        }

        private static void SetRange(SerializedProperty rangeProperty, int minimum, int maximum)
        {
            rangeProperty.FindPropertyRelative("minimum").intValue = minimum;
            rangeProperty.FindPropertyRelative("maximum").intValue = maximum;
        }
    }
}
