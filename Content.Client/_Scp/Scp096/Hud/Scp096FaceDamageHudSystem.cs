using Content.Client.Overlays;
using Content.Shared._Scp.Scp096.Hud;
using Content.Shared._Scp.Scp096.Main.Components;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Scp.Scp096.Hud;

public sealed partial class Scp096FaceDamageHudSystem : EquipmentHudSystem<ShowScp096FaceDamageHudComponent>
{
    [Dependency] private Scp096System _scp096 = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp096Component, GetStatusIconsEvent>(OnGetStatusIcon);
    }

    private void OnGetStatusIcon(Entity<Scp096Component> ent, ref GetStatusIconsEvent args)
    {
        if (!IsActive)
            return;

        if (!_scp096.TryGetFaceDamageLevel(ent, 0, ShowScp096FaceDamageHudComponent.Icons.Count - 1, out var severity))
            return;

        if (!ShowScp096FaceDamageHudComponent.Icons.TryGetValue(severity, out var iconProto))
            return;

        if (!_prototype.TryIndex(iconProto, out var icon))
            return;

        args.StatusIcons.Add(icon);
    }
}
