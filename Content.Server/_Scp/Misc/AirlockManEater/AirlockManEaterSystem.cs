using System.Threading;
using Content.Server.Doors.Systems;
using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Components;
using Content.Shared._Scp.Other.AirlockManEater;
using Content.Shared._Scp.Other.Events;
using Content.Shared._Scp.Proximity;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._Scp.Misc.AirlockManEater;

// TODO: Фикс отстающего от маски спрайта. Или наоборот.
public sealed partial class AirlockManEaterSystem : SharedAirlockManEaterSystem
{
    [Dependency] private DoorSystem _door = default!;
    [Dependency] private AirlockSystem _airlock = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private AudioSystem _audio = default!;

    private static readonly TimeSpan CrushAgainAfter = TimeSpan.FromSeconds(0.5f);
    private static readonly TimeSpan LaughAfter = TimeSpan.FromSeconds(0.3f);

    private static readonly SoundSpecifier AirlockLaughSound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");

    private static EntityQuery<DoorComponent> _doors;
    private static EntityQuery<AirlockComponent> _airlocks;

    private static CancellationTokenSource _token = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AirlockManEaterComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<AirlockManEaterComponent, AirlockCrushedEvent>(OnCrush);

        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());

        _doors = GetEntityQuery<DoorComponent>();
        _airlocks = GetEntityQuery<AirlockComponent>();
    }

    private void OnMapInit(Entity<AirlockManEaterComponent> ent, ref MapInitEvent args)
    {
        DoAirlockStuff(ent);
        DoDoorStuff(ent);
        MakeScary(ent);
    }

    private void DoAirlockStuff(Entity<AirlockManEaterComponent> ent)
    {
        if (!_airlocks.TryComp(ent, out var airlockComponent))
            return;

        _airlock.SetSafety(airlockComponent, false);
        _airlock.SetAutoCloseDelayModifier(airlockComponent, AirlockManEaterComponent.AutoCloseModifier);
    }

    private void DoDoorStuff(Entity<AirlockManEaterComponent> ent)
    {
        if (!_doors.TryComp(ent, out var doorComponent))
            return;

        doorComponent.CanCrush = true;
        doorComponent.CrushDamage = ent.Comp.CrushDamage;
        doorComponent.DoorStunTime = ent.Comp.StunTime;

        doorComponent.OpenTimeOne /= AirlockManEaterComponent.TimeModifier;
        doorComponent.OpenTimeTwo /= AirlockManEaterComponent.TimeModifier;
        doorComponent.CloseTimeOne /= AirlockManEaterComponent.TimeModifier;
        doorComponent.CloseTimeTwo /= AirlockManEaterComponent.TimeModifier;

        doorComponent.OpeningAnimationTime /= AirlockManEaterComponent.TimeModifier;
        doorComponent.ClosingAnimationTime /= AirlockManEaterComponent.TimeModifier;

        doorComponent.UnsafeClosing = true;

        Dirty(ent, doorComponent);
    }

    /// <summary>
    /// Делает шлюз страшным, чтобы дать игроку возможность отличить опасный шлюз от неопасного.
    /// </summary>
    private void MakeScary(EntityUid uid)
    {
        var fearSource = EnsureComp<FearSourceComponent>(uid);
        fearSource.UponSeenState = FearState.None;
        fearSource.UponComeCloser = FearState.Anxiety;
        fearSource.GrainShaderStrength = new(0, 300);
        fearSource.VignetteShaderStrength = new(0, 120);
        fearSource.PlayHeartbeatSound = false;

        Dirty(uid, fearSource);

        var proximityReceiver = EnsureComp<ProximityReceiverComponent>(uid);
        proximityReceiver.RequiredLineOfSight = LineOfSightBlockerLevel.None;
        proximityReceiver.CloseRange = 2f;

        Dirty(uid, proximityReceiver);
    }

    private void OnCrush(Entity<AirlockManEaterComponent> ent, ref AirlockCrushedEvent args)
    {
        var entity = GetEntity(args.Entity);

        if (!HasComp<MobStateComponent>(entity))
            return;

        if (_mob.IsDead(entity))
            return;

        Timer.Spawn(LaughAfter, () => _audio.PlayPvs(AirlockLaughSound, ent, AudioParams.Default.WithPitchScale(0.5f)), _token.Token);
        Timer.Spawn(CrushAgainAfter, () => _door.TryOpen(ent), _token.Token);

        // TODO: Какой-нибудь звук победы шлюза над человеком
    }

    private static void Clear()
    {
        _token.Cancel();
        _token = new();
    }
}
