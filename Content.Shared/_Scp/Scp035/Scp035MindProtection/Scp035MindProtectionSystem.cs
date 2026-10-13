using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Shared._Scp.Scp035.Scp035MindProtection;

public sealed partial class Scp035MindProtectionSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp035MaskComponent, ContainerGettingInsertedAttemptEvent>(OnEquipAttempt);
    }

    /// <summary>
    /// Не дает защищенному игроку взять/надеть SCP-035
    /// И выдает попап о невозможности
    /// </summary>
    /// <param name="scp">SCP-035</param>
    /// <param name="args">Ивент</param>
    private void OnEquipAttempt(Entity<Scp035MaskComponent> scp, ref ContainerGettingInsertedAttemptEvent args)
    {
        var owner = args.Container.Owner;

        if (!HasComp<Scp035MindProtectionComponent>(owner))
            return;

        var message = Loc.GetString("scp035-protection-success", ("name", MetaData(scp).EntityName));
        _popup.PopupCursor(message, owner);

        args.Cancel();
    }
}
