using Content.Shared._Scp.Scp106.Components;
using Content.Shared.Actions;
using Content.Shared.Rejuvenate;

namespace Content.Shared._Scp.Scp106.Systems;

public abstract partial class SharedScp106System
{
    [Dependency] private SharedActionsSystem _actions = default!;

    private void InitializeStore()
    {
        // Abilities in that store - I love lambdas >:)

        // TODO: Проверка на хендхелд и кенселед
        SubscribeLocalEvent((Entity<Scp106Component> ent, ref Scp106BoughtBareBladeAction args) =>
            _actions.AddAction(ent, args.BoughtAction));
        SubscribeLocalEvent((Entity<Scp106Component> ent, ref Scp106BoughtCreatePortal args) =>
            _actions.AddAction(ent, args.BoughtAction));
        SubscribeLocalEvent((Entity<Scp106Component> ent, ref Scp106BoughtTerrify args) =>
            _actions.AddAction(ent, args.BoughtAction));
        SubscribeLocalEvent((Entity<Scp106Component> ent, ref Scp106BoughtPuddle args) =>
            _actions.AddAction(ent, args.BoughtAction));

        SubscribeLocalEvent<Scp106Component, Scp106OnUpgradePhantomAction>(OnUpgradePhantomAction);

#if DEBUG || TOOLS

        SubscribeLocalEvent<Scp106Component, RejuvenateEvent>(OnRejuvenate);

#endif

    }

    private void OnUpgradePhantomAction(Entity<Scp106Component> ent, ref Scp106OnUpgradePhantomAction args)
    {
        ent.Comp.PhantomCoolDown -= args.CooldownReduce;
        Dirty(ent);
    }

#if DEBUG || TOOLS

    private void OnRejuvenate(Entity<Scp106Component> ent, ref RejuvenateEvent args)
    {
        ent.Comp.Essence += 100;
        Dirty(ent);
    }

#endif
}
