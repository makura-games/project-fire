using Content.Client.DamageState;
using Content.Shared._Scp.Scp247;
using Content.Shared._Scp.Watching;
using Content.Shared.Ghost;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Scp247;

// TODO: Анхардкод
public sealed partial class Scp247System : SharedScp247System
{
    private const string CatState = "scp-247";
    private const string TigerState = "angry";
    private const string DeadState = "dead";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private readonly Dictionary<EntityUid, Observation> _observations = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp247Component, EntitySeenEvent>(OnEntitySeen);
        SubscribeLocalEvent<Scp247Component, AppearanceChangeEvent>(OnAppearanceChange,
            after: [typeof(DamageStateVisualizerSystem)]);
    }

    public override void Shutdown()
    {
        _observations.Clear();

        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var localPlayer = _player.LocalEntity;
        var isGhost = localPlayer is { } player && HasComp<GhostComponent>(player);
        var hasProtection = localPlayer is { } protectedPlayer && HasComp<Scp247ProtectionComponent>(protectedPlayer);

        if (isGhost)
            _observations.Clear();

        var query = EntityQueryEnumerator<Scp247Component, MobStateComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var scp247, out var mobState, out var sprite))
        {
            ApplyState(uid, scp247, mobState, sprite, localPlayer, isGhost, hasProtection, _timing.CurTime);
        }

        if (_observations.Count == 0)
            return;

        var observsToRemove = new List<EntityUid>();
        foreach (var (uid, observation) in _observations)
        {
            if (!TryComp<Scp247Component>(uid, out var scp247) ||
                _timing.CurTime - observation.LastSeen >= scp247.ResetTime)
                observsToRemove.Add(uid);
        }

        foreach (var uid in observsToRemove)
        {
            _observations.Remove(uid);
        }
    }

    private void OnEntitySeen(Entity<Scp247Component> ent, ref EntitySeenEvent args)
    {
        if (_player.LocalEntity != args.Viewer || HasComp<GhostComponent>(args.Viewer))
            return;

        var now = _timing.CurTime;
        if (!_observations.TryGetValue(ent.Owner, out var observation))
            observation = new Observation(now, now);
        else
            observation.LastSeen = now;

        _observations[ent.Owner] = observation;
    }

    private void OnAppearanceChange(Entity<Scp247Component> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !TryComp<MobStateComponent>(ent, out var mobState))
            return;

        ApplyState(ent.Owner, ent.Comp, mobState, args.Sprite, _player.LocalEntity,
            _player.LocalEntity is { } player && HasComp<GhostComponent>(player),
            _player.LocalEntity is { } protectedPlayer && HasComp<Scp247ProtectionComponent>(protectedPlayer),
            _timing.CurTime);
    }

    private void ApplyState(EntityUid uid, Scp247Component scp247, MobStateComponent mobState,
        SpriteComponent sprite, EntityUid? localPlayer, bool isGhost, bool hasProtection, TimeSpan now)
    {
        if (!_sprite.LayerMapTryGet((uid, sprite), DamageStateVisualLayers.Base, out var layer, false))
            return;

        var state = mobState.CurrentState == MobState.Dead
            ? DeadState
            : ShouldShowTiger(uid, scp247, localPlayer, isGhost, hasProtection, now) ? TigerState : CatState;

        if (_sprite.LayerGetRsiState((uid, sprite), layer).Name == state)
            return;

        _sprite.LayerSetRsiState((uid, sprite), layer, state);
    }

    private bool ShouldShowTiger(EntityUid uid, Scp247Component scp247, EntityUid? localPlayer,
        bool isGhost, bool hasProtection, TimeSpan now)
    {
        if (isGhost)
            return false;

        if (hasProtection || localPlayer == uid)
            return true;

        if (!_observations.TryGetValue(uid, out var observation) ||
            now - observation.LastSeen >= scp247.ResetTime)
        {
            return false;
        }

        return now - observation.SeenSince >= scp247.RevealTime;
    }

    private struct Observation(TimeSpan seenSince, TimeSpan lastSeen)
    {
        public TimeSpan SeenSince = seenSince;
        public TimeSpan LastSeen = lastSeen;
    }
}
