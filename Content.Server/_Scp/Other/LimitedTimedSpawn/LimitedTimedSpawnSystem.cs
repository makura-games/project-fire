using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Server.GameObjects;
using Robust.Shared.Physics.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Map;

namespace Content.Server._Scp.Other.LimitedTimedSpawn;

public sealed partial class LimitedTimedSpawnSystem : EntitySystem
{
    [Dependency] private PhysicsSystem _physics = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    private readonly Dictionary<int, HashSet<EntityUid>> _savedHashes = new();
    private readonly List<(EntityCoordinates Coords, LimitedTimedSpawnComponent Comp)> _readyToSpawn = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LimitedTimedSpawnComponent, MapInitEvent>(OnStartup);
        SubscribeLocalEvent<LimitedTimedSpawnComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnStartup(Entity<LimitedTimedSpawnComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextSpawn = _timing.CurTime + ent.Comp.IntervalSeconds;
    }

    private void OnShutdown(Entity<LimitedTimedSpawnComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.EntityIdentificator == null)
            return;

        if (!_savedHashes.TryGetValue((int)ent.Comp.EntityIdentificator, out var list))
            return;

        list.Remove(ent);

        if (list.Count == 0)
            _savedHashes.Remove((int)ent.Comp.EntityIdentificator);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _savedHashes.Clear();
        _readyToSpawn.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var entities = EntityQueryEnumerator<LimitedTimedSpawnComponent>();
        _readyToSpawn.Clear();

        while (entities.MoveNext(out var ent, out var comp))
        {
            if (_timing.CurTime < comp.NextSpawn)
                continue;

            comp.NextSpawn = _timing.CurTime + comp.IntervalSeconds;
            int hash;

            if (comp.EntityIdentificator == null)
            {
                hash = _random.Next(int.MinValue, int.MaxValue);
                comp.EntityIdentificator = hash;
            }
            else
                hash = (int)comp.EntityIdentificator;

            if (!_savedHashes.TryGetValue(hash, out _))
            {
                _savedHashes.TryAdd(hash, new());
                _savedHashes[hash].Add(ent);
            }

            if (_savedHashes[hash].Count > comp.EntitiesLimit)
                continue;

            _readyToSpawn.Add((Transform(ent).Coordinates, comp));
        }

        foreach (var data in _readyToSpawn)
        {
            if (data.Comp.EntityIdentificator == null)
                continue;

            var hash = (int)data.Comp.EntityIdentificator;

            if (_savedHashes[hash].Count > data.Comp.EntitiesLimit)
                continue;

            if (!_random.Prob(data.Comp.Chance))
                continue;

            var newEnt = Spawn(data.Comp.Prototype, data.Coords);
            _savedHashes.TryAdd(hash, new());
            _savedHashes[hash].Add(newEnt);

            if (TryComp<LimitedTimedSpawnComponent>(newEnt, out var sComp))
                sComp.EntityIdentificator = hash;

            if (data.Comp.ImpulseStrength != 0)
                ThrowRand(newEnt, data.Comp.ImpulseStrength);
        }
    }

    private void ThrowRand(EntityUid uid, float impulseStrength)
    {
        if (!TryComp<PhysicsComponent>(uid, out var physics) ||
            impulseStrength <= 0)
            return;

        var angle = _random.NextAngle();
        var direction = angle.ToVec();

        _physics.ApplyLinearImpulse(uid, direction * impulseStrength, body: physics);
        _physics.ApplyAngularImpulse(uid, _random.NextFloat(-0.5f, 0.5f), body: physics);
    }
}
