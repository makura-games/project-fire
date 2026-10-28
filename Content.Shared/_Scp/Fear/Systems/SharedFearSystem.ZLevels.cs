using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Watching;
using Content.Shared._Scp.Weapons.Ranged;
using Robust.Shared.Network;

namespace Content.Shared._Scp.Fear.Systems;

public abstract partial class SharedFearSystem
{
    // Межэтажный поиск добавляет источники в существующий путь страха; обычное наблюдение не меняется.
    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityQuery<WatchingTargetComponent> _zWatchingQuery = default!;
    [Dependency] private EntityQuery<ProximityReceiverComponent> _zProximityQuery = default!;
    [Dependency] private EntityQuery<CEZMapComponent> _zMapQuery = default!;

    private const float ZLevelFearStrength = 0.5f;
    private static readonly TimeSpan ZLevelFearCheckInterval = TimeSpan.FromSeconds(0.2);
    private TimeSpan _nextZLevelFearCheck;
    private TimeSpan _lastZLevelFearCheck;
    private readonly List<(EntityUid Uid, FearSourceComponent Source, EntityUid? Map)> _zFearSources = [];

    private void InitializeZLevelFear()
    {
        _nextZLevelFearCheck = TimeSpan.Zero;
    }

    private void UpdateZLevelFear()
    {
        // Сервер подтверждает видимость; клиенты получают уже существующие сетевые состояния эффектов.
        if (!_net.IsServer ||
            (_timing.CurTime >= _lastZLevelFearCheck && _timing.CurTime < _nextZLevelFearCheck))
            return;

        _lastZLevelFearCheck = _timing.CurTime;
        _nextZLevelFearCheck = _timing.CurTime + ZLevelFearCheckInterval;
        var activeEffects = EntityQueryEnumerator<ActiveCloseFearComponent, FearComponent>();
        while (activeEffects.MoveNext(out var uid, out var active, out var fear))
        {
            if (active.AcrossZLevels && !HasComp<CEZLevelViewerComponent>(uid))
                ClearCloseFear((uid, fear));
        }
        _zFearSources.Clear();
        var sources = EntityQueryEnumerator<FearSourceComponent, TransformComponent>();
        while (sources.MoveNext(out var uid, out var source, out var xform))
        {
            if (xform.MapUid is { } map && _zMapQuery.HasComp(map))
                _zFearSources.Add((uid, source, map));
        }

        var viewers = EntityQueryEnumerator<FearComponent, CEZLevelViewerComponent, TransformComponent>();
        while (viewers.MoveNext(out var uid, out var fear, out _, out var xform))
        {
            Entity<FearComponent> ent = (uid, fear);
            EntityUid? closeSource = null;
            FearSourceComponent? closeFear = null;
            var nearest = float.PositiveInfinity;
            var closeDistance = 0f;
            var closeRange = 0f;

            if (_mobState.IsAlive(ent) && xform.MapUid is { } map && _zMapQuery.HasComp(map))
            {
                foreach (var candidate in _zFearSources)
                {
                    if (candidate.Map == map || candidate.Uid == uid ||
                        (candidate.Source.PhobiaType is { } phobia && !fear.Phobias.Contains(phobia)))
                        continue;

                    if (_zWatchingQuery.HasComp(candidate.Uid) && candidate.Source.UponSeenState != FearState.None &&
                        _watching.CanSeeAcrossZ(uid, candidate.Uid, fear.SeenBlockerLevel, out _))
                    {
                        if (TryApplyObservedFear(ent, candidate.Source.UponSeenState, ZLevelFearStrength))
                        {
                            AddSeenFearMood(ent, ZLevelFearStrength);
                            _highlight.Highlight(candidate.Uid, uid);
                        }
                    }

                    if (candidate.Source.UponComeCloser == FearState.None ||
                        !_zProximityQuery.TryComp(candidate.Uid, out var proximity) || proximity.CloseRange <= 0f ||
                        !_watching.CanSeeAcrossZ(uid, candidate.Uid, fear.ProximityBlockerLevel, out var distance) ||
                        distance > proximity.CloseRange)
                        continue;

                    var relativeDistance = distance / proximity.CloseRange;
                    if (relativeDistance >= nearest)
                        continue;

                    nearest = relativeDistance;
                    closeSource = candidate.Uid;
                    closeFear = candidate.Source;
                    closeDistance = distance;
                    closeRange = proximity.CloseRange;
                }
            }

            // Сохраняем действующий локальный источник и его полную силу.
            if (_activeCloseFearQuery.TryComp(uid, out var active) && !active.AcrossZLevels)
                continue;

            if (closeSource is { } sourceUid && closeFear != null)
            {
                ApplyCloseFear(ent, sourceUid, closeFear, closeDistance, closeRange,
                    LineOfSightBlockerLevel.None, ZLevelFearStrength);
            }
            else if (active != null)
            {
                ClearCloseFear(ent);
            }
        }
        _zFearSources.Clear();
    }

    private bool TryApplyObservedFear(Entity<FearComponent> ent, FearState state, float strength)
    {
        if (state == FearState.None || state < ent.Comp.State)
            return false;

        // Новый источник не должен уменьшать уже сохраняющиеся штрафы другого источника.
        if (ent.Comp.State != FearState.None && strength < ent.Comp.EffectStrength &&
            (ent.Comp.FearBasedGrainStrength[state] * strength <
             ent.Comp.FearBasedGrainStrength[ent.Comp.State] * ent.Comp.EffectStrength ||
             ent.Comp.FearBasedVignetteStrength[state] * strength <
             ent.Comp.FearBasedVignetteStrength[ent.Comp.State] * ent.Comp.EffectStrength))
            return false;

        if (strength < ent.Comp.EffectStrength && TryComp<DispersingShotSourceComponent>(ent, out var spread))
        {
            var modifier = ent.Comp.FearBasedSpreadAngleModifier[state];
            var angle = spread.DefaultAngleIncreaseModifier +
                        (Math.Max(spread.DefaultAngleIncreaseModifier, modifier) - spread.DefaultAngleIncreaseModifier) * strength;
            var maxAngle = spread.DefaultMaxAngleMultiplier +
                           (Math.Max(spread.DefaultMaxAngleMultiplier, modifier) - spread.DefaultMaxAngleMultiplier) * strength;
            if (angle < Math.Max(spread.DefaultAngleIncreaseModifier, spread.AngleIncreaseMultiplier) ||
                maxAngle < Math.Max(spread.DefaultMaxAngleMultiplier, spread.MaxAngleMultiplier))
                return false;
        }

        return TrySetFearLevel(ent.AsNullable(), state, strength);
    }

    protected void ResetZLevelFearSearch()
    {
        _nextZLevelFearCheck = TimeSpan.Zero;
        _lastZLevelFearCheck = TimeSpan.Zero;
        _zFearSources.Clear();
    }

    /// <summary>
    /// Whether a matching fear source is still visible on another floor and prevents calming down.
    /// </summary>
    protected bool HasVisibleZLevelFearSource(Entity<FearComponent> ent)
    {
        var sources = EntityQueryEnumerator<FearSourceComponent>();
        while (sources.MoveNext(out var uid, out var source))
        {
            if (source.PhobiaType is { } phobia && !ent.Comp.Phobias.Contains(phobia))
                continue;

            if (source.UponSeenState != FearState.None && _zWatchingQuery.HasComp(uid) &&
                _watching.CanSeeAcrossZ(ent, uid, ent.Comp.SeenBlockerLevel, out _))
                return true;

            if (source.UponComeCloser != FearState.None && _zProximityQuery.TryComp(uid, out var proximity) &&
                _watching.CanSeeAcrossZ(ent, uid, ent.Comp.ProximityBlockerLevel, out var distance) &&
                distance <= proximity.CloseRange)
                return true;
        }

        return false;
    }
}
