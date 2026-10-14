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

### 7. Module roles

- [ ] Add a role to module categories: **`Start`, `MainPath`, `Boss`, `End`, `Branch`, `Connector`**.
- [ ] Theme settings: main path length (min/max, counted in rooms, not connectors), branch count (min/max), branch depth (min/max), connectors between rooms (min/max).

### 8. Generation phases

1. [ ] **Start:** place one `Start` module at the origin.
2. [ ] **Main path:** choose a length N. Each new `MainPath` module attaches to an open doorway on the **previous main-path module**, not any module. This is the change that turns the blob into a path. Backtracking pops main-path modules only.
3. [ ] **Boss:** attach a `Boss` module to the last main-path module. Boss prefabs must have at least two doorways (entrance and exit).
4. [ ] **End:** attach an `End` module to the boss's remaining doorway. If boss or end placement fails, backtrack one or two main-path modules and retry.
5. [ ] **Branches:** for leftover open doorways on main-path modules, grow short branches (1–3 modules) from `Branch` categories (shops, treasure, optional fights). If a branch doesn't fit, skip it; no backtracking needed.
6. [ ] **Close remaining doorways** using each passage's closed state.

### 9. Connection rules

Currently any module can attach to any other module, so rooms end up packed directly against each other. Add DunGen-style connection rules that control which categories may connect at a doorway.

**Rules asset**
- [ ] `ConnectionRules` ScriptableObject referenced by the theme.
- [ ] Default policy: **allow unless denied** or **deny unless allowed**.
- [ ] List of category pairs, each marked **Allow** or **Deny**. Rules are symmetric (Room–Hallway also covers Hallway–Room).
- [ ] Editor validation: warn about pairs that reference categories not in the theme, and about required categories that no rule allows to connect to anything.

**Applying rules**
- [ ] When filling a doorway, filter candidates to categories allowed next to that module **before** the weighted pick, so attempts aren't wasted on modules that could never be legal there. (Alias tables need to be built per allowed set and cached, or use rejection sampling with an attempt cap.)
- [ ] Rules also apply to secondary (loop) connections.

**Sequence settings (rules control what can touch, not how many)**
- [ ] **Consecutive connectors (min/max):** how many `Connector` modules appear in a row between rooms on the main path and branches.
- [ ] **Branches must end in a room.** If a branch's final room fails to place, remove that branch's connectors too. No dead-end hallway stubs.
- [ ] Backtracking treats a room plus the connectors leading into it as one unit, popping them together.

**Example setup:** default *allow*, deny Room–Room, Room–Boss, Room–Start; allow Boss–End directly. Result: every room is reached through hallways, and the boss exits straight into the end room.

**Done when:** the batch tester confirms every doorway connection in every successful layout obeys the rules, consecutive connector counts stay within min/max, and no branch ends in a connector.

### 10. Depth tracking

- [ ] Store each module's depth (rooms from Start, not counting connectors) and whether it's on the main path or a branch.
- [ ] Expose this on the placed module so game code can scale encounter difficulty by depth.

**Done when:** the batch tester shows 100% of successful layouts have Start first, Boss directly before End, End as the last main-path module, and branches only off main-path modules.

---

## Tier 4: Room prefab conventions

Rules for authoring room prefabs, so the game code and the generator don't fight each other.

### 11. No logic in `Awake` or `Start` on room scripts

- [ ] Every placement attempt instantiates the full prefab and destroys it if it doesn't fit, up to 25 attempts per placement. `Awake` and `Start` run on rooms that get thrown away.
- [ ] **Rule:** room controllers do nothing in `Awake` or `Start`. After generation succeeds, the generator calls `Activate()` (via an interface such as `IDungeonModuleListener`) on each final module.
- [ ] Spawning rooms inactive does **not** work as an alternative, because inactive colliders are ignored by the overlap check.

### 12. Bounds colliders

- [ ] Make bounds colliders slightly smaller than the room's real footprint, so modules meeting at a doorway aren't counted as overlapping.
- [ ] Put bounds colliders on a dedicated layer (the generator's `placementLayers`) that doesn't collide with the player or enemies.

### 13. Doorway orientation

- [ ] Keep all doorway entrance points rotated in 90° steps around Y. Alignment handles any angle, but the overlap check uses axis-aligned bounds, which are only accurate at 90° rotations.

---

## Later / optional

- [ ] Doorway sockets (per-doorway tags such as size or type), checked alongside category rules, so wide gates only connect to wide gates.
- [ ] Plan the layout as data first and instantiate only the final rooms, removing the instantiate-and-destroy cost and the physics dependency.
- [ ] Re-enable loops once backtracking handles secondary connections correctly.
- [ ] Generate over several frames (coroutine or async) for large layouts.
