using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._Scp.LightFlicking;

public abstract partial class SharedLightFlickingSystem : EntitySystem
{
    [Dependency] protected IRobustRandom Random = default!;
    [Dependency] protected IGameTiming Timing = default!;

    private readonly TimeSpan _flickInterval = TimeSpan.FromSeconds(0.5);
    private readonly TimeSpan _flickVariation = TimeSpan.FromSeconds(0.45);

    protected void SetNextFlickingTime(Entity<ActiveLightFlickingComponent> ent)
    {
        var additionalTime = _flickInterval - Random.Next(_flickVariation);
        ent.Comp.NextFlickTime = Timing.CurTime + additionalTime;

        Dirty(ent);
    }
}
