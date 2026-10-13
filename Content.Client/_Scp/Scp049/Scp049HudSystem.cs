using Content.Client.Overlays;
using Content.Shared._Scp.Scp049;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Scp.Scp049;

public sealed partial class Scp049HudSystem : EquipmentHudSystem<ScpShow049HudComponent>
{
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp049MinionComponent, GetStatusIconsEvent>(OnGetMinionIcon);
        SubscribeLocalEvent<Scp049Component, GetStatusIconsEvent>(OnGetScpIcon);
    }

    private void OnGetScpIcon(Entity<Scp049Component> ent, ref GetStatusIconsEvent args)
    {
        if (!IsActive)
            return;

        var prototype = _prototype.Index(Scp049Component.Icon);
        args.StatusIcons.Add(prototype);
    }

    private void OnGetMinionIcon(Entity<Scp049MinionComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!IsActive)
            return;

        var prototype = _prototype.Index(Scp049MinionComponent.Icon);
        args.StatusIcons.Add(prototype);
    }
}
