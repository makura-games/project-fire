namespace Content.Client._Scp.Audio;

/// <summary>
/// Controls combat track gain and retention without depending on an audio device.
/// A retained stream keeps playing even at zero gain.
/// </summary>
public sealed class ScpCombatMusicPlayback
{
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetentionDuration = TimeSpan.FromSeconds(10);

    private bool _combatMode;
    private bool _hasTrack;
    private TimeSpan? _retainUntil;
    private TimeSpan _fadeStarted;
    private TimeSpan _fadeDuration = FadeInDuration;
    private float _fadeFrom;
    private float _fadeTarget;

    /// <summary>
    /// Current fade multiplier, independent of the track and user volume settings.
    /// </summary>
    public float Gain { get; private set; }

    /// <summary>
    /// Updates the gain envelope and handles combat mode transitions.
    /// Calling this every frame does not restart the track or retention when the mode is unchanged.
    /// </summary>
    public ScpCombatMusicPlaybackAction SetCombatMode(bool enabled, TimeSpan now, bool trackPlaying)
    {
        var action = Update(now, trackPlaying);
        if (_combatMode == enabled)
            return action;

        _combatMode = enabled;
        if (enabled)
        {
            _retainUntil = null;
            if (!_hasTrack)
            {
                _hasTrack = true;
                Gain = 0f;
                FadeTo(1f, now, FadeInDuration);
                return ScpCombatMusicPlaybackAction.Start;
            }

            FadeTo(1f, now, FadeInDuration);
        }
        else if (_hasTrack)
        {
            _retainUntil = now + RetentionDuration;
            FadeTo(0f, now, FadeOutDuration);
        }

        return action;
    }

    /// <summary>
    /// Advances the fade and releases a finished or expired track without starting another one.
    /// </summary>
    public ScpCombatMusicPlaybackAction Update(TimeSpan now, bool trackPlaying)
    {
        if (!_hasTrack)
            return ScpCombatMusicPlaybackAction.None;

        if (!trackPlaying || _retainUntil is { } deadline && now >= deadline)
        {
            ClearTrack();
            return ScpCombatMusicPlaybackAction.Stop;
        }

        var fraction = (float)((now - _fadeStarted).TotalSeconds / _fadeDuration.TotalSeconds);
        fraction = Math.Clamp(fraction, 0f, 1f);
        Gain = _fadeFrom + (_fadeTarget - _fadeFrom) * fraction;
        return ScpCombatMusicPlaybackAction.None;
    }

    /// <summary>
    /// Clears playback history when the local body, round or music setting changes.
    /// </summary>
    public void Reset()
    {
        _combatMode = false;
        ClearTrack();
    }

    private void FadeTo(float target, TimeSpan now, TimeSpan duration)
    {
        _fadeFrom = Gain;
        _fadeTarget = target;
        _fadeStarted = now;
        _fadeDuration = duration;
    }

    private void ClearTrack()
    {
        _hasTrack = false;
        _retainUntil = null;
        Gain = 0f;
    }
}

public enum ScpCombatMusicPlaybackAction : byte
{
    None,

    /// <summary>
    /// Replace any previous stream with a new track at zero initial gain.
    /// </summary>
    Start,

    Stop,
}
