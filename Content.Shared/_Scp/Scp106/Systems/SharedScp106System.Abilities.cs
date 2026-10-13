using System.Linq;
using Content.Shared._RMC14.Xenonids.Screech;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Fear.Systems;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.Coordinates;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Scp106.Systems;

public abstract partial class SharedScp106System
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedFearSystem _fear = default!;

    private const float TerrifyRange = 23f;

    private void InitializeAbilities()
    {
        SubscribeLocalEvent<Scp106Component, Scp106BackroomsAction>(OnBackroomsAction);
        SubscribeLocalEvent<Scp106Component, Scp106RandomTeleportAction>(OnRandomTeleportAction);
        SubscribeLocalEvent<Scp106Component, Scp106BecomePhantomAction>(OnScp106BecomePhantomAction);
        SubscribeLocalEvent<Scp106Component, Scp106BecomeTeleportPhantomAction>(OnBecomeTeleportPhantomAction);
        SubscribeLocalEvent<Scp106Component, Scp106TerrifyNearbyActionEvent>(OnTerrify);
        SubscribeLocalEvent<Scp106Component, Scp106BareBladeAction>(OnScp106BareBladeAction);

        SubscribeLocalEvent<Scp106Component, Scp106BackroomsActionEvent>(OnBackroomsDoAfter);
        SubscribeLocalEvent<Scp106Component, Scp106RandomTeleportActionEvent>(OnTeleportDoAfter);
    }

    private void OnBackroomsAction(Entity<Scp106Component> ent, ref Scp106BackroomsAction args)
    {
        if (IsInDimension(ent))
        {
            var message = Loc.GetString("scp106-already-in-dimension");
            _popup.PopupEntity(message, ent, ent, PopupType.SmallCaution);
            return;
        }

        TryDoTeleport(ent, ref args, new Scp106BackroomsActionEvent ());
    }

    private void OnRandomTeleportAction(Entity<Scp106Component> ent, ref Scp106RandomTeleportAction args)
    {
        TryDoTeleport(ent, ref args, new Scp106RandomTeleportActionEvent ());
    }

    private void OnScp106BecomePhantomAction(Entity<Scp106Component> ent, ref Scp106BecomePhantomAction args)
    {
        if (args.Handled)
            return;

        if (!TryDeductEssence(ent, args.Cost))
            return;

        BecomePhantom(ent, ref args);
    }

    private void OnBecomeTeleportPhantomAction(Entity<Scp106Component> ent, ref Scp106BecomeTeleportPhantomAction args)
    {
        if (IsContained(ent))
            return;

        if (!TryDeductEssence(ent, args.Cost))
            return;

        BecomeTeleportPhantom(ent, ref args);
    }

    private void OnBackroomsDoAfter(Entity<Scp106Component> ent, ref Scp106BackroomsActionEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        DoTeleportEffects(ent);
        _ = SendToBackrooms(ent);

        args.Handled = true;
    }

    private void OnTeleportDoAfter(Entity<Scp106Component> ent, ref Scp106RandomTeleportActionEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        DoTeleportEffects(ent);
        SendToStation(ent);

        args.Handled = true;
    }

    private void OnTerrify(Entity<Scp106Component> ent, ref Scp106TerrifyNearbyActionEvent args)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (args.Handled)
            return;

        if (IsContained(ent))
            return;

        if (ent.Comp.AbsorbedFears.Count == 0)
            return;

        if (!TryDeductEssence(ent, args.Cost))
            return;

        var nearby = _lookup.GetEntitiesInRange<FearComponent>(Transform(ent).Coordinates, TerrifyRange);
        var state = ent.Comp.AbsorbedFears.Max();

        foreach (var target in nearby)
        {
            _fear.TrySetFearLevel(target.AsNullable(), state);
        }

        ent.Comp.AbsorbedFears.Remove(state);
        Dirty(ent);
        StartScreech(ent.Owner, false);

        args.Handled = true;
    }

    private void OnScp106BareBladeAction(Entity<Scp106Component> ent, ref Scp106BareBladeAction args)
    {
        if (args.Handled)
            return;

        if (!TryToggleBlade(ent, args.Prototype))
            return;

        _actions.SetToggled(args.Action.AsNullable(), !args.Action.Comp.Toggled);
        args.Handled = true;
    }

    public bool TryToggleBlade(Entity<Scp106Component> ent, EntProtoId proto, bool force = false)
    {
        // Клинок можно использовать только в карманном измерении или форсированно через код
        if (!IsInDimension(ent) && !force)
        {
            var message = Loc.GetString("scp106-not-enough-power");
            _popup.PopupClient(message, ent, ent);

            return false;
        }

        ToggleBlade(ent, proto);
        return true;
    }

    private void StartScreech(Entity<XenoScreechComponent?> ent, bool playSound = true)
    {
        if (!Resolve(ent, ref ent.Comp))
            return;

        if (playSound)
            _audio.PlayPvs(ent.Comp.Sound, ent);

        SpawnAttachedTo(ent.Comp.Effect, ent.Owner.ToCoordinates());
    }

    protected virtual void ToggleBlade(Entity<Scp106Component> ent, EntProtoId blade) { }
}
