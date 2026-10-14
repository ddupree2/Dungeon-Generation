# Dungeon Generation
A modular dungeon generation system built using C# and Unity that assembles pre-built module prefabs and connects them procedurally. Inspired by titles such as *Lethal Company* and *R.E.P.O.*, this tool focuses on generation using constraints, weighted randomness, and backtracking to ensure valid dungeon layouts.

[![Demo Video](https://github.com/user-attachments/assets/ad0d322d-22ff-4d9c-8d5e-13c1c731f1fd)](https://www.youtube.com/watch?v=wXb4MGtWiRc)

## Installation
This repo is a Unity package (`com.starstonestudio.dungeongeneration`). Add it as a git submodule and reference it from `Packages/manifest.json`:

```json
"com.starstonestudio.dungeongeneration": "file:../DungeonGeneration"
```

Or install by git URL: `https://github.com/ddupree2/Dungeon-Generation.git`.

The demo scene, sample theme and module prefabs can be imported from **Package Manager > Dungeon Generation > Samples**. The demo player needs the Input System and Cinemachine packages.

## System Overview
This project was design to study the dungeon generation systems in titles such as *Lethal Company* and *R.E.P.O.*, which feature consistent, hand-crafted rooms reused across playthroughs.

A core development goal was to build a flexible, configurable system suitable for future projects.

The system is organized into three main layers:
1. **Themes** - High-level dungeon templates that define visual style, atmosphere, and generation rules
2. **Module Categories** - Logical groupings of modules that share behavior (e.g. Hallways, Rooms, Spawn)
3. **Module Assets** - Individual prefabs with associated spawning rules such as spawn rate, uniqueness, etc.

Each module prefab must contain have a `DungeonModule` MonoBehaviour script at its root which defines the module bounds and its entrance/exit connection points.

The `DungeonGenerator` MonoBehaviour script drives the overall generation process, using a selected theme and user-defined constraints to assemble a comprehensive dungeon layout.

### User-Defined Constraints
| Constraint | Description |
| ---------- | ----------- |
| Dungeon Size | Defines the maximum and minimum number of modules per dungeon |
| Categorical Beahvior | Groups modules into categories with shared spawning rules |
| Spawn Limits | Defines the maximum and minimum number of modules per category |
| Spawn Rates | Weighted probabilities for themes, categories, and individual modules |
| Required Modules | Categories that must appear in every dungeon (e.g. spawn rooms) |
| Unique Modules | Modules that can appear only once per dungeon (e.g. exit room) |
| Optional Looping | Enables circular connections for non-linear level design |

<details>
  <summary><b>Configuration Examples</b></summary>
  <img width="824" height="426" alt="image" src="https://github.com/user-attachments/assets/76b8f034-9013-48f5-84dc-bf40fabafe92" />
  <img width="826" height="735" alt="image" src="https://github.com/user-attachments/assets/7a1811ac-176a-4987-9e56-e973ebfe6605" />
  <img width="549" height="386" alt="image" src="https://github.com/user-attachments/assets/d27be10a-c3a5-4005-bcec-ee654fb0886b" />
</details>

## The Algorithm
The generation process is handled by the `DungeonGenerator` MonoBehaviour script and proceeds as follows:
### 1. Initialization
- Builds an alias probability table containing all available themes for weighted random selection
- Samples a theme from this table
- For the chosen theme, builds alias probability tables for:
    - Module Categories
    - Module Assets (within each category)
- Initializes internal structures such as the target module count, a stack for module placement history, and more
### 2. Generation Loop
The generator then enters a loop that continues until the target module count is reached or a generation error occurs (which then triggers an optional failure callback).
#### Step A. Module Selection
- If the number of remaining open connections equals the number of required modules left to instantiate, a required module is selected
- Otherwise, a module is chosen randomly using weighted alias sampling
#### Step B. Placement Attempt
- If this is **not** the first module:
    - Find an open connection from an already-placed module
    - If no open connections exist, attempt backtracking:
        - Pop the most recently placed module from the history stack
        - Reopen its connections
        - Retry placement
    - If an open connection is found:
        - Align the active module's entrance with the selected open connection
        - Check for intersections with existing modules (via bounds checking)
        - If an intersection is detected, discard and try another module
        - If there is no intersection detected, place the module and mark the used connection as closed
#### Step C. Loop Resolution (Optional)
- If the `Enable Loops` option is active, the generator may attempt to create secondary connections between the active module and other open connections in the dungeon, allowing circular pathways
### 3. Completion
- Once the target module count is met, generation stops
- A success callback is triggered (if configured)
- The resulting dungeon is fully ocnnected and respects all constraints

## Path-First Layouts
Set a theme's **Layout Mode** to **Path First** to build a guaranteed **Start → main path → Boss → End** structure with optional side branches, instead of the weighted "attach anywhere" layout described above.

1. Give every module category a **Role**: `Start`, `MainPath`, `Boss`, `End`, `Branch` or `Connector` (hallways and other pieces between rooms).
2. Set the theme's path settings: main path length, branch count, branch depth (all counted in rooms) and connectors between rooms.
3. Optionally create **Dungeon Connection Rules** (Create menu) and assign them to the theme. Rules name categories by title and are symmetric. For example, with a default of *allow*, deny `Room–Room`, `Room–Boss` and `Room–Start` and allow `Boss–End`: every room is reached through hallways and the boss exits straight into the end room.

Rooms are placed as units (the room plus the connectors leading into it), so a branch never ends in a dead-end hallway, and backtracking only ever pops main-path units. Mark a `Branch` category **Spawn Required** to guarantee it appears (shops, for example).

After generation, every placed `DungeonModule` exposes its `Category`, `Role`, `ParentModule`, `Depth` (rooms from Start, not counting connectors) and `IsMainPath`, and the generator exposes `PlacedModules`, `LoopConnections` and `LastResult` (including the seed). Use `Depth` to scale encounter difficulty.

### Testing
`Tools > Dungeon Generation > Batch Tester` (or **Open Batch Tester** on the generator) generates hundreds of seeds and reports failures, failure reasons, backtracking, timing and any layout rule violations. Every failure lists its seed with a **Replay** button. Run it before and after every generator change. `Tools > Dungeon Generation > Create Test Content` builds a set of placeholder primitive modules with a path-first theme and connection rules to test against.

## Room Prefab Conventions
Rules for authoring module prefabs so game code and the generator don't fight each other.

- **No logic in `Awake` or `Start` on room scripts.** Every placement attempt instantiates the full prefab and destroys it if it doesn't fit, so `Awake` and `Start` also run on rooms that get thrown away. Implement `IDungeonModuleListener` instead: after generation succeeds in Play Mode the generator calls `Activate(module, generator)` on every listener in each final module. Spawning rooms inactive is not an alternative, because inactive colliders are ignored by the overlap check.
- **Bounds colliders** should be slightly smaller than the room's real footprint, so modules meeting at a doorway aren't counted as overlapping, and should sit on a dedicated layer included in the generator's **Placement Layers** that doesn't collide with the player or enemies. The batch tester reports bounds colliders outside the placement layers.
- **Doorways** should be rotated in 90° steps around Y only. Alignment handles any angle, but the overlap check uses axis-aligned bounds. `DungeonModule` warns about doorways that break this.
- **Boss rooms** need at least two doorways (entrance and exit), and any room that something attaches to next needs a spare doorway; the generator rejects modules that would leave none.

## License
This project is licensed under the MIT License. For more information, see the LICENSE file.
