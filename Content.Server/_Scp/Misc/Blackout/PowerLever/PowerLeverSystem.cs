using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Scp.Other.Blackout;
using Content.Shared._Scp.Other.Blackout.PowerLever;
using Content.Shared.Interaction;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._Scp.Misc.Blackout.PowerLever;

public sealed partial class PowerLeverSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private ApcSystem _apc = default!;

    private readonly SoundSpecifier _powerRestoredSound =
        new SoundPathSpecifier("/Audio/_Scp/Effects/Blackout/after_lever_toggled.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PowerLeverComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PowerLeverComponent, InteractHandEvent>(OnHandInteract);
    }

    private void OnInit(Entity<PowerLeverComponent> ent, ref ComponentInit args)
    {
        _appearance.SetData(ent, PowerLevelVisualLayers.Toggled, ent.Comp.Toggled);
    }

    private void OnHandInteract(Entity<PowerLeverComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        ent.Comp.Toggled = !ent.Comp.Toggled;
        Dirty(ent);

        _audio.PlayPvs(ent.Comp.ToggleSound, ent);
        _appearance.SetData(ent, PowerLevelVisualLayers.Toggled, ent.Comp.Toggled);

        if (ent.Comp.Toggled)
            OnToggled(ent);

        args.Handled = true;
    }

    private void OnToggled(Entity<PowerLeverComponent> ent)
    {
        var query = AllEntityQuery<ApcComponent, MalfunctionApcComponent>();
        var count = 0;

        while (query.MoveNext(out var uid, out var apcComponent, out _))
        {
            RemComp<MalfunctionApcComponent>(uid);
            _apc.ApcToggleBreaker(uid, apcComponent);
            count++;
        }

        if (count != 0)
        {
            var gridUid = Transform(ent).GridUid;

            if (!gridUid.HasValue)
                return;

            _audio.PlayGlobal(_powerRestoredSound, Filter.BroadcastGrid(gridUid.Value), true);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString("lever-toggled-nothing-happened"), ent);
        }
    }
}
