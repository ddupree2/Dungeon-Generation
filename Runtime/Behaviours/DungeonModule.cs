using StarStoneStudio.DungeonGeneration.Attributes;
using StarStoneStudio.DungeonGeneration.Utils;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StarStoneStudio.DungeonGeneration
{
    /// <summary>
    /// Represents a modular section of a dungeon.
    /// </summary>
    [ExecuteAlways]
    public class DungeonModule : MonoBehaviour
    {
        [Required]
        [SerializeField]
        [Tooltip("The colliders that define the physical bounds of the dungeon module.")]
        private Collider[] moduleBounds;
        
        [SerializeField]
        [Tooltip("The entrances that can connect this module to other dungeon modules.")]
        private DungeonPassage[] moduleEntrances;

        /// <summary>
        /// A runtime list of entrances on this <see cref="DungeonModule"/> that are currently available for connections to other modules.
        /// These entrances are updated during dungeon generation.
        /// </summary>
        public List<DungeonPassage> ConnectableEntrances { get; private set; }

        /// <summary>
        /// Gets the collection of <see cref="Collider"/> components that define the physical bounds of this module.
        /// </summary>
        public Collider[] ModuleBounds => moduleBounds;

        /// <summary>
        /// The doorways defined on this module's prefab, whether or not they are connected.
        /// </summary>
        public IReadOnlyList<DungeonPassage> ModuleEntrances => moduleEntrances;

        /// <summary>
        /// The category this module was placed from. Set by the <see cref="DungeonGenerator"/> when the module is placed.
        /// </summary>
        public ModuleCategory Category { get; private set; }

        /// <summary>
        /// The role of this module's category in a path-first layout, or <see cref="ModuleRole.None"/>.
        /// </summary>
        public ModuleRole Role => Category?.Role ?? ModuleRole.None;

        /// <summary>
        /// The module this one was attached to when placed, or <c>null</c> for the first module.
        /// Loop connections made afterwards are not reflected here; see <see cref="DungeonGenerator.LoopConnections"/>.
        /// </summary>
        public DungeonModule ParentModule { get; private set; }

        /// <summary>
        /// How many rooms lie between the first module and this one, not counting connectors. The first module has depth 0,
        /// and a connector shares the depth of the room it leads away from. Game code can use this to scale encounter difficulty.
        /// </summary>
        public int Depth { get; internal set; }

        /// <summary>
        /// Whether this module is part of the Start → Boss → End main path (path-first layouts only).
        /// Branch rooms and the connectors leading to them are not.
        /// </summary>
        public bool IsMainPath { get; internal set; }

        /// <summary>
        /// Initializes this module's collection of connectable entrances at runtime.
        /// Each entrance is assigned a reference to its parent <see cref="DungeonModule"/> and activated in its open state to indicate availability for connection.
        /// </summary>
        private void Awake()
        {
            ConnectableEntrances = moduleEntrances.ToList();
            
            foreach (var connectableEntrance in ConnectableEntrances)
            {
                connectableEntrance.AssignModule(this);
            }
            
            ToggleConnectableEntrances(true);
        }
        
        /// <summary>
        /// Registers a new connectable entrance to this module's list of connectable entrances.
        /// </summary>
        /// <param name="dungeonPassage">The <see cref="DungeonPassage"/> instance representing the entrance to be registered.</param>
        public void RegisterConnectableEntrance(DungeonPassage dungeonPassage)
        {
            ConnectableEntrances.Add(dungeonPassage);
        }
        
        /// <summary>
        /// Removes an entrance from the list of available entrances.
        /// This should be called once an entrance has successfully been connected with another module's entrance.
        /// </summary>
        /// <param name="dungeonPassage">The <see cref="DungeonPassage"/> instance to be removed.</param>
        public void RemoveConnectableEntrance(DungeonPassage dungeonPassage)
        {
            ConnectableEntrances.RemoveAll(availableEntrance => availableEntrance == dungeonPassage);
        }
        
        /// <summary>
        /// Selects an unused entrance at random from the module.
        /// </summary>
        /// <param name="random">The random source to draw from, so results are reproducible for a given seed.</param>
        /// <returns>
        /// A <see cref="Transform"/> of the selected entrance or <c>null</c> if no entrances are available.
        /// </returns>
        public DungeonPassage SelectConnectableEntrance(System.Random random)
        {
            if (ConnectableEntrances.Count <= 0) return null;
            
            var entranceIndex = random.Next(0, ConnectableEntrances.Count);
            return ConnectableEntrances[entranceIndex];
        }
        
        /// <summary>
        /// Records where this module sits in the generated layout. Called by the <see cref="DungeonGenerator"/> once the module is placed.
        /// </summary>
        /// <param name="moduleCategory">The category the module was placed from.</param>
        /// <param name="parentModule">The module it was attached to, or <c>null</c> for the first module.</param>
        internal void AssignPlacement(ModuleCategory moduleCategory, DungeonModule parentModule)
        {
            Category = moduleCategory;
            ParentModule = parentModule;
        }
        
        /// <summary>
        /// Sets the current active state of all connectable entrances belonging to this module.
        /// When enabled, entrances are visually and functionally open for connection.
        /// When disabled, entrances are closed and unavailable for further connections.
        /// </summary>
        public void ToggleConnectableEntrances(bool isOpen)
        {
            foreach (var connectableEntrance in ConnectableEntrances)
            {
                connectableEntrance.EnableEntrance(isOpen);
            }
        }
        
        #if UNITY_EDITOR

        /// <summary>
        /// Ensures <see cref="moduleBounds"/> and <see cref="moduleEntrances"/> are not empty and logs errors if requirements are not met.
        /// If validation fails while in Play mode, the editor will immediately exit Play mode to prevent further issues regarding dungeon generation.
        /// </summary>
        public void OnValidate()
        {
            ObjectValidator.AssertConditions(
                this,
                (moduleBounds.Length <= 0, $"<b>{nameof(moduleBounds)}</b> must contain at least one element."),
                (moduleEntrances.Length <= 0, $"<b>{nameof(moduleEntrances)}</b> must contain at least one element.")
            );

            WarnAboutDoorwayOrientation();
        }

        /// <summary>
        /// Warns about doorways that are not rotated in 90° steps around Y. Alignment handles any angle, but the overlap
        /// check uses axis-aligned bounds, which are only accurate at 90° rotations.
        /// </summary>
        private void WarnAboutDoorwayOrientation()
        {
            if (moduleEntrances == null) return;

            foreach (var moduleEntrance in moduleEntrances)
            {
                var entrancePoint = moduleEntrance?.EntrancePoint;
                if (entrancePoint == null) continue;

                var localRotation = (Quaternion.Inverse(transform.rotation) * entrancePoint.rotation).eulerAngles;
                var yawOffset = Mathf.Abs(Mathf.DeltaAngle(localRotation.y, Mathf.Round(localRotation.y / 90f) * 90f));
                var isTilted = 0.5f < Mathf.Abs(Mathf.DeltaAngle(localRotation.x, 0f)) || 0.5f < Mathf.Abs(Mathf.DeltaAngle(localRotation.z, 0f));

                if (0.5f < yawOffset || isTilted)
                {
                    Debug.LogWarning($"[{name}] Doorway '{entrancePoint.name}' should be rotated in 90° steps around Y only; the overlap check assumes axis-aligned rooms.", this);
                }
            }
        }
                
        #endif
    }
}