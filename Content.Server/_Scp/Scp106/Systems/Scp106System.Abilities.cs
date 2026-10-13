using Content.Server.Hands.Systems;
using Content.Shared._Scp.Scp106;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.Coordinates;
using Content.Shared.Hands.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Scp.Scp106.Systems;

public sealed partial class Scp106System
{
    [Dependency] private HandsSystem _hands = default!;

    private void InitializeAbilities()
    {
        SubscribeLocalEvent<Scp106Component, EntParentChangedMessage>(OnParentChanged);
    }

    private void OnParentChanged(Entity<Scp106Component> ent, ref EntParentChangedMessage args)
    {
        if (!HasComp<Scp106BackRoomMapComponent>(args.OldMapId))
            return;

        HideBlade(ent);
    }

    public override bool PhantomTeleport(Scp106BecomeTeleportPhantomActionEvent args)
    {
        if (args.Args.EventTarget is not {} phantom)
            return false;

        if (!TryComp<Scp106PhantomComponent>(phantom, out var phantomComponent))
            return false;

        if (!_mind.TryGetMind(phantom, out var mindId, out _))
            return false;

        var scp106 = phantomComponent.Scp106BodyUid;

        if (!Exists(scp106))
            return false;

        if (!TryComp<Scp106Component>(scp106, out var scp106Component))
            return false;

        _mind.TransferTo(mindId, scp106);

        var phantomPos = Transform(phantom).Coordinates;

        _transform.SetCoordinates(scp106.Value, phantomPos);

        Del(phantom);

        Scp106FinishTeleportation(scp106.Value, scp106Component.TeleportationDuration);

        return true;
    }

    protected override void ToggleBlade(Entity<Scp106Component> ent, EntProtoId bladeId)
    {
        base.ToggleBlade(ent, bladeId);

        // Если клинок уже имеется
        if (Exists(ent.Comp.BladeEntity))
        {
            HideBlade(ent);
        }
        else
        {
            EnsureComp<HandsComponent>(ent);
            _hands.AddHand(ent.Owner, "right", HandLocation.Middle);
            var blade = Spawn(bladeId, ent.Owner.ToCoordinates());
            ent.Comp.BladeEntity = blade;

            if (!_hands.TryPickup(ent, blade, "right"))
                HideBlade(ent);
        }
    }

    public void HideBlade(Entity<Scp106Component> ent)
    {
        if (!Exists(ent.Comp.BladeEntity))
            return;

        Del(ent.Comp.BladeEntity);
        ent.Comp.BladeEntity = null;

        _hands.RemoveHands(ent.Owner);
    }
}
