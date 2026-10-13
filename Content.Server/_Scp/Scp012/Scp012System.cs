using Content.Server._Scp.Fear;
using Content.Shared.Mobs.Systems;
using Content.Shared.Hands;
using Content.Shared.Movement.Systems;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp012;
using Content.Shared.Hands.Components;
using Robust.Server.Audio;

namespace Content.Server._Scp.Scp012;

// TODO: Больше предикшена
// TODO: Перенести систему притягивания и форсированного подбирания для SCP-035.
// TODO: Переделать систему притягивания под pathfinding, чтобы обходить препятствия
public sealed partial class Scp012System : SharedScp012System
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private FearSystem _fear = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<Scp012Component> _scpQuery;
    private EntityQuery<Scp012VictimComponent> _victimQuery;

    private readonly HashSet<Entity<HandsComponent>> _cachedEntities = [];

    public override void Initialize()
    {
        base.Initialize();

        _scpQuery = GetEntityQuery<Scp012Component>();
        _victimQuery = GetEntityQuery<Scp012VictimComponent>();

        InitializeVictim();

        SubscribeLocalEvent<Scp012Component, GotEquippedHandEvent>(OnGotEquipped);
        SubscribeLocalEvent<Scp012Component, EntParentChangedMessage>(OnParentChanged);

        SubscribeLocalEvent<Scp012Component, ComponentShutdown>(OnShutdown);
    }

    #region Event handlers

    private void OnGotEquipped(Entity<Scp012Component> ent, ref GotEquippedHandEvent args)
    {
        if (!_whitelist.CheckBoth(args.User, ent.Comp.Blacklist, ent.Comp.Whitelist))
            return;

        var victimComp = EnsureComp<Scp012VictimComponent>(args.User);
        victimComp.Source = ent;

        _movementSpeed.RefreshMovementSpeedModifiers(args.User);
        _fear.TrySetFearLevel(args.User, ent.Comp.FearOnPickup);

        var victimEnt = (args.User, victimComp);
        SetAudio(victimEnt, ent, true);
        SetNextSuicideTime(victimEnt);
    }

    private void OnParentChanged(Entity<Scp012Component> ent, ref EntParentChangedMessage args)
    {
        if (!_victimQuery.TryComp(args.OldParent, out var victim))
            return;

        var victimEntity = (args.OldParent.Value, victim);
        SetAudio(victimEntity, enable: false);
        SetNextLosCheckTime(victimEntity);
    }

    private void OnShutdown(Entity<Scp012Component> ent, ref ComponentShutdown args)
    {
        var query = EntityQueryEnumerator<Scp012VictimComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Source != ent)
                continue;

            RemCompDeferred<Scp012VictimComponent>(uid);
        }
    }

    #endregion

    #region Update

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateScp();
        UpdateVictims();
    }

    private void UpdateScp()
    {
        var query = EntityQueryEnumerator<Scp012Component>();
        while (query.MoveNext(out var uid, out var scp))
        {
            _cachedEntities.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid),
                scp.Range,
                _cachedEntities,
                LookupFlags.Dynamic | LookupFlags.Approximate);

            foreach (var ent in _cachedEntities)
            {
                if (_victimQuery.HasComp(ent))
                    continue;

                if (!_whitelist.CheckBoth(ent, scp.Blacklist, scp.Whitelist))
                    continue;

                if (!_mobState.IsAlive(ent))
                    continue;

                if (!_proximity.IsRightType(uid, ent, LineOfSightBlockerLevel.None))
                    continue;

                var victim = EnsureComp<Scp012VictimComponent>(ent);
                victim.Source = uid;
            }
        }
    }

    #endregion

    #region Helpers

    private void SetAudio(Entity<Scp012VictimComponent> source, Entity<Scp012Component>? scp = null, bool enable = false)
    {
        source.Comp.AudioStream = _audio.Stop(source.Comp.AudioStream);
        if (enable && scp.HasValue)
            source.Comp.AudioStream = _audio.PlayPvs(scp.Value.Comp.WritingSound, source)?.Entity;
    }

    #endregion
}
