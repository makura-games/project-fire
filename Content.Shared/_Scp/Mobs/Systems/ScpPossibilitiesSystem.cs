using Content.Shared._Scp.Mobs.Components;
using Content.Shared._Sunrise.Random;
using Content.Shared.Mech;
using Content.Shared.Mech.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Whitelist;

namespace Content.Shared._Scp.Mobs.Systems;

public sealed partial class ScpPossibilitiesSystem : EntitySystem
{
    [Dependency] private RandomPredictedSystem _random = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpPossibilitiesComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnMeleeHit(Entity<ScpPossibilitiesComponent> ent, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count == 0 || !args.IsHit)
            return;

        if (ent.Comp.CanEjectPilotFromMech)
            TryEjectFromMech(ref args);

        if (ent.Comp.OpenContainer)
            TryOpenContainer(ent, ref args);
    }

    public void TryEjectFromMech(ref MeleeHitEvent args)
    {
        foreach (var target in args.HitEntities)
        {
            if (!TryComp<MechComponent>(target, out var mechComp))
                continue;

            if (mechComp.PilotSlot.ContainedEntity == null)
                continue;

            var ev = new MechEjectPilotEvent();
            RaiseLocalEvent(target, ev);
        }
    }

    public void TryOpenContainer(Entity<ScpPossibilitiesComponent> ent, ref MeleeHitEvent args)
    {
        foreach (var target in args.HitEntities)
        {
            if (!HasComp<EntityStorageComponent>(target))
                continue;

            if (!_whitelist.CheckBoth(target, ent.Comp.OpenContainerBlacklist, ent.Comp.OpenContainerWhitelist))
                continue;

            if (_random.ProbForEntity(ent, ent.Comp.OpenContainerChance) &&
                _entityStorage.TryOpenStorage(ent, target, false, false))
                continue;

            _popup.PopupPredicted(Loc.GetString("scp-possibilities-open-container-failed"), target, ent, PopupType.Medium);
        }
    }
}
