using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Nutrition;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Prototypes;
using Content.Shared._Scp.Holding;
using Content.Shared.Body;

namespace Content.Shared._Scp.Scp082;

// TODO: Анхардкод
public abstract partial class SharedScp082System : EntitySystem
{
    [Dependency] private BodySystem _body = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<Scp082Component, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<Scp082Component, BeforeIngestedEvent>(OnBeforeIngested);
        SubscribeLocalEvent<Scp082Component, ScpHoldAttemptEvent>(OnScpHoldAttempt);
    }

    private void OnGetMeleeDamage(Entity<Scp082Component> entity, ref GetMeleeDamageEvent args)
    {
        args.Damage = DamageSpecifier.ApplyModifier(args.Damage, entity.Comp.DamageModifier, 1f);
    }

    private void OnBeforeIngested(Entity<Scp082Component> entity, ref BeforeIngestedEvent args)
    {
        if (args.Solution is not { } solution)
            return;

        args.Transfer = solution.Volume;
    }

    private void OnScpHoldAttempt(Entity<Scp082Component> ent, ref ScpHoldAttemptEvent args)
    {
        if (ent.Comp.Anger >= ent.Comp.AngerHoldDisable)
            args.Cancelled = true;
    }

    protected void UpdateDigestibility(Entity<Scp082Component> ent)
    {
        if (!_body.TryGetOrganWithComponent<StomachComponent>(ent.Owner, out var stomachs))
            return;

        EntityWhitelist whitelist;
        if (ent.Comp.Hunger < ent.Comp.HumanoidFoodHungerThreshold)
        {
            var tags = new List<ProtoId<TagPrototype>>();
            tags.Add("Meat");
            whitelist = new EntityWhitelist { Tags = tags };
        }
        else
        {
            whitelist = new EntityWhitelist { Components = new[] { "Edible" } };
        }

        stomachs.Comp.SpecialDigestible = whitelist;
    }
}
