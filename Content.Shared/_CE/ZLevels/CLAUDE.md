# CrystallEdge ZLevels — Architecture

## Concept
Fake 3D via a vertical stack of **Map entities**. Each z-level = a full RobustToolbox `Map`.
CE z-levels ship as **planet maps**: the map entity also has `MapGridComponent`, so
map entity == grid entity. Levels are ordered by integer `Depth`: 0 = station main map,
negative = below, positive = above. Client rendering stacks eyes/sprites with a fixed
per-level screen offset to fake height.

**zGrid is a different, unrelated subsystem** (`CEZGrid*Component`): grid-on-grid vertical
stacking (e.g. shuttle parked above a grid, linked by connector walls) inside the
physics/roof system. NOT the map z-network. Do not confuse the two.

## Core data — Content.Shared/_CE/ZLevels/Core/Components
- `CEZMapComponent` (each z-map, networked): `int Depth`, `EntityUid NetworkUid`,
  `EntityUid? MapAbove`, `EntityUid? MapBelow`.
- `CEZMapNetworkComponent` (nullspace manager, networked, PVS-forced to every client):
  `Dictionary<int,EntityUid?> ZLevels`, `Dictionary<EntityUid,int> ZLevelByEntity`,
  `List<EntityUid> SortedZLevels` (dense; index 0 == `SortedMin`; may hold
  `EntityUid.Invalid` gaps), `int SortedMin`, `int SortedMax`.
  Map at depth d = `SortedZLevels[d - SortedMin]`.
- `CEStationZLevelsComponent` (server, on station): `EntityUid? ZNetworkEntity`,
  `List<ResPath> MapsBelow`, `List<ResPath> MapsAbove`, `ComponentRegistry ZLevelsComponentOverrides`.
- `CEZLevelViewerComponent` (players, networked): `bool LookUp` (binary "peek 1+ levels up",
  toggled by action `CEActionToggleLookUp`), `HashSet<EntityUid> Eyes`.
  There is NO arbitrary "selected level" — a viewer's active level is always its transform's map.

## Systems
- `CESharedZLevelsSystem` — partial: `.Maps .Grids .View .Update .Movement .Activation
  .Cache .Constants`. Traversal + query API.
- Server `CEZLevelsSystem` (`.cs .Maps .Grids .View`): builds the network on
  `StationPostInitEvent`, loads MapsBelow/Above, calls `AddGridToStation` for each planet
  z-level, spawns PVS eyes per viewer.
- Client `CEClientZLevelsSystem : CESharedZLevelsSystem`: only overrides eye-offset /
  draw-depth visuals. No client-side registry — discovery is purely via the networked components.

## Key API (CESharedZLevelsSystem.Maps.cs / .Grids.cs)
- `TryGetMapNetwork(mapUid, out Entity<CEZMapNetworkComponent>)`
- `TryMapOffset(ent, offset, out ...)`, `TryMapUp`, `TryMapDown`
- `GetAllMapsAbove(Entity<CEZMapComponent>)`, `GetAllMapsBelow(...)` — nearest-first
- `TryGetZLevelOffset(mapA, mapB, out int)` — O(1) depth diff
- `TryGetMapAtDepth(network, depth, out EntityUid)` — server only
- `TryGetGridZDepth(gridUid)`, `GridTileToWorldTile(gridUid, grid, tile)`
- `GetVisibleZLevelsAbove(ent)`, `HasOpaqueAbove(worldPos, map)` — `.View.cs`

## Constants — CESharedZLevelsSystem.Constants.cs
`ZLevelOffset = 0.7f` (screen/world offset per level), `MaxZLevelsBelowRendering = 5`,
`MaxZLevelsAboveRendering = 3`, plus z-gravity/velocity tuning values.

## Client rendering
`Content.Client/_CE/ZLevels/Core/ScalingViewport.CEZLevels.cs` — renders levels
`lowestDepth .. lookUp`; per depth builds a `ZEye` with
`eye.Offset += rotation.ToWorldVec() * ZLevelOffset * depth`.
`Content.Client/_CE/ZLevels/Core/Overlays/CEZLevelBlurOverlay.cs` — blurs/tints levels
with `Depth < 0` (dims lower levels).

## NavMap on z-levels
Every planet z-level map is added to the station, so each gets a `NavMapComponent`
(server `Content.Server/Pinpointer/NavMapSystem.cs:61`, on `StationGridAddedEvent`).
To read a level's schematic: `TryComp<NavMapComponent>(zMapUid, out var nav)` then walk
`nav.Chunks` / `chunk.TileData` with `SharedNavMapSystem` masks
(`FloorMask` / `WallMask` / `AirlockMask`) — same as `Content.Client/Pinpointer/UI/NavMapControl.cs`.

## Events
- `CEZLevelMapNetworkUpdatedEvent` (class, on network entity) — maps added/removed.
- `CEMapAddedIntoZNetworkEvent` (ByRefEvent struct `{ Network, Depth }`, on the map).
- zGrid: `CEGridAddedIntoZNetworkEvent`, `CEGridRemovedFromZNetworkEvent`,
  `CEZLevelGridNetworkUpdatedEvent`.

## Example configs
`Resources/Prototypes/_CE/Maps/shaar.yml`, `debug.yml` — `[Prototype("zMap")]`
`CEZLevelMapPrototype` (`List<ResPath> Maps`, `ComponentRegistry Components`);
mapping/admin tooling only, not a runtime registry.
Planet-map proof: `Resources/Maps/_CE/Dev/Dev1.yml:33-43`.