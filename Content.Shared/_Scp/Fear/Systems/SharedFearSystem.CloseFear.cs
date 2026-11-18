using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Shaders.Grain;
using Content.Shared._Scp.Shaders.Vignette;

namespace Content.Shared._Scp.Fear.Systems;

public abstract partial class SharedFearSystem
{
    private EntityQuery<ActiveCloseFearComponent> _activeCloseFearQuery;

    private void InitializeCloseFear()
    {
        SubscribeLocalEvent<ActiveProximityTargetComponent, ComponentShutdown>(OnActiveProximityShutdown);

        _activeCloseFearQuery = GetEntityQuery<ActiveCloseFearComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateCloseFear();
        UpdateZLevelFear();
    }

    private void UpdateCloseFear()
    {
        var query = EntityQueryEnumerator<ActiveProximityTargetComponent, FearComponent>();
        while (query.MoveNext(out var uid, out var proximity, out var fear))
        {
            SyncCloseFear((uid, fear), (uid, proximity));
        }
    }

    private void SyncCloseFear(Entity<FearComponent> ent, Entity<ActiveProximityTargetComponent> proximity)
    {
        if (!_mobState.IsAlive(ent))
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        if (!_fearSourceQuery.TryComp(proximity.Comp.Receiver, out var source))
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        if (source.UponComeCloser == FearState.None)
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        if (source.PhobiaType.HasValue && !ent.Comp.Phobias.Contains(source.PhobiaType.Value))
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        var blockerLevel = _proximity.GetLightOfSightBlockerLevel(proximity.Comp.Receiver, ent);
        if (blockerLevel > ent.Comp.ProximityBlockerLevel)
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        if (!_watching.IsWatchedBy(proximity.Comp.Receiver, ent, checkProximity: false, useFov: false, checkBlinking: false))
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        var sourceCoords = Transform(proximity.Comp.Receiver).Coordinates;
        var targetCoords = Transform(ent).Coordinates;
        if (!sourceCoords.TryDistance(EntityManager, targetCoords, out var range))
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        if (range > proximity.Comp.CloseRange)
        {
            ClearSameLevelCloseFear(ent);
            return;
        }

        ApplyCloseFear(ent, proximity.Comp.Receiver, source, range, proximity.Comp.CloseRange, blockerLevel);
    }

    private void ApplyCloseFear(Entity<FearComponent> ent, EntityUid sourceUid, FearSourceComponent source,
        float range, float closeRange, LineOfSightBlockerLevel blockerLevel, float strength = 1f)
    {
        var hadActive = _activeCloseFearQuery.TryComp(ent, out var activeCloseFear);
        activeCloseFear ??= EnsureComp<ActiveCloseFearComponent>(ent);
        var sourceChanged = hadActive && activeCloseFear.Source != sourceUid;
        var strengthChanged = hadActive && activeCloseFear.EffectStrength != strength;

        if (!hadActive || strengthChanged)
        {
            if (strengthChanged)
                RemoveCloseFearMood(ent);
            AddNegativeMoodEffect(ent, MoodSourceClose, strength);
        }
        if (sourceChanged)
        {
            RemoveSoundEffects(ent.Owner);
        }

        activeCloseFear.Source = sourceUid;
        activeCloseFear.EffectStrength = strength;
        activeCloseFear.AcrossZLevels = strength < 1f;
        if (!hadActive || sourceChanged || strengthChanged)
            Dirty(ent, activeCloseFear);

        StartEffects(ent, source.PlayHeartbeatSound, source.PlayBreathingSound);

        TryApplyObservedFear(ent, source.UponComeCloser, strength);

        RecalculateEffectsStrength(ent.Owner, range, closeRange);

        SetRangeBasedShaderStrength<GrainOverlayComponent>(
            ent.Owner,
            range,
            closeRange,
            source.GrainShaderStrength,
            blockerLevel,
            ent.Comp,
            strength);

        SetRangeBasedShaderStrength<VignetteOverlayComponent>(
            ent.Owner,
            range,
            closeRange,
            source.VignetteShaderStrength,
            blockerLevel,
            ent.Comp,
            strength);
    }

    private void OnActiveProximityShutdown(Entity<ActiveProximityTargetComponent> ent, ref ComponentShutdown args)
    {
        if (!_fearQuery.TryComp(ent, out var fear))
            return;

        ClearSameLevelCloseFear((ent.Owner, fear));
    }

    private void ClearSameLevelCloseFear(Entity<FearComponent> ent)
    {
        // Межэтажная близость проверяется своим поиском, а не локальным ProximityTarget.
        if (_activeCloseFearQuery.TryComp(ent, out var active) && active.AcrossZLevels)
            return;

        ClearCloseFear(ent);
    }

    private void ClearCloseFear(Entity<FearComponent> ent)
    {
        RemComp<ActiveCloseFearComponent>(ent);

        SetFearBasedShaderStrength(ent);

        RemoveSoundEffects(ent.Owner);
        RemoveCloseFearMood(ent.Owner);
    }
}
