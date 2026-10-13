using Content.Server.Defusable.WireActions;
using Content.Server.Doors.Systems;
using Content.Server.Power;
using Content.Server.Wires;
using Content.Shared.Doors.Components;
using Content.Shared.Wires;
using Robust.Server.Audio;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;

namespace Content.Server._Scp.Other.BreakDoorOnCollide;

public sealed partial class BreakDoorOnCollideSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WiresSystem _wires = default!;
    [Dependency] private DoorSystem _door = default!;
    [Dependency] private AudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BreakDoorOnCollideComponent, StartCollideEvent>(OnCollide);
    }

    private void OnCollide(Entity<BreakDoorOnCollideComponent> ent, ref StartCollideEvent args)
    {
        if (!ent.Comp.Enabled)
            return;

        if (!TryComp<DoorComponent>(args.OtherEntity, out var doorComponent))
            return;

        _door.StartOpening(args.OtherEntity, doorComponent);

        if (TryComp<DoorBoltComponent>(args.OtherEntity, out var doorBoltComponent))
            _door.SetBoltsDown((args.OtherEntity, doorBoltComponent), true);

        if (!TryComp<WiresComponent>(args.OtherEntity, out var wiresComponent))
            return;

        if (TryComp<WiresPanelComponent>(args.OtherEntity, out var wiresPanelComponent))
            _wires.TogglePanel(args.OtherEntity, wiresPanelComponent, true);

        foreach (var x in wiresComponent.WiresList)
        {
            if (x.Action is PowerWireAction or BoltWireAction) // Always cut this wires
                x.Action?.Cut(ent, x);
            else if (_random.Prob(ent.Comp.WireCutChance)) // randomly cut other wires
                x.Action?.Cut(ent, x);
        }

        _audio.PlayPvs(ent.Comp.DoorSmashSoundCollection, args.OtherEntity);
    }
}
