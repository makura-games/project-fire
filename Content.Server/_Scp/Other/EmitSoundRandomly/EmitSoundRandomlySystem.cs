using Robust.Server.Audio;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Other.EmitSoundRandomly;

public sealed partial class EmitSoundRandomlySystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmitSoundRandomlyComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<EmitSoundRandomlyComponent> ent, ref ComponentStartup args)
    {
        SetNextSoundTime(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<EmitSoundRandomlyComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            if (_timing.CurTime < component.NextSoundTime)
                continue;

            var ev = new BeforeRandomlyEmittingSoundEvent();
            RaiseLocalEvent(uid, ref ev);

            if (!ev.Cancelled)
                _audio.PlayPvs(component.Sound, uid);

            SetNextSoundTime((uid, component));
        }
    }

    private void SetNextSoundTime(Entity<EmitSoundRandomlyComponent> ent)
    {
        var variance = _random.Next(TimeSpan.Zero, ent.Comp.CooldownVariation);
        var cooldown = ent.Comp.SoundCooldown + variance;

        ent.Comp.NextSoundTime = _timing.CurTime + cooldown;
    }
}


[ByRefEvent]
public record struct BeforeRandomlyEmittingSoundEvent()
{
    public bool Cancelled = false;
}
