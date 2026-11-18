# ZCollapse — performance notes

> **TODO (future optimization):** the system causes heavy server and client lag during mass collapse.
> Findings below are from profiling on 2026-09-28; none of the fixes are implemented yet.

## How it was measured

- `CEGridStability` added to a whole map in a local Tools build, 1 player, 60 s capture from the moment collapse started.
- `dotnet-trace collect --profile dotnet-sampled-thread-time,dotnet-common` on server and client at the same time, plus `dotnet-counters` (System.Runtime) on server.
- Numbers are main-thread time out of 60 s. Sampling granularity ~1 ms, so small items are noise.

## Server — `CEZCollapseSystem.Update` = 12.2 s / 60 s (20% of main thread), worst tick 662 ms

| Where | Time | Why |
|---|---|---|
| `StartPendingJobs` → `StartJob` (snapshot) | **6.3 s** | Runs ~1586 times / 60 s — nearly every tick, avg 4 ms, max 56 ms. Rebuilds the whole column snapshot from scratch: every tile via `GetAllTilesEnumerator` into `HashSet<(EntityUid, Vector2i)>`, plus `AddBridge` (1.76 s) allocating two `List`s per Support — and every wall/window prototype has `CEGridStabilitySupport`. |
| `ProcessPendingCollapses` → `CollapseTile` | 3.0 s | Destructible damage on anchored ents (1.1 s), `DropTileItemBelow` spawn (0.6 s), dust + sound spawns. |
| `CollectFinishedJobs` → `ApplyJobResult` | 2.7 s, **620 ms in one tick** | First result: `ScheduleCollapsingTiles` spawns dust + `PlayPvs` for every unsupported tile in a single tick. This is the biggest hitch. Also rebuilds `stabilityByGrid` / `liveByGrid` dictionaries every result. |
| `JobQueue.Process` (the flood fill itself) | 0.18 s | Cheap — not the problem. |

Side effects outside the system: Z-physics of falling items 2.6 s, physics 3.3 s, entity deletion 1.1 s, PVS 2.3 s (vs 0.5 s idle).

GC: 100–145 MB/s allocations, gen2 heap grew 522 → 809 MB in 60 s.

**Root cause of the per-tick rebuild:** `CollapseTile` → `SetTile` → `TileChangedEvent` → `OnTileChanged` → `MarkDirty` → a new full-column job on the next tick. With collapses always in flight, the column is re-snapshotted continuously.

## Client — `ApplyGameState` went from ~3% to 31% of frame, ticks up to 187 ms

| Where | Time | Why |
|---|---|---|
| `AudioSystem.OnAudioStartup` → `SetupSource` | **8.3 s (14%)** | Two `rumble.ogg` per tile (on schedule and on collapse); each is an audio entity + OpenAL source. |
| `AudioSystem.FrameUpdate` | 2.2 s | Updating all those streams every frame. |
| Entity create/delete from state | ~1.9 s | Dust, audio, dropped items. |
| Z-physics + physics of falling items | ~3.8 s | Including prediction. |

## Planned fixes (by expected impact)

1. **Debounce recompute.** Start a column job at most every 0.5–1 s per column instead of every tick. Collapse already waits 3–10 s (`CollapseDelayMin/MaxSeconds`), so the latency is invisible. Removes most of the 6.3 s and most allocations.
2. **Fewer sounds.** No sound on schedule; on collapse, one sound per area/chunk or with a cooldown, not per tile. Cuts most of the ~10.5 s client audio cost and server audio-entity spawns.
3. **Budget `ScheduleCollapsingTiles` effects per tick** (like `MaxCollapsesPerTick`) to kill the 620 ms hitch.
4. **Cheaper snapshot.** Cache bridges (Supports only change on anchor/unanchor, not on tile change); per-grid `HashSet<Vector2i>` instead of tuple keys; pre-size collections.
5. **Dropped items.** Their Z-physics + physics cost ~2.6 s server / ~3.8 s client; consider dropping only with a probability.

### Option considered: client-side effects

Server sends one batched network event per tick (`grid + scheduled tiles + collapsed tiles`, `Filter.Pvs`) instead of spawning dust/audio entities; the client spawns local dust and sounds.

- Server gain is modest: ~2–3 s / 60 s (entity spawn, PVS, deletes) — less than debounce alone.
- Client gain only comes **together with culling/capping** (only on-screen / current z-level, max N sounds per second, graphics setting). OpenAL source creation costs the same whether the sound came from the server or was spawned locally.
- Can't use client `TileChangedEvent` for this — it can't distinguish a collapse from RCD/explosions.
- The cheaper alternative with most of the client benefit: keep server spawning, but play one sound per area with a cooldown and no sound on schedule (fix 2).
