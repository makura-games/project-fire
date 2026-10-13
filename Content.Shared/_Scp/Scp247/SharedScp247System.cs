using Content.Shared.Humanoid;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Shared._Scp.Scp247;

public abstract partial class SharedScp247System : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    private EntityQuery<HumanoidProfileComponent> _humanoid;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp247Component, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<Scp247Component, GettingAttackedAttemptEvent>(OnEntGettingAttackedAttempt);

        _humanoid = GetEntityQuery<HumanoidProfileComponent>();
    }

    private void OnMeleeHit(Entity<Scp247Component> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        foreach (var target in args.HitEntities)
        {
            if (target == ent.Owner)
                continue;

            if (!_humanoid.HasComp(target))
                return;

            ent.Comp.AllowedAttackers.Add(target);
        }
    }

    private void OnEntGettingAttackedAttempt(Entity<Scp247Component> ent, ref GettingAttackedAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (HasComp<Scp247ProtectionComponent>(args.Attacker))
            return;

        if (ent.Comp.AllowedAttackers.Contains(args.Attacker))
            return;

        _popup.PopupPredicted(Loc.GetString("scp247-cannot-harm"), args.Attacker, null, PopupType.Medium);
        args.Cancelled = true;
    }
}
