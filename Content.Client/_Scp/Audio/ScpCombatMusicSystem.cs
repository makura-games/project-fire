using Content.Client.Audio;
using Content.Client.Gameplay;
using Content.Shared._Scp.Audio.CombatMusic;
using Content.Shared._Scp.ScpCCVars;
using Content.Shared.CombatMode;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Scp.Audio;

// Механика адаптирована из Hullrot: https://github.com/Sector-Crescent/Hullrot
/// <summary>
/// Plays a local, non-looping combat soundtrack and retains its moving timeline briefly after combat ends.
/// </summary>
public sealed partial class ScpCombatMusicSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = null!;
    [Dependency] private ContentAudioSystem _contentAudio = null!;
    [Dependency] private SharedScpCombatMusicSystem _combatMusic = null!;
    [Dependency] private IConfigurationManager _configuration = null!;
    [Dependency] private IPlayerManager _player = null!;
    [Dependency] private IStateManager _state = null!;
    [Dependency] private IGameTiming _timing = null!;
    [Dependency] private EntityQuery<CombatModeComponent> _combatModeQuery;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery;
    [Dependency] private EntityQuery<AudioComponent> _audioQuery;

    private readonly ScpCombatMusicPlayback _playback = new();
    private EntityUid? _localEntity;
    private EntityUid? _stream;
    private string? _musicId;
    private float _trackGain;
    private float _volume;
    private bool _enabled;
    private bool _combatMode;
    private bool _roundEnded;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_configuration, ScpCCVars.CombatMusicEnabled, OnEnabledChanged, true);
        Subs.CVar(_configuration, ScpCCVars.CombatMusicVolume, OnVolumeChanged, true);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<PlayAmbientMusicEvent>(OnPlayAmbientMusic);
        SubscribeNetworkEvent<RoundEndMessageEvent>(OnRoundEnd);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _state.OnStateChanged += OnStateChanged;
    }

    public override void Shutdown()
    {
        _state.OnStateChanged -= OnStateChanged;
        Reset();
        base.Shutdown();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var entity = _player.LocalEntity;
        if (!_enabled || _roundEnded || _state.CurrentState is not GameplayState || entity == null)
        {
            Reset();
            return;
        }

        if (_localEntity != entity)
        {
            Reset();
            _localEntity = entity;
        }

        if (!_combatModeQuery.TryComp(entity, out var combatMode) ||
            !_mobStateQuery.TryComp(entity, out var mobState) || mobState.CurrentState == MobState.Dead ||
            !_combatMusic.TryGetMusic(entity.Value, out var music))
        {
            Reset();
            return;
        }

        if (_musicId != music.ID)
        {
            ResetPlayback();
            _musicId = music.ID;
        }

        // Сверяем итоговое локальное состояние вне prediction, включая серверные коррекции и смену тела.
        _combatMode = combatMode.IsInCombatMode;
        var action = _playback.SetCombatMode(_combatMode, _timing.RealTime, IsTrackPlaying());
        switch (action)
        {
            case ScpCombatMusicPlaybackAction.Start:
                StartTrack(music.Sound);
                break;
            case ScpCombatMusicPlaybackAction.Stop:
                StopTrack();
                break;
            case ScpCombatMusicPlaybackAction.None:
                break;
            default:
                throw new InvalidOperationException($"Unknown combat music playback action: {action}");
        }

        // Сервер может включить новый фон напрямую, в обход PlayAmbientMusicEvent.
        if (_combatMode && _stream != null)
            _contentAudio.DisableAmbientMusic();

        UpdateGain();
    }

    private bool IsTrackPlaying()
    {
        // Новый источник начинает играть в FrameUpdate аудиосистемы, который может идти после нашего.
        return _audioQuery.TryComp(_stream, out var audio) && (!audio.Started || audio.Playing);
    }

    private void StartTrack(SoundSpecifier sound)
    {
        StopTrack();
        var resolved = _audio.ResolveSound(sound);
        if (ResolvedSoundSpecifier.IsNullOrEmpty(resolved))
            return;

        _trackGain = SharedAudioSystem.VolumeToGain(sound.Params.Volume);
        var stream = _audio.PlayGlobal(resolved,
            Filter.Local(),
            false,
            sound.Params.WithLoop(false).WithVolume(float.NegativeInfinity));
        _stream = stream?.Entity;
    }

    private void UpdateGain()
    {
        if (_audioQuery.TryComp(_stream, out var audio))
            _audio.SetGain(_stream, _trackGain * _volume * _playback.Gain, audio);
    }

    private void StopTrack()
    {
        if (_audioQuery.TryComp(_stream, out var audio))
        {
            // При смене тела Stop пропускает удаление во время применения состояния сервера.
            _audio.SetState(_stream, AudioState.Stopped, component: audio);
            QueueDel(_stream);
        }

        _stream = null;
        _trackGain = 0f;
    }

    private void ResetPlayback()
    {
        StopTrack();
        _playback.Reset();
        _musicId = null;
        _combatMode = false;
    }

    private void Reset()
    {
        ResetPlayback();
        _localEntity = null;
    }

    private void OnEnabledChanged(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
            Reset();
    }

    private void OnVolumeChanged(float volume)
    {
        _volume = float.IsFinite(volume) ? MathF.Max(0f, volume) : 0f;
        UpdateGain();
    }

    private void OnPlayerDetached(LocalPlayerDetachedEvent args)
    {
        Reset();
    }

    private void OnStateChanged(StateChangedEventArgs args)
    {
        if (args.NewState is not GameplayState)
            Reset();
    }

    private void OnRoundEnd(RoundEndMessageEvent args)
    {
        _roundEnded = true;
        Reset();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _roundEnded = false;
        Reset();
    }

    private void OnPlayAmbientMusic(ref PlayAmbientMusicEvent args)
    {
        if (_combatMode && IsTrackPlaying())
            args.Cancelled = true;
    }
}
