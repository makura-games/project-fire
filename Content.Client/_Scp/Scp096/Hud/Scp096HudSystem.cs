using System.Diagnostics.CodeAnalysis;
using Content.Client.Overlays;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared.StatusIcon.Components;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Scp.Scp096.Hud;

public sealed partial class Scp096HudSystem : EquipmentHudSystem<Scp096Component>
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    private EntityQuery<Scp096Component> _scp096Query;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp096TargetComponent, GetStatusIconsEvent>(OnGetStatusIcon);
        _scp096Query = GetEntityQuery<Scp096Component>();
    }

    private void OnGetStatusIcon(Entity<Scp096TargetComponent> ent, ref GetStatusIconsEvent args)
    {
        var playerEntity = _player.LocalEntity;

        if (!Validate(playerEntity))
            return;

        if (!_prototypeManager.TryIndex(ent.Comp.KillIconPrototype, out var killIconPrototype))
            return;

        args.StatusIcons.Add(killIconPrototype);
    }

    private bool Validate([NotNullWhen(true)] EntityUid? player)
    {
        return IsActive && _scp096Query.HasComp(player);
    }
}
