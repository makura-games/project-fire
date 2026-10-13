using System.Linq;
using System.Threading;
using Content.Server._Scp.GameTicking.Rules.Components;
using Content.Server._Scp.Scp106.Components;
using Content.Server._Scp.Scp106.Systems;
using Content.Server.AlertLevel;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Ghost;
using Content.Server.Jittering;
using Content.Server.Nuke;
using Content.Server.RoundEnd;
using Content.Server.Speech.EntitySystems;
using Content.Server.Stunnable;
using Content.Shared._Scp.Audio;
using Content.Shared._Scp.Scp106;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.Audio;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Humanoid;
using Content.Shared.Light.Components;
using Content.Shared.StatusEffect;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._Scp.GameTicking.Rules;

// TODO: Разбить этот гигакод на партиалы
public sealed partial class Scp106AscentRule : GameRuleSystem<Scp106AscentRuleComponent>
{
    [Dependency] private JitteringSystem _jittering = default!;
    [Dependency] private StutteringSystem _stuttering = default!;
    [Dependency] private StunSystem _stun = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private AlertLevelSystem _alertLevel = default!;
    [Dependency] private GhostSystem _ghost = default!;
    [Dependency] private NukeSystem _nuke = default!;
    [Dependency] private Scp106System _scp106 = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private AudioEffectsManagerSystem _effectsManager = default!;
    [Dependency] private RoundEndSystem _roundEnd = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan AscentStunTime = TimeSpan.FromSeconds(10f);
    private static readonly TimeSpan AscentJitterTime = TimeSpan.FromSeconds(15f);
    private static readonly TimeSpan AscentStutterTime = TimeSpan.FromSeconds(30f);

    private static readonly TimeSpan AscentFailTime = TimeSpan.FromMinutes(5f);
    private static readonly TimeSpan AscentAnnounceAfter = TimeSpan.FromSeconds(5f);

    private bool _tickEffectEnabled;
    private static readonly TimeSpan TickEffectCooldown = TimeSpan.FromSeconds(1f);
    private TimeSpan _nextTickEffectTime;

    private static readonly SoundSpecifier TickEffectSound =
        new SoundPathSpecifier("/Audio/_Scp/Effects/tick.ogg");

    private static readonly SoundSpecifier ShiftStartSound =
        new SoundCollectionSpecifier("ShiftStart");
    private static readonly SoundSpecifier ShiftPassedSound =
        new SoundPathSpecifier("/Audio/_Scp/Effects/Shift/passed.ogg");

    private static readonly ProtoId<AmbientMusicPrototype> ShiftAddedMusic = "ShiftAdded";
    private static readonly ProtoId<AmbientMusicPrototype> ShiftStartedMusic = "ShiftStarted";
    private static readonly ProtoId<AmbientMusicPrototype> ShiftAvertedMusic = "ShiftAverted";

    private static readonly SoundSpecifier ShiftNoReturnPointReachedMusic =
        new SoundPathSpecifier("/Audio/_Scp/Ambience/Music/Shift/noreturn.ogg");

    private static readonly ProtoId<AudioPresetPrototype> FancyEffect = "Dizzy";

    private const string RuleAddedCode = "scpPurple";
    private const string RuleStartedCode = "scpGamma";

    private static readonly TimeSpan ReturnCachedAlertLevelAfter = TimeSpan.FromSeconds(5f);

    private bool _noReturnPointReached;

    // В теории несколько событий сразу без сранья педалями быть не должно, поэтому для удобства оно будет тут сохранено
    private EntityUid _ruleUid;
    private EntityUid? _spawnPortalsRuleUid;

    private CancellationTokenSource _timerDespawnToken = new ();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp106PortalSpawnerComponent, ComponentShutdown>(OnSpawnerShutdown);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => Clear());

        SubscribeLocalEvent<HumanoidProfileComponent, PlayerAttachedEvent>(OnPlayerAttached);
    }

    private void Clear()
    {
        _tickEffectEnabled = default;
        _nextTickEffectTime = default;
        _noReturnPointReached = default;

        _timerDespawnToken.Cancel();
        _timerDespawnToken = new();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_tickEffectEnabled)
            return;

        if (_nextTickEffectTime > _timing.CurTime)
            return;

        var audio = _audio.PlayGlobal(TickEffectSound, Filter.Broadcast(), true);
        if (audio != null)
            _effectsManager.TryAddEffect(audio.Value, FancyEffect);

        _nextTickEffectTime = _timing.CurTime + TickEffectCooldown;
    }

    protected override void Added(EntityUid uid,
        Scp106AscentRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleAddedEvent args)
    {
        base.Added(uid, component, gameRule, args);

        if (!gameRule.Delay.HasValue)
            return;

        var time = TimeSpan.FromSeconds(gameRule.Delay.Value.Min);
        var timeString = $"{time.Minutes:D2}:{time.Seconds:D2}";

        var message = Loc.GetString("scp106-many-humans-in-backrooms-alarm-announcement", ("time", timeString));
        _chat.DispatchGlobalAnnouncement(message, colorOverride: Color.Firebrick);

        _tickEffectEnabled = true;
        RaiseNetworkEvent(new NetworkAmbientMusicEvent(ShiftAddedMusic));

        if (!TryGetRandomStation(out var station))
            return;

        component.TargetStation = station.Value;

        Timer.Spawn(AscentAnnounceAfter,
            () =>
            {
                component.CachedAlertLevel = _alertLevel.GetLevel(station.Value);
                _alertLevel.SetLevel(station.Value, RuleAddedCode, true, true, true);
            },
            _timerDespawnToken.Token);
    }

    protected override void Started(EntityUid uid,
        Scp106AscentRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        _tickEffectEnabled = false;

        var humans = _scp106.CountHumansInBackrooms();

        // Если событие было остановлено до начала(людей спасли)
#if !DEBUG && !TOOLS
        if (humans < Scp106System.HumansInBackroomsRequiredToAscent)
        {
            EarlyAvert((uid, component));
            return;
        }
#endif

        if (!TryGetRandomStation(out var station))
            return;

        RaiseNetworkEvent(new NetworkAmbientMusicEvent(ShiftStartedMusic));

        var statusEffectQuery = EntityQueryEnumerator<HumanoidProfileComponent, StatusEffectsComponent>();
        while (statusEffectQuery.MoveNext(out var human, out _, out _))
        {
            _stun.TryAddParalyzeDuration(human, AscentStunTime);
            _jittering.DoJitter(human, AscentJitterTime, true, 30f, 300f);
            _stuttering.DoStutter(human, AscentStutterTime, true);

            var coords = Transform(human).Coordinates;
            var nearestLights = _lookup.GetEntitiesInRange<PoweredLightComponent>(coords, 10f, LookupFlags.Static);

            RaiseNetworkEvent(new WarpingOverlayToggle(true), human);

            foreach (var light in nearestLights)
            {
                _ghost.DoGhostBooEvent(light);
            }
        }

        var timeString = $"{AscentFailTime.Minutes:D2}:{AscentFailTime.Seconds:D2}";
        var message = Loc.GetString("scp106-dimension-shift-alarm-announcement", ("time", timeString));
        _chat.DispatchGlobalAnnouncement(message, playDefault: false, colorOverride: Color.Red);

        var audio = _audio.PlayGlobal(
            ShiftStartSound,
            Filter.Broadcast(),
            true,
            new AudioParams().WithVolume(10));

        if (audio != null)
            _effectsManager.TryAddEffect(audio.Value, FancyEffect);

        EnsureComp<Scp106DimensionShiftingMapComponent>(station.Value);

        Timer.Spawn(AscentAnnounceAfter,
            () =>
            {
                _alertLevel.SetLevel(station.Value, RuleStartedCode, false, true, true);
            },
            _timerDespawnToken.Token);

        Timer.Spawn(AscentFailTime, OnTimeEnded, _timerDespawnToken.Token);

        _gameTicker.StartGameRule(component.SpawnPortalsRule, out var ruleEntity);
        _ruleUid = uid;
        _spawnPortalsRuleUid = ruleEntity;
    }

    private void OnTimeEnded()
    {
        if (!_gameTicker.IsGameRuleActive(Scp106System.AscentRule))
            return;

        var timeToExplosion = ToggleNuke();
        _noReturnPointReached = true;

        RaiseNetworkEvent(new NetworkAmbientMusicEventStop());

        var message = Loc.GetString("dimensional-shift-start-alarm-announcement");
        _chat.DispatchGlobalAnnouncement(message, colorOverride: Color.DarkViolet);

        // Завершаем раунд чуть позже конца музыки
        Timer.Spawn(timeToExplosion + TimeSpan.FromSeconds(1f), () => _roundEnd.EndRound(), _timerDespawnToken.Token);
    }

    private TimeSpan ToggleNuke()
    {
        var nukes = EntityQuery<NukeComponent>();
        var nuke = nukes.FirstOrDefault();

        var endSongLenght = _audio.GetAudioLength(_audio.ResolveSound(ShiftNoReturnPointReachedMusic));

        if (nuke == null) // Fallback для карт без ядерки
        {
            var endSong =
                _audio.PlayGlobal(ShiftNoReturnPointReachedMusic, Filter.Broadcast(), true, AudioParams.Default.WithVolume(10f));

            return endSong.HasValue
                ? endSongLenght
                : TimeSpan.FromSeconds(120f); // Fallback
        }

        nuke.ArmMusic = ShiftNoReturnPointReachedMusic;
        nuke.Timer = (int) endSongLenght.TotalSeconds + 7; // Чтобы не моментально начинала ебашить
        nuke.RemainingTime = nuke.Timer;
        _nuke.ArmBomb(nuke.Owner, nuke);

        return TimeSpan.FromSeconds(nuke.Timer);
    }

    private void OnSpawnerShutdown(Entity<Scp106PortalSpawnerComponent> ent, ref ComponentShutdown args)
    {
        if (!_gameTicker.IsGameRuleActive(Scp106System.AscentRule))
            return;

        // Если не успели - уже ничего не поможет
        if (_noReturnPointReached)
            return;

        var foundOtherPortals = false;
        var query = EntityQueryEnumerator<Scp106PortalSpawnerComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (ent.Owner == uid)
                continue;

            foundOtherPortals = true;
            break;
        }

        // Если все порталы уничтожены до начала конца, то все заебись все молодцы
        if (!foundOtherPortals)
            Avert();
    }

    private void Avert()
    {
        _audio.PlayGlobal(
            ShiftPassedSound,
            Filter.Broadcast(),
            true,
            new AudioParams().WithVolume(5));

        var message = Loc.GetString("scp106-dimension-shift-passed-alarm-announcement");
        _chat.DispatchGlobalAnnouncement(message, colorOverride: Color.FromHex("#33A652"));

        RaiseNetworkEvent(new WarpingOverlayToggle(false));
        RaiseNetworkEvent(new NetworkAmbientMusicEvent(ShiftAvertedMusic));

        // Как только все порталы уничтожены завершает события вторжения
        _gameTicker.EndGameRule(_ruleUid);

        if (Exists(_spawnPortalsRuleUid))
            _gameTicker.EndGameRule(_spawnPortalsRuleUid.Value);
    }

    private void EarlyAvert(Entity<Scp106AscentRuleComponent> rule)
    {
        var avertedMessage = Loc.GetString("scp106-dimension-shift-averted-announcement");
        _chat.DispatchGlobalAnnouncement(avertedMessage, colorOverride: Color.Firebrick);

        _alertLevel.SetLevel(rule.Comp.TargetStation, RuleAddedCode, true, true, true);

        Timer.Spawn(ReturnCachedAlertLevelAfter,
            () =>
            {
                _alertLevel.SetLevel(rule.Comp.TargetStation, rule.Comp.CachedAlertLevel, true, true, true);
            },
            _timerDespawnToken.Token);

        _gameTicker.EndGameRule(rule);
    }

    /// <summary>
    /// Обработка новозашедших/перезашедших игроков
    /// </summary>
    /// TODO: Реализовать получше, но оно в принципе работает
    private void OnPlayerAttached(Entity<HumanoidProfileComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!IsConnectedRecently(args.Player.ConnectedTime))
            return;

        if (_gameTicker.IsGameRuleActive(Scp106System.AscentRule))
        {
            RaiseNetworkEvent(new WarpingOverlayToggle(true), ent);
            RaiseNetworkEvent(new NetworkAmbientMusicEvent(ShiftStartedMusic), ent);
        }
        else if (_gameTicker.IsGameRuleAdded(Scp106System.AscentRule))
        {
            RaiseNetworkEvent(new NetworkAmbientMusicEvent(ShiftAddedMusic), ent);
        }
    }

    // TODO: Пофиксить через нормальную реализацию и получение серверного времени через какую-нибудь систему
    private static bool IsConnectedRecently(DateTime connectionTime)
    {
        var deltaTime = DateTime.Now.TimeOfDay.Minutes - connectionTime.TimeOfDay.Minutes;

        // Если с момента подключения прошло <1 минуты, то мы зашли недавно
        return deltaTime <= 1;
    }
}
