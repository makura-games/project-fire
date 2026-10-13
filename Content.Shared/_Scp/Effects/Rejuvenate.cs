using Content.Shared.Administration.Systems;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Components;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Effects;

public sealed partial class RejuvenateEntityEffectSystem : EntityEffectSystem<MobStateComponent, Rejuvenate>
{
    [Dependency] private RejuvenateSystem _rejuvenate = default!;

    protected override void Effect(Entity<MobStateComponent> entity, ref EntityEffectEvent<Rejuvenate> args)
    {
        _rejuvenate.PerformRejuvenate(entity);
    }
}

/// <inheritdoc cref="EntityEffectBase"/>
[UsedImplicitly]
public sealed partial class Rejuvenate : EntityEffectBase<Rejuvenate>
{
    public override string EntityEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) =>
        Loc.GetString("reagent-effect-guidebook-scp500");
}

