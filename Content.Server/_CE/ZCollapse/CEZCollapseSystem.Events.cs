/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.Analyzers;
namespace Content.Server._CE.ZCollapse;

// Every handler here does exactly two things: keep CEGridStabilityComponent.Cores/Supports in sync
// with what's actually anchored, and MarkDirty() whatever grid(s) that could affect. No computation
// happens here — see CEZCollapseSystem.cs for the actual recompute pipeline.
public sealed partial class CEZCollapseSystem
{
    private void InitializeEventsSubscriptions()
    {
        SubscribeLocalEvent<CEGridStabilityCoreComponent, AnchorStateChangedEvent>(OnCoreAnchorChanged);
        SubscribeLocalEvent<CEGridStabilityCoreComponent, ReAnchorEvent>(OnCoreReAnchor);
        SubscribeLocalEvent<CEGridStabilitySupportComponent, AnchorStateChangedEvent>(OnSupportAnchorChanged);
        SubscribeLocalEvent<CEGridStabilitySupportComponent, ReAnchorEvent>(OnSupportReAnchor);
        SubscribeLocalEvent<CEGridStabilityComponent, TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<CEGridStabilityComponent, MapInitEvent>(OnStabilityMapInit);
        SubscribeLocalEvent<CEGridStabilityComponent, GridSplitEvent>(OnGridSplit);
    }


    // Entities anchored from map/prototype data never raise AnchorStateChangedEvent (they start
    // already-anchored) — that's what the deferred MapInit index scan is for, not this handler.
    // Deleting an anchored entity does go through here: entity termination detaches it first, which
    // raises AnchorStateChangedEvent(Anchored: false) before the entity is actually gone.
    private void OnCoreAnchorChanged(Entity<CEGridStabilityCoreComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Transform.GridUid is not { } gridUid || !_stabilityQuery.TryGetComponent(gridUid, out var comp))
            return;

        if (args.Anchored)
            comp.Cores.Add(ent.Owner);
        else
            comp.Cores.Remove(ent.Owner);

        MarkDirty(gridUid);
    }

    private void OnCoreReAnchor(Entity<CEGridStabilityCoreComponent> ent, ref ReAnchorEvent args)
    {
        if (_stabilityQuery.TryGetComponent(args.OldGrid, out var oldComp))
        {
            oldComp.Cores.Remove(ent.Owner);
            MarkDirty(args.OldGrid);
        }

        if (_stabilityQuery.TryGetComponent(args.Grid, out var newComp))
        {
            newComp.Cores.Add(ent.Owner);
            MarkDirty(args.Grid);
        }
    }

    private void OnSupportAnchorChanged(Entity<CEGridStabilitySupportComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Transform.GridUid is not { } gridUid || !_stabilityQuery.TryGetComponent(gridUid, out var comp))
            return;

        if (args.Anchored)
            comp.Supports.Add(ent.Owner);
        else
            comp.Supports.Remove(ent.Owner);

        // Marking just this grid is enough — GetColumn() pulls in every Z-adjacent participating grid
        // (including whichever one this Support bridges to) the moment a job actually starts for it.
        MarkDirty(gridUid);
    }

    private void OnSupportReAnchor(Entity<CEGridStabilitySupportComponent> ent, ref ReAnchorEvent args)
    {
        if (_stabilityQuery.TryGetComponent(args.OldGrid, out var oldComp))
        {
            oldComp.Supports.Remove(ent.Owner);
            MarkDirty(args.OldGrid);
        }

        if (_stabilityQuery.TryGetComponent(args.Grid, out var newComp))
        {
            newComp.Supports.Add(ent.Owner);
            MarkDirty(args.Grid);
        }
    }

    // Externally-caused tile add/remove (RCD, explosions, etc). No special-cased math for "inherit
    // from neighbor" or "reap immediately if unsupported" — the next full recompute derives both
    // correctly from ground truth on its own.
    private void OnTileChanged(Entity<CEGridStabilityComponent> ent, ref TileChangedEvent args)
    {
        MarkDirty(ent.Owner);
    }

    private void OnStabilityMapInit(Entity<CEGridStabilityComponent> ent, ref MapInitEvent args)
    {
        _pendingIndexScan.Add(ent.Owner);
    }

    // Reparented entities during a grid split may not raise ReAnchorEvent either — deferring both the
    // old and new grid(s) into the same index-scan pass as MapInit self-corrects regardless of what
    // events did or didn't fire during the split.
    private void OnGridSplit(Entity<CEGridStabilityComponent> ent, ref GridSplitEvent args)
    {
        _pendingIndexScan.Add(ent.Owner);

        foreach (var newGrid in args.NewGrids)
        {
            EnsureComp<CEGridStabilityComponent>(newGrid);
            _pendingIndexScan.Add(newGrid);
        }
    }
}
