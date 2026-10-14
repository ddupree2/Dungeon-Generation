# Fork Roadmap

Planned changes to this fork of the modular dungeon generator. The goal is a reusable, path-first room generator: hand-made room prefabs connected at doorways, with a guaranteed **Start → main path → Boss → End** structure and optional side branches.

Work through the tiers in order. Each tier makes the next one safer to change.

## Ground rules

- **Keep game-specific code out of this fork.** Room controllers, encounter logic, rewards, VFX, and forcefield visuals belong in the game project. This repo only knows about modules, doorways, and placement rules.
- **This repo is maintained independently as a UPM package** (`com.starstonestudio.dungeongeneration`). Upstream changes are not tracked automatically; port anything useful by hand.
- **Run the batch tester (Tier 2) before and after every generator change.**

Main files referenced below:

- `Runtime/Behaviours/DungeonGenerator.cs`
- `Runtime/Behaviours/DungeonModule.cs`
- `Runtime/Algorithms/AliasProbability.cs`

---

## Tier 1: Make it safe

### 1. Infinite loop guards

Three loops can spin forever with no exit condition:

- [x] **`InitializeRequiredModules`**: the inner `while (spawnCount < spawnLimit)` loop hits `continue` without counting when `SampleWeightedModule` returns null. That happens once every "spawn once" asset in the category has been used.
- [x] **`GenerateDungeon` main loop**: hits `continue` when the sampled category is already at its spawn maximum. If every optional category is maxed before `moduleLimit` is reached, the loop never exits.
- [x] **Category inner loop** (`while (categoryCount < categoryLimit)`): only increments on success, so repeated null samples spin forever.

**Fix:** give every loop an attempt counter. When it runs out, set a `GenerationError` and break. Don't try to make each loop smarter; just make it impossible for any of them to run forever.

**Done when:** a theme where every optional category is capped low, and a category made entirely of spawn-once assets, both fail cleanly with an error instead of freezing the editor.

### 2. Unique modules consumed on failed placement

- [x] `SampleWeightedModule` adds an asset to `uniqueModules` when it is *sampled*, not when it is *placed*. A spawn-once room (such as an exit) that fails to fit once can never spawn again.
- [x] Move the "mark as used" step into `RecordModule` (successful placement).
- [x] Remove it from `uniqueModules` in `TryBacktracking` when that module is undone.

### 3. Backtracking reported as a successful placement

- [x] `TryInstantiatingModule` and `TryPlacingModule` both `return TryBacktracking()`. That returns `true`, so callers count it as a placed module and per-category counts drift.
- [x] Backtracking changes state but is not a placement. Both paths should return `false` after backtracking so the caller retries.

### 4. Loop connections not undone on backtrack

- [x] `ResolveSecondaryConnections` closes doorways on *other* modules when loops are enabled, but `TryBacktracking` only reopens the single primary connection stored in `HistoryEntry`.
- [x] Secondary connections are recorded in `HistoryEntry` and reopened when backtracking.

### 4b. Discarded modules still block placement in Play Mode

- [x] `Destroy` is deferred to the end of the frame, so rejected and backtracked modules kept their colliders live for the rest of generation and caused false overlaps. `DestroyModule` now deactivates the object first.
- [x] Unity null checks (`is null` / `?.` on components) replaced with `== null` so missing `NavMeshSurface` etc. are detected in the editor.

---

## Tier 2: Make it reproducible

### 5. Single seeded random source

- [x] Replace every use of `UnityEngine.Random` with one `System.Random` instance created per generation and passed to everything that needs randomness.
- [x] This includes `AliasProbability.Sample()`, which currently calls `UnityEngine.Random.Range` and `UnityEngine.Random.value`.
- [x] When the seed is `0`, generate a seed, log it, and expose it (property or event) so any bad layout can be replayed exactly.
- [x] Stop calling `UnityEngine.Random.InitState`, which resets random state for the whole game.
- [x] Also covers `DungeonModule.SelectConnectableEntrance`. Results are exposed through `DungeonGenerator.LastResult` and the `GenerationCompleted` event (`GenerationResult` carries seed, error, module/backtrack counts and timing).
- [x] Fixed: `InitializeGeneration` cleared `generationError` after required-module setup, hiding setup failures.

**Done when:** the same seed and theme produce an identical layout every time, and generating a dungeon doesn't change any other system's random results.

### 6. Batch tester

- [x] Editor button or window that generates N seeds (default 200) in a row. (`Tools > Dungeon Generation > Batch Tester`, or "Open Batch Tester" on the generator inspector)
- [x] Reports: success/failure count, failure reasons, backtrack count (average and max), and time per layout.
- [x] Logs the seeds of failures so they can be replayed (console report + Replay buttons).
- [x] Optional determinism check (generates each seed twice and compares layout fingerprints) and layout rule checks via `DungeonLayoutValidator` (module count, bounds overlaps; Tier 3 adds structure checks).

---

## Tier 3: Path-first generation

Replaces the current main loop, which attaches each new module to *any* open doorway and produces blob-shaped layouts with no guaranteed order.

> **Implemented as a theme setting.** `DungeonTheme.LayoutMode` is `Weighted` (the original loop, still the default so existing themes keep working) or `PathFirst`. Path-first lives in `Runtime/Behaviours/DungeonGenerator.PathFirst.cs`. The weighted mode can be removed once nothing uses it.

### 7. Module roles

- [x] Add a role to module categories: **`Start`, `MainPath`, `Boss`, `End`, `Branch`, `Connector`**.
- [x] Theme settings: main path length (min/max, counted in rooms, not connectors), branch count (min/max), branch depth (min/max), connectors between rooms (min/max).
- [x] `ModuleRole.None` is the default; weighted themes ignore roles. Path-first themes are validated to have Start, MainPath, Boss and End categories.

### 8. Generation phases

1. [x] **Start:** place one `Start` module at the origin.
2. [x] **Main path:** choose a length N. Each new `MainPath` module attaches to an open doorway on the **previous main-path module**, not any module. This is the change that turns the blob into a path. Backtracking pops main-path modules only.
3. [x] **Boss:** attach a `Boss` module to the last main-path module. Boss prefabs must have at least two doorways (entrance and exit).
4. [x] **End:** attach an `End` module to the boss's remaining doorway. If boss or end placement fails, backtrack one or two main-path modules and retry.
5. [x] **Branches:** for leftover open doorways on main-path modules, grow short branches (1–3 modules) from `Branch` categories (shops, treasure, optional fights). If a branch doesn't fit, skip it; no backtracking needed.
6. [x] **Close remaining doorways** using each passage's closed state.

Notes: required `Branch` categories (`SpawnRequired`, repeated by `SpawnMinimum`) are placed first and may add branches beyond the branch count; if one can't be placed the pass fails with `RequiredBranchFailed`. Branch roots are `MainPath` rooms. With loops enabled, touching doorways are connected after the structure is complete (rules applied), so loops never need undoing.

### 9. Connection rules

Currently any module can attach to any other module, so rooms end up packed directly against each other. Add DunGen-style connection rules that control which categories may connect at a doorway.

**Rules asset**
- [x] `ConnectionRules` ScriptableObject referenced by the theme. Categories are inline data rather than assets, so rules name them by **title** (case-insensitive); the theme now requires unique category titles.
- [x] Default policy: **allow unless denied** or **deny unless allowed**.
- [x] List of category pairs, each marked **Allow** or **Deny**. Rules are symmetric (Room–Hallway also covers Hallway–Room).
- [x] Editor validation: warn about pairs that reference categories not in the theme, and about required categories that no rule allows to connect to anything.

**Applying rules**
- [x] When filling a doorway, filter candidates to categories allowed next to that module **before** the weighted pick, so attempts aren't wasted on modules that could never be legal there. (Alias tables need to be built per allowed set and cached, or use rejection sampling with an attempt cap.)
- [x] Rules also apply to secondary (loop) connections.

**Sequence settings (rules control what can touch, not how many)**
- [x] **Consecutive connectors (min/max):** how many `Connector` modules appear in a row between rooms on the main path and branches.
- [x] **Branches must end in a room.** If a branch's final room fails to place, remove that branch's connectors too. No dead-end hallway stubs.
- [x] Backtracking treats a room plus the connectors leading into it as one unit, popping them together.
- [x] Connector count per unit is random within the range; if it rolls 0 but the rules deny the direct connection it becomes 1. **End attaches straight to Boss when the rules allow Boss–End** (matches the example below).
- [x] Weighted themes also honour the rules: a module only attaches to modules its category may connect to.

**Example setup:** default *allow*, deny Room–Room, Room–Boss, Room–Start; allow Boss–End directly. Result: every room is reached through hallways, and the boss exits straight into the end room.

**Done when:** the batch tester confirms every doorway connection in every successful layout obeys the rules, consecutive connector counts stay within min/max, and no branch ends in a connector.

### 10. Depth tracking

- [x] Store each module's depth (rooms from Start, not counting connectors) and whether it's on the main path or a branch.
- [x] Expose this on the placed module so game code can scale encounter difficulty by depth. (`DungeonModule.Depth`, `IsMainPath`, `Role`, `Category`, `ParentModule`; `DungeonGenerator.PlacedModules` and `LoopConnections` give the whole graph.)
- [x] Batch tester checks all of the above per layout (`DungeonLayoutValidator`): rules on every connection, connector runs, no dead-end connectors, Start first, Boss → End, main path order and length, branch roots, depth consistency.

**Done when:** the batch tester shows 100% of successful layouts have Start first, Boss directly before End, End as the last main-path module, and branches only off main-path modules.

---

## Tier 4: Room prefab conventions

Rules for authoring room prefabs, so the game code and the generator don't fight each other. Documented in the README under **Room Prefab Conventions**.

### 11. No logic in `Awake` or `Start` on room scripts

- [x] Every placement attempt instantiates the full prefab and destroys it if it doesn't fit, up to 25 attempts per placement. `Awake` and `Start` run on rooms that get thrown away.
- [x] **Rule:** room controllers do nothing in `Awake` or `Start`. After generation succeeds, the generator calls `Activate()` (via an interface such as `IDungeonModuleListener`) on each final module.
- [x] Spawning rooms inactive does **not** work as an alternative, because inactive colliders are ignored by the overlap check.
- [x] `IDungeonModuleListener.Activate(module, generator)` is called on every listener in each final module after a successful pass in Play Mode (not in edit mode or batch tests).

### 12. Bounds colliders

- [x] Make bounds colliders slightly smaller than the room's real footprint, so modules meeting at a doorway aren't counted as overlapping.
- [x] Put bounds colliders on a dedicated layer (the generator's `placementLayers`) that doesn't collide with the player or enemies.
- [x] The batch tester reports bounds colliders outside the placement layers and any overlapping bounds.

### 13. Doorway orientation

- [x] Keep all doorway entrance points rotated in 90° steps around Y. Alignment handles any angle, but the overlap check uses axis-aligned bounds, which are only accurate at 90° rotations.
- [x] `DungeonModule.OnValidate` warns about doorways that aren't.

---

## Later / optional

- [ ] Doorway sockets (per-doorway tags such as size or type), checked alongside category rules, so wide gates only connect to wide gates.
- [ ] Plan the layout as data first and instantiate only the final rooms, removing the instantiate-and-destroy cost and the physics dependency.
- [x] Re-enable loops once backtracking handles secondary connections correctly. (Tier 1 #4; path-first connects loops after the structure is built.)
- [x] Generate over several frames (coroutine or async) for large layouts. `GenerateDungeonAsync` (Awaitable) with a per-frame budget, `HideUntilComplete`, `DeferActivation` + `ActivateModules()`, cancellation, and `BuildNavMeshAsync()`. Sync and async run the same step iterator, so a seed gives the same layout either way.

---

## Verification log

**2026-10-14, headless batch run** (Unity 6000.6.1f1, scratch project, `Create Test Content` modules, `DungeonBatchRunner`):

| Scenario | Seeds | Succeeded | Rule violations | Nondeterministic | Backtracks avg/max | ms avg/max |
|---|---|---|---|---|---|---|
| Path-first, defaults (main 3–5, branches 1–3, depth 1–2, connectors 1–2) | 300 | 100% | 0 | 0 | 0.1 / 2 | 12 / 109 |
| Path-first, loops enabled | 200 | 100% | 0 | 0 | 0.1 / 1 | 13 / 58 |
| Path-first, long (main 8–12, branches 3–5, depth 1–3) | 150 | 100% | 0 | not checked | 0.3 / 9 | 37 / 150 |
| Path-first, connectors 0–1 | 150 | 100% | 0 | not checked | 0.1 / 2 | 15 / 101 |
| Weighted (regression, same content) | 150 | 100% | 0 | not checked | 26.9 / 73 | 37 / 312 |

Generation leaves `UnityEngine.Random` untouched. Loops were not exercised: with the test content, facing doorways almost never meet (one Room–Room pair in 30 layouts, correctly denied by the rules).

Fixed by this run: a failed first unit off Start used to fail the whole pass (1 in 150 with connectors 0–1, when every retry picked a corner hallway that turns back into Start); Start is now placed again within the backtrack budget. Loop detection required `dot < -1` (never true) and accepted any opposite-facing doorway on the hit module; it now needs doorways that face each other (`dot < -0.99`) within 0.1 units.

**2026-10-14, async generation** (same harness, Play Mode in batch mode):
seeds 1–15 generated async with a 0.25 ms frame budget (16–48 frames each) match the synchronous layouts exactly; `HideUntilComplete` keeps
renderers off mid-pass and restores them; `DeferActivation` waits for `ActivateModules()`; a second request while generating returns
`GenerationInProgress`; cancelling throws and leaves no modules; `BuildNavMeshAsync` produces a NavMesh. All sync scenarios above unchanged.
