using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Fear.Components;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._Scp.Fear;

public sealed partial class FearSystem
{
    [Dependency] private AudioSystem _audio = default!;

    private static readonly SoundSpecifier FearIncreaseSound =
        new SoundPathSpecifier("/Audio/_Scp/Effects/Fear/increase.ogg", AudioParams.Default.WithVolume(5f));
    private static readonly SoundSpecifier FearDecreaseSound =
        new SoundPathSpecifier("/Audio/_Scp/Effects/Fear/decrease.ogg", AudioParams.Default.WithVolume(-1f));

    private static readonly SoundSpecifier BreathingSound =
        new SoundPathSpecifier("/Audio/_Scp/Effects/Fear/breathing.ogg");

    private void InitializeSoundEffects()
    {
        SubscribeLocalEvent<FearActiveSoundEffectsComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<FearActiveSoundEffectsComponent, PlayerDetachedEvent>(OnPlayerDetached);

        SubscribeLocalEvent<FearActiveSoundEffectsComponent, ComponentShutdown>(OnShutdown);
    }

    #region Events

    private void OnShutdown(Entity<FearActiveSoundEffectsComponent> ent, ref ComponentShutdown args)
    {
        StopBreathing(ent);
    }

    private void OnPlayerAttached(Entity<FearActiveSoundEffectsComponent> ent, ref PlayerAttachedEvent args)
    {
        StartBreathing(ent);
    }

    private void OnPlayerDetached(Entity<FearActiveSoundEffectsComponent> ent, ref PlayerDetachedEvent args)
    {
        StopBreathing(ent);
    }

    #endregion

    /// <summary>
    /// Проигрывает специфический звук в зависимости от установленного уровня страха.
    /// Для повышения и понижения уровня звуки разные.
    /// </summary>
    protected override void PlayFearStateSound(Entity<FearComponent> ent, FearState oldState)
    {
        base.PlayFearStateSound(ent, oldState);

        // Выбираем звук. Если уровень страха повысился, то проигрываем звук увеличения и наоборот.
        var sound = ent.Comp.State > oldState ? FearIncreaseSound : FearDecreaseSound;
        _audio.PlayGlobal(sound, ent);
    }

    /// <summary>
    /// Создает эффект дыхания, слышимый только пугающемуся игроку
    /// </summary>
    protected override void StartBreathing(Entity<FearActiveSoundEffectsComponent> ent)
    {
        base.StartBreathing(ent);

        if (!ent.Comp.PlayBreathingSound)
            return;

        if (ent.Comp.BreathingAudioStream.HasValue)
            return;

        var audioParams = AudioParams.Default
            .AddVolume(ent.Comp.AdditionalVolume)
            .WithLoop(true);

        var audio = _audio.PlayGlobal(BreathingSound, ent, audioParams);
        ent.Comp.BreathingAudioStream = audio?.Entity;
        DirtyField(ent, ent.Comp, nameof(FearActiveSoundEffectsComponent.BreathingAudioStream));
    }

    protected override void StopBreathing(Entity<FearActiveSoundEffectsComponent> ent)
    {
        ent.Comp.BreathingAudioStream = _audio.Stop(ent.Comp.BreathingAudioStream);
        DirtyField(ent, ent.Comp, nameof(FearActiveSoundEffectsComponent.BreathingAudioStream));
    }
}
