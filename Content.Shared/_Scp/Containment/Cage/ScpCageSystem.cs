using System.Linq;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Proximity;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Whitelist;

namespace Content.Shared._Scp.Containment.Cage;

public sealed partial class ScpCageSystem : EntitySystem
{
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private ScpHelpers _helpers = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpCageComponent, StorageOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<ScpCageComponent, StorageCloseAttemptEvent>(OnCloseAttempt);
    }

    private void OnOpenAttempt(Entity<ScpCageComponent> ent, ref StorageOpenAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!IsRestricted(ent, ent.Comp.OpenStorageBlacklist))
            return;

        if (!string.IsNullOrEmpty(ent.Comp.Reason))
            _popup.PopupPredicted(Loc.GetString(ent.Comp.Reason), ent, args.User);

        args.Cancelled = true;
    }

    private void OnCloseAttempt(Entity<ScpCageComponent> ent, ref StorageCloseAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!IsRestricted(ent, ent.Comp.CloseStorageBlacklist))
            return;

        if (!string.IsNullOrEmpty(ent.Comp.Reason))
            _popup.PopupPredicted(Loc.GetString(ent.Comp.Reason), ent, args.User);

        args.Cancelled = true;
    }

    private bool IsRestricted(Entity<ScpCageComponent> ent, EntityWhitelist? blacklist)
    {
        if (blacklist == null)
            return false;

        return _lookup.GetEntitiesInRange(ent, ent.Comp.SearchRadius)
            .Any(uid => IsBadEntity(ent, blacklist, uid));
    }

    private bool IsBadEntity(Entity<ScpCageComponent> ent, EntityWhitelist? blacklist, EntityUid uid)
    {
        var passesWhitelist = _whitelist.IsWhitelistPass(blacklist, uid);
        if (!passesWhitelist)
            return false;

        var correctType = _proximity.IsRightType(ent, uid, ent.Comp.LineOfSight, out _);
        if (!correctType)
            return false;

        var reagentOk = ent.Comp.StopReagent == null
                        || _helpers.IsAroundSolutionVolumeGreaterThan(
                            ent,
                            ent.Comp.StopReagent.Value,
                            ent.Comp.StopReagentVolume,
                            LineOfSightBlockerLevel.None);

        if (!reagentOk)
            return false;

        return true;
    }
}
