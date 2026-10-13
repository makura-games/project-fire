using Content.Shared.IdentityManagement;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Shared._Scp.Scp049;

public abstract partial class SharedScp049System : EntitySystem
{
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<Scp049Component, Scp049KillLivingBeingAction>(OnKillLivingBeing);
    }

    private void OnKillLivingBeing(Entity<Scp049Component> ent, ref Scp049KillLivingBeingAction args)
    {
        if (args.Handled)
            return;

        if (_mob.IsDead(args.Target))
        {
            _popup.PopupClient(Loc.GetString("scp049-kill-action-already-dead"), args.Target, ent, PopupType.MediumCaution);
            return;
        }

        if (!_mob.HasState(args.Target, MobState.Dead))
        {
            _popup.PopupClient(Loc.GetString("scp049-kill-action-cant-kill"), args.Target, ent, PopupType.MediumCaution);
            return;
        }

        _mob.ChangeMobState(args.Target, MobState.Dead);

        var targetName = Identity.Name(args.Target, EntityManager);
        var performerName = Identity.Name(ent, EntityManager);

        var localeMessage = Loc.GetString("scp049-touch-action-success",
            ("target", targetName),
            ("performer", performerName));

        _popup.PopupPredicted(localeMessage, args.Target, ent, PopupType.MediumCaution);

        args.Handled = true;
    }
}
