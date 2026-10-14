# Dungeon Generation

A Unity package that builds dungeons from hand-made room prefabs. You author rooms with doorways, group them into categories, and the generator snaps them together at their doorways, using weighted randomness, connection rules and backtracking to guarantee a valid layout. Inspired by games like *Lethal Company* and *R.E.P.O.*, where the same hand-crafted rooms are reused across playthroughs.

[![Demo Video](https://github.com/user-attachments/assets/ad0d322d-22ff-4d9c-8d5e-13c1c731f1fd)](https://www.youtube.com/watch?v=wXb4MGtWiRc)

**Contents**
- [Installation](#installation)
- [Quick start (5 minutes)](#quick-start-5-minutes)
- [How it fits together](#how-it-fits-together)
- [Building a room prefab](#building-a-room-prefab)
- [Setting up a theme](#setting-up-a-theme)
- [Connection rules](#connection-rules)
- [The generator component](#the-generator-component)
- [Using it from game code](#using-it-from-game-code)
- [Testing layouts](#testing-layouts)
- [Troubleshooting](#troubleshooting)
- [How the algorithms work](#how-the-algorithms-work)

---

## Installation

The package id is `com.starstonestudio.dungeongeneration` and it needs Unity 6000.6 or newer. Its only dependency, `com.unity.ai.navigation`, is installed automatically.

**As an editable submodule** (recommended if you'll change the generator):

```sh
git submodule add https://github.com/ddupree2/Dungeon-Generation.git DungeonGeneration
```

```json
// Packages/manifest.json
"com.starstonestudio.dungeongeneration": "file:../DungeonGeneration"
```

Edits made in Unity land in the submodule; commit and push them there, then commit the updated submodule pointer in your game repo.

**Read-only by git URL:** add `https://github.com/ddupree2/Dungeon-Generation.git` in Package Manager.

The original demo (scene, art, sample theme, first-person player) can be imported from **Package Manager > Dungeon Generation > Samples**. The demo player needs the Input System and Cinemachine packages.

---

## Quick start (5 minutes)

1. Open an empty scene.
2. Run **Tools > Dungeon Generation > Create Test Content**. This creates `Assets/DungeonTestContent` (placeholder rooms, a path-first theme and connection rules) and a **Dungeon Generator (Test)** object in the scene. It also adds a `DungeonBounds` layer to the project if one doesn't exist.
3. Select the generator and click **Generate Dungeon**. Click it again for a new layout, or set **Seed** to a non-zero number to get the same layout every time.
4. Click **Open Batch Tester**, then **Run Batch** to generate 200 layouts and check them all.

The test content is colour coded: green Start, blue main-path rooms, red Boss, yellow End, purple and cyan branch rooms (Treasure and Shop), grey hallways. Use it as a reference while you build your own rooms.

---

## How it fits together

| Piece | What it is |
|---|---|
| **Module** | A room prefab with a `DungeonModule` component: its bounds and its doorways. |
| **Doorway** (`DungeonPassage`) | A point on the edge of a module where another module can attach, with an *open* and a *closed* visual. |
| **Module Asset** | One prefab entry in a theme, with a spawn rate and an optional "spawn once" flag. |
| **Module Category** | A group of module assets that share rules: a title, a **role**, spawn limits, whether it's required, and a spawn rate. |
| **Theme** (`DungeonTheme` asset) | The categories and settings for one kind of dungeon. Chooses a **layout mode**. |
| **Connection Rules** (asset, optional) | Which categories may connect to each other at a doorway. |
| **Generator** (`DungeonGenerator` component) | Picks a theme, builds the layout as child objects, and reports the result. |

There are two layout modes, chosen per theme:

- **Path First** (recommended): builds a guaranteed **Start → main path → Boss → End** route, then grows optional side branches off main-path rooms. Uses category roles, path settings and connection rules.
- **Weighted** (the original mode): attaches weighted random categories to any open doorway until the module count is reached. Produces blob-shaped layouts with no guaranteed order. Kept for existing themes.

---

## Building a room prefab

Every module is a prefab with a `DungeonModule` on its root.

```
Room_Square                ← DungeonModule
├── Geometry …             (floors, walls, props: anything)
├── Bounds                 ← BoxCollider, on your bounds layer
├── Doorway_North          ← entrance point: on the edge, blue arrow (+Z) facing OUT of the room
│   ├── Open               ← shown when something is connected here (e.g. an empty or a door frame)
│   └── Closed             ← shown when nothing connects (e.g. a wall piece)
└── Doorway_South …
```

1. **Bounds.** Add one or more colliders that cover the room's footprint and assign them to **Module Bounds**. Make them slightly smaller than the real footprint (a few centimetres on each side) so two rooms meeting at a doorway don't count as overlapping. Put them on a dedicated layer (for example `DungeonBounds`) that is included in the generator's **Placement Layers** and doesn't collide with the player or enemies.
2. **Doorways.** For each doorway, add an empty transform exactly on the edge of the footprint, rotated so its **forward (blue) axis points out of the room**. Rotate doorways in 90° steps around Y only: the overlap check uses axis-aligned bounds. Add it to **Module Entrances** with its **Entrance Point**, **Open State** and **Closed State** objects.
3. **Doorway sizes must match.** Any doorway can connect to any other, so keep doorway widths consistent across all rooms in a theme.
4. **Enough doorways.**
   - Start needs at least one.
   - Main-path rooms need at least two (in and out). Extra doorways are where branches grow.
   - Boss needs at least two (entrance and exit to End).
   - End and dead-end branch rooms can have one.
   - Hallways need at least two.

   The generator rejects a module that would leave no doorway for whatever comes next.
5. **No game logic in `Awake` or `Start`.** The generator instantiates rooms to test whether they fit and destroys the ones that don't, so `Awake`/`Start` also run on rooms that get thrown away. Put room setup in [`IDungeonModuleListener.Activate`](#using-it-from-game-code) instead. Don't try to spawn rooms inactive: inactive colliders are invisible to the overlap check.

The `DungeonModule` inspector logs a warning for doorways that aren't rotated in 90° steps, and an error if bounds or doorways are missing.

---

## Setting up a theme

Create one with **Create > Dungeon Theme**.

### Common settings

| Setting | Meaning |
|---|---|
| **Theme Title** | Display name. |
| **Layout Mode** | **Path First** or **Weighted** (see above). The inspector only shows the settings for the selected mode. |
| **Module Data** | A list of categories, each with its module assets. |

**Each category:**

| Field | Meaning |
|---|---|
| **Category Title** | Unique within the theme. Connection rules refer to categories by this title. |
| **Role** | The category's part in a path-first layout (see below). Ignored in weighted mode. |
| **Spawn Limits** / **Spawn Maximum** / **Spawn Minimum** | Caps how many modules come from this category. |
| **Spawn Required** | Weighted: at least one module from this category always spawns. Path-first: only meaningful for **Branch** categories (see below). |
| **Spawn Rate** | Relative weight when picking between categories. |

**Each module asset:**

| Field | Meaning |
|---|---|
| **Module Prefab** | The room prefab. |
| **Spawn Rate** | Relative weight when picking between modules in the same category. |
| **Spawn Once** | At most one of this prefab per dungeon (an exit room, a unique shop). |

### Path First settings

**Roles:**

| Role | Used for | How many |
|---|---|---|
| **Start** | The room the player spawns in. Placed at the generator's position. | Exactly 1 |
| **MainPath** | Rooms on the route from Start to Boss. | Main Path Length |
| **Boss** | The last room before End. Needs 2+ doorways. | Exactly 1 |
| **End** | The exit. Attaches to the Boss room's remaining doorway. | Exactly 1 |
| **Branch** | Optional side rooms: shops, treasure, optional fights. | Branch Count × Branch Depth |
| **Connector** | Hallways, corridors, stairs: pieces *between* rooms. Not counted as rooms. | Connectors Between Rooms, per gap |
| **None** | Ignored by path-first generation. | 0 |

A path-first theme must have at least one category each for **Start**, **MainPath**, **Boss** and **End**. Branch and Connector are optional.

**Path settings** (all are min–max ranges; a random value is picked each generation):

| Setting | Meaning |
|---|---|
| **Main Path Length** | How many MainPath rooms sit between Start and Boss. Connectors don't count. |
| **Branch Count** | How many side branches to grow off MainPath rooms. |
| **Branch Depth** | How many Branch rooms each branch contains (at least 1). |
| **Connectors Between Rooms** | How many Connector modules appear in a row between two rooms. |
| **Connection Rules** | Optional rules asset (see below). |

**Spawn rules in path-first mode:**

- Spawn rates are **relative weights** between categories with the same role, and between modules in the same category. They don't need to add up to 1, so you can ignore the inspector's red **Cumulative Weight** totals in this mode.
- **Spawn Maximum** caps a category. **Spawn Once** is respected.
- Mark a **Branch** category **Spawn Required** to guarantee it appears. With **Spawn Limits** on, it appears at least **Spawn Minimum** times. Required branches are placed first and may add branches beyond Branch Count.
- **Spawn Required** has no effect on the other roles, because their counts are fixed by the structure.

**A typical setup:**

| Category | Role | Modules |
|---|---|---|
| Start | Start | 1 spawn room |
| Room | MainPath | 3–6 combat rooms |
| Boss | Boss | 1–2 boss arenas |
| End | End | 1 exit room |
| Treasure | Branch | 2–3 small rooms |
| Shop | Branch, **Spawn Required** | 1 shop |
| Hallway | Connector | straight, corner, T-junction pieces |

with Main Path Length 3–5, Branch Count 1–3, Branch Depth 1–2, Connectors Between Rooms 1–2, and the rules from the next section.

### Weighted settings

| Setting | Meaning |
|---|---|
| **Maximum / Minimum Modules** | The layout keeps placing modules until it reaches a random count in this range. |

In weighted mode the category spawn rates must add up to about 1, and so must the module spawn rates within each category. The inspector shows the totals in red when they don't. Required categories are placed alongside the weighted ones until all of them are in.

**Theme validation:** a theme with errors (missing roles, a minimum above a maximum, duplicate titles, empty categories, and so on) logs them in the Console, and entering Play Mode is cancelled until they're fixed.

---

## Connection rules

Without rules, any category can attach to any other: rooms end up packed directly against each other. A **Connection Rules** asset (**Create > Dungeon Connection Rules**), assigned to a theme, controls which categories may meet at a doorway.

| Field | Meaning |
|---|---|
| **Default Policy** | **Allow Unless Denied**: anything not mentioned may connect. **Deny Unless Allowed**: only listed pairs may connect. |
| **Rules** | Pairs of **category titles** marked **Allow** or **Deny**. |

- Rules are **symmetric**: `Room – Hallway` also covers `Hallway – Room`.
- Titles are matched ignoring case and surrounding spaces.
- If a pair is both allowed and denied, **deny wins**.
- A category can be paired with itself (`Room – Room`).

**Example:** every room is reached through hallways, and the boss exits straight into the end room.

| Default policy: Allow Unless Denied | |
|---|---|
| Room – Room | Deny |
| Room – Start | Deny |
| Room – Boss | Deny |
| Room – Treasure, Room – Shop | Deny |
| Boss – End | Allow |

**How rules interact with connectors (path-first):**
- Candidate modules are filtered by the rules *before* the random pick, so no attempts are wasted on illegal pieces.
- If **Connectors Between Rooms** rolls 0 but the rules deny the direct connection, one connector is placed instead.
- **End attaches straight to Boss** whenever the rules allow `Boss – End`, which includes themes with no rules asset. Otherwise it gets connectors like any other room.
- A connector run is placed together with the room it leads to, so a branch never ends in a dead-end hallway.

Rules also apply to loop connections and to weighted themes. The theme logs a warning if a rule names a category that isn't in the theme, or if a required category isn't allowed to connect to anything.

---

## The generator component

Add `DungeonGenerator` to an empty GameObject. Generated modules become its children, positioned relative to it.

| Setting | Meaning |
|---|---|
| **Seed** | `0` uses a new random seed each time. Any other value reproduces the same layout (given the same themes and prefabs). |
| **Possible Themes** | Themes to choose from, each with an **Occurrence Rate** (relative weight). |
| **Placement Layers** | Layers checked for overlaps. Must include your bounds layer and nothing else that sits in the dungeon area (floors, terrain, props). |
| **Enable Loops** | After the layout is built, connect doorways that happen to line up with another open doorway (where the rules allow), creating circular routes. |
| **On Generation Failed / Success** | UnityEvents fired after each generation. |

**Inspector buttons:**
- **Generate Dungeon**
- **Generate NavMesh**: adds a `NavMeshSurface` to the generator and bakes it.
- **Reset Environment**: removes the generated modules.
- **Open Batch Tester**

Every generation logs its seed in the Console, so you can copy a seed from a bad layout into **Seed** to reproduce it.

---

## Using it from game code

```csharp
using StarStoneStudio.DungeonGeneration;

public class DungeonBootstrap : MonoBehaviour
{
    [SerializeField] private DungeonGenerator generator;

    private void Start()
    {
        // 0 = random seed. Pass a stored seed to rebuild the same dungeon (e.g. after loading a save).
        var result = generator.GenerateDungeon(0);

        if (!result.Succeeded)
        {
            Debug.LogError($"Dungeon failed (seed {result.Seed}): {GenerationResult.Describe(result.Error)}");
            return;
        }

        generator.GenerateNavMesh();
        SaveSeed(result.Seed);
    }
}
```

**Generator API**

| Member | Description |
|---|---|
| `GenerateDungeon()` | Generate using the inspector's Seed. |
| `GenerateDungeon(int seed, bool silent = false)` | Generate from a specific seed (`0` = random) within the current frame. Returns a `GenerationResult`. `silent` skips logging, callbacks and listeners. |
| `GenerateDungeon(int seed, GenerationOptions options)` | Same, with [options](#building-the-next-floor-in-the-background). |
| `GenerateDungeonAsync(seed, options, cancellationToken)` | Generate over several frames (Play Mode). `await` it for the `GenerationResult`. Same layout as the synchronous call for the same seed. |
| `ActivateModules()` | Call room listeners now, after a pass with `DeferActivation`. |
| `BuildNavMeshAsync()` | Bake the NavMesh in the background; returns an `AsyncOperation`. |
| `IsGenerating` / `ModulesActivated` | Whether a pass is running / whether listeners have been called for the current layout. |
| `LastResult` | The most recent `GenerationResult`: `Seed`, `Succeeded`, `Error`, `ThemeTitle`, `ModuleCount`, `BacktrackCount`, `ElapsedMilliseconds`. |
| `GenerationCompleted` | C# event raised after every non-silent generation. |
| `PlacedModules` | Every module in the layout, in placement order. |
| `LoopConnections` | Extra connections made by **Enable Loops**. |
| `SelectedTheme` | The theme used for the current layout. |
| `GenerateNavMesh()` / `ResetEnvironment()` | Bake the NavMesh in one go / clear the layout. Both bake only this generator's own modules. |

Generation is deterministic: the same seed and the same themes/prefabs always give the same layout, whether generated synchronously or asynchronously, and it never touches `UnityEngine.Random`, so other systems' randomness isn't affected.

**Per-module data** (`DungeonModule`, set after placement)

| Member | Description |
|---|---|
| `Role` / `Category` | The role and category the module was placed from. |
| `Depth` | Rooms from Start, not counting connectors. Start is 0; a hallway has the depth of the room it leads away from. Use it to scale difficulty. |
| `IsMainPath` | `true` for Start, main-path rooms, Boss, End and the hallways between them; `false` for branches. |
| `ParentModule` | The module this one was attached to (`null` for the first module). |

### Building the next floor in the background

`GenerateDungeonAsync` spreads generation over several frames so a floor can be built while the player is still playing the current one, with no loading screen and no hitch. It spends about `MillisecondsPerFrame` per frame (default 4 ms); a single placement attempt is never split, so a frame can run slightly over with very heavy rooms.

```csharp
using StarStoneStudio.DungeonGeneration;

public class FloorStreamer : MonoBehaviour
{
    // Two generators far apart (e.g. the next floor 500 units below), so their modules never overlap
    [SerializeField] private DungeonGenerator currentFloor;
    [SerializeField] private DungeonGenerator nextFloor;

    private async void PrepareNextFloor(int seed)
    {
        var options = new GenerationOptions
        {
            MillisecondsPerFrame = 3f,   // per-frame budget
            HideUntilComplete = true,    // no half-built floor on screen
            DeferActivation = true       // don't start room logic (enemies etc.) yet
        };

        var result = await nextFloor.GenerateDungeonAsync(seed, options, destroyCancellationToken);
        if (!result.Succeeded)
        {
            result = await nextFloor.GenerateDungeonAsync(0, options, destroyCancellationToken);   // try another seed
        }

        await nextFloor.BuildNavMeshAsync();
    }

    // Called when the player takes the exit
    private void EnterNextFloor(Transform player)
    {
        var start = nextFloor.PlacedModules[0];    // the Start room
        player.position = start.transform.position;

        nextFloor.ActivateModules();               // room listeners run now
        currentFloor.ResetEnvironment();
        (currentFloor, nextFloor) = (nextFloor, currentFloor);
    }
}
```

**`GenerationOptions`**

| Option | Effect |
|---|---|
| `MillisecondsPerFrame` | Async only: time budget per frame (0 = default 4 ms). |
| `HideUntilComplete` | New modules' renderers and lights stay off until the layout is complete. Colliders stay on, because the overlap check needs them. |
| `DeferActivation` | Don't call `IDungeonModuleListener.Activate` on success; call `ActivateModules()` yourself later. |
| `Silent` | No logging, callbacks, `GenerationCompleted` event or activation (batch testing). |

**Things to know:**
- **One pass at a time per generator.** A second request while a pass is running is refused with `GenerationInProgress`, and `ResetEnvironment()` is ignored until the pass ends. Use a second generator for a second floor.
- **Keep floors apart.** Each generator's overlap check sees every module on the placement layers, including another generator's, so place generators far enough apart that their floors can't overlap.
- **Cancelling** (your token, or destroying the generator) throws `OperationCanceledException` from the `await` and clears the partial layout.
- **Deactivating a finished floor is fine.** Calling `SetActive(false)` on a generator after its pass completes is a cheap way to park a ready floor. Don't deactivate it while it's generating: inactive colliders are invisible to the overlap check.

**Room logic: `IDungeonModuleListener`**

```csharp
using StarStoneStudio.DungeonGeneration;

public class CombatRoom : MonoBehaviour, IDungeonModuleListener
{
    // Do nothing in Awake/Start: this prefab may be a discarded placement attempt.
    public void Activate(DungeonModule module, DungeonGenerator generator)
    {
        var difficulty = 1 + module.Depth;
        SpawnEnemies(difficulty);

        if (module.Role == ModuleRole.Boss)
        {
            LockExitUntilBossDies();
        }
    }
}
```

`Activate` is called once on every `IDungeonModuleListener` anywhere in each final module's hierarchy, after a **successful** generation in **Play Mode**, or when you call `ActivateModules()` after a pass with `DeferActivation`. It isn't called for layouts generated in the editor, for silent generations, or for batch tests.

---

## Testing layouts

**Batch Tester:** open it with **Tools > Dungeon Generation > Batch Tester** or the generator's **Open Batch Tester** button.

1. Pick the generator and the number of **Runs** (200 is a good default).
2. **Start Seed** `0` tests random seeds. Any other value tests `seed, seed+1, …`, which is useful for before/after comparisons.
3. **Check Determinism** generates every seed twice and compares the layouts.
4. **Run Batch.**

The report shows:
- success rate and failure reasons
- backtracking and timing (average and max)
- **layout rule violations**: rule-breaking connections, connector runs outside the range, dead-end hallways, wrong Start/Boss/End order, branches not attached to the main path, wrong depths, overlapping bounds, bounds on the wrong layer
- every problem seed, with a **Replay** button that rebuilds it in the scene

The same report is logged to the Console. Run a batch before and after any change to rooms, themes or the generator. A healthy theme is at or near 100% with no violations.

**From scripts or CI**, e.g. Unity's `-executeMethod` in batch mode:

```csharp
var report = DungeonBatchRunner.Run(generator, runCount: 300, startSeed: 1, checkDeterminism: true);
Debug.Log(report);            // full text report
bool ok = report.AllPassed;   // no failures, no rule violations, deterministic
```

`DungeonTestContentBuilder.Build(folder)` and `DungeonTestContentBuilder.CreateGenerator(theme)` create the placeholder content from code.

---

## Troubleshooting

| Error | What it means / what to try |
|---|---|
| `MissingRoleCategory` | A path-first theme lacks a Start, MainPath, Boss or End category, or every module in one of them is unusable (missing prefab, spawn-once already used, spawn maximum reached). |
| `MainPathFailed` | The main path couldn't fit even after backtracking. Rooms may not have enough doorways, doorways may face into each other, the rules may be too strict (check that hallways can connect to rooms), or the main path may be too long for the room shapes. |
| `RequiredBranchFailed` | A required Branch category (e.g. the shop) couldn't be attached. Give main-path rooms more doorways, or make sure the rules let that branch category connect to a hallway or a main-path room. |
| `IterationLimitExceeded` *(weighted)* | Optional categories are capped (Spawn Maximum) below the module count. Raise the caps or lower Minimum/Maximum Modules. |
| `CategoryPlacementFailed` *(weighted)* | A spawn-limited category couldn't place enough modules. |
| `RequiredSamplingFailed` *(weighted)* | A required category asks for more modules than it has (often too few non-spawn-once modules). |
| `InsufficientSpace` / `InsufficientRequiredSpace` *(weighted)* | Required modules don't fit in the module count. |
| `InvalidBacktracking` | The backtracking budget ran out. Usually the same causes as `MainPathFailed`. |
| `NoOptionalCategories` *(weighted)* | Every category is required, but the module count needs more modules. |
| `GenerationInProgress` | This generator was already generating; the new request was ignored. Wait for the running pass (or use another generator). |

**Common setup problems:**
- **Everything fails immediately.** **Placement Layers** probably includes a layer used by something already in the scene (a floor, terrain), so every placement overlaps it. Use a dedicated bounds layer.
- **Rooms overlap visually.** The bounds don't cover the room geometry, or the bounds colliders aren't on a placement layer. The batch tester flags the second case.
- **Rooms never connect / constant backtracking.** Doorway forward axes point *into* the room instead of out, or doorways sit inside the footprint instead of on its edge.
- **Room scripts run on rooms that vanish.** Move the setup from `Awake`/`Start` into `IDungeonModuleListener.Activate`.
- **A layout can't be reproduced from its seed.** Something in a room prefab uses `UnityEngine.Random` or depends on frame timing during `Awake`. Run the batch tester with **Check Determinism** on.

---

## How the algorithms work

### Path First
1. **Start**: place a Start module at the generator's origin.
2. **Main path**: choose a length N. Each MainPath room attaches to an open doorway on the *previous* main-path room, never to an arbitrary module; that's what turns a blob into a path. Each room is placed as a **unit**: the room plus 0–n connectors leading into it, added and removed together.
3. **Boss**, then **End**, attach to the end of the path in the same way.
4. **Backtracking**: if a unit can't be placed after several attempts (each trying different modules and doorways), the previous main-path unit is removed and placed again. If nothing fits off the Start module, Start itself is placed again. Backtracking stops at a budget and reports `MainPathFailed`.
5. **Branches**: pick a MainPath room with a free doorway and grow 1–n Branch units off it. Required Branch categories go first. A branch that doesn't fit just stops; no backtracking is needed because units are all-or-nothing.
6. **Loops** (optional): connect doorways that already line up with another open doorway, where the rules allow.
7. **Close** every unused doorway (its Closed visual is shown), set depths, and call listeners.

### Weighted
1. Pick a theme and a target module count. Reserve the required modules.
2. Loop until the count is reached:
   - Place a required module, more likely as the remaining space runs out.
   - Otherwise sample a category and a module by weight, and attach it to a random open doorway that the rules allow.
3. Placement aligns the new module's doorway to the target doorway, rotating it to face the opposite way, and rejects it if its bounds overlap anything on the placement layers. After repeated failures the most recent module is removed (backtracking).
4. With loops enabled, each new module also connects any doorways that line up with existing ones.

Every random decision in both modes comes from a single `System.Random` created from the seed, so layouts are reproducible. Every loop has an attempt cap, so a bad theme fails with an error instead of freezing the editor.

---

## License
MIT. See [LICENSE.txt](LICENSE.txt). Original generator by Christalin Dorsey; extended and maintained by Star Stone Studio.
