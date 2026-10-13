using Content.Server._Scp.Blood;
using Content.Shared._Scp.Blood;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Misc.LiquidParticlesGenerator;

public sealed partial class LiquidParticlesGeneratorSystem : EntitySystem
{
    [Dependency] private BloodSplatterSystem _bloodSplatter = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LiquidParticlesGeneratorComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<LiquidParticlesGeneratorComponent> ent, ref MapInitEvent args)
    {
        if (!ent.Comp.EnableOnMapInit)
            return;

        ent.Comp.Enabled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LiquidParticlesGeneratorComponent, BloodSplattererComponent>();
        while (query.MoveNext(out var uid, out var liquid, out var bloodSplatterer))
        {
            // TODO: Заменить на ActiveComponent логику, если потребуется оптимизация
            if (!liquid.Enabled)
                continue;

            if (_timing.CurTime < liquid.NextSpawn)
                continue;

            SpawnParticle((uid, bloodSplatterer, liquid));

            var variation = _random.Next(liquid.CooldownVariation);
            liquid.NextSpawn = _timing.CurTime + liquid.Cooldown + variation;
        }
    }

    private void SpawnParticle(Entity<BloodSplattererComponent, LiquidParticlesGeneratorComponent> target)
    {
        var parent = Transform(target).GridUid;
        var angle = Angle.Zero;

        // Чтобы направление правильно просчитывалось, нужно прибавлять поворот грида
        // Иначе каждый раунд все будет летать в разном направлении из-за случайного поворота грида относительно мира
        if (parent.HasValue)
            angle = _transform.GetWorldPositionRotation(parent.Value).WorldRotation;

        _bloodSplatter.SpawnBloodParticles(target, target, Angle.FromDegrees(target.Comp2.Angle) + angle, target.Comp2.Radians);
    }
}
