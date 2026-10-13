using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Fear.Systems;
using Content.Shared._Sunrise.Mood;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Rejuvenate;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Fear;

public sealed partial class FearSystem : SharedFearSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CalmDownCheckCooldown = TimeSpan.FromSeconds(1f);
    private TimeSpan _nextCalmDownCheck = TimeSpan.Zero;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanUp);

        InitializeSoundEffects();
        InitializeFears();
        InitializeTraits();
        InitializeEntityEffects();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateCalmDown();
        UpdateHemophobia();
    }

    private void UpdateCalmDown()
    {
        if (_timing.CurTime < _nextCalmDownCheck)
            return;

        var query = EntityQueryEnumerator<FearComponent, MobStateComponent>();

        // Проходимся по людям с компонентом страха и уменьшаем уровень страха со временем
        while (query.MoveNext(out var uid, out var fear, out var mob))
        {
            if (fear.State == FearState.None)
                continue;

            if (!_mob.IsAlive(uid, mob))
                continue;

            if (_timing.CurTime < fear.NextTimeDecreaseFearLevel)
                continue;

            var entity = (uid, fear);

            // Если по какой-то причине не получилось успокоиться, то ждем снова
            // Это нужно, чтобы игрок только что отойдя от источника страха не успокоился моментально
            if (!TryCalmDown(entity))
                SetNextCalmDownTime(entity);
        }

        _nextCalmDownCheck = _timing.CurTime + CalmDownCheckCooldown;
    }

    /// <summary>
    /// Пытается успокоить сущность, испытывающую страх.
    /// Понижает уровень страха на 1, пока не успокоит полностью.
    /// </summary>
    public bool TryCalmDown(Entity<FearComponent> ent)
    {
        // Проверка на то, что мы в данный момент не смотрим на какую-то страшную сущность.
        // Нельзя успокоиться, когда мы смотрим на источник страха.
        if (_watching.TryGetAnyEntitiesVisibleTo<FearSourceComponent>(ent.Owner, ent.Comp.SeenBlockerLevel))
            return false;

        var newFearState = GetDecreasedLevel(ent.Comp.State);

        // АХТУНГ, МИСПРЕДИКТ!!
        // Использовать только с сервера до предикта Solution
        var attempt = new FearCalmDownAttemptEvent(newFearState);
        RaiseLocalEvent(ent, attempt);

        if (attempt.Cancelled)
            return false;

        if (!TrySetFearLevel(ent.AsNullable(), newFearState))
            return false;

        return true;
    }

    protected override void OnShutdown(Entity<FearComponent> ent, ref ComponentShutdown args)
    {
        base.OnShutdown(ent, ref args);

        RemoveMoodEffects(ent);
    }

    protected override void OnRejuvenate(Entity<FearComponent> ent, ref RejuvenateEvent args)
    {
        base.OnRejuvenate(ent, ref args);

        RemoveMoodEffects(ent);
    }

    private void RemoveMoodEffects(EntityUid uid)
    {
        RaiseLocalEvent(uid, new MoodRemoveEffectEvent(MoodSomeoneDiedOnMyEyes));
        RaiseLocalEvent(uid, new MoodRemoveEffectEvent(MoodHemophobicSeeBlood));
        RaiseLocalEvent(uid, new MoodRemoveEffectEvent(MoodHemophobicBleeding));
    }

    private void OnCleanUp(RoundRestartCleanupEvent args)
    {
        _nextHemophobiaCheck = TimeSpan.Zero;
        _nextCalmDownCheck = TimeSpan.Zero;
    }
}
