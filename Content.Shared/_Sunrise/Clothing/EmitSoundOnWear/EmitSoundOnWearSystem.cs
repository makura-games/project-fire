using Content.Shared._Sunrise.Clothing.Components;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Gravity;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Sunrise.Clothing.EntitySystems;

public sealed partial class EmitSoundWhileWornSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmitSoundWhileWornComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<EmitSoundWhileWornComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<EmitSoundWhileWornComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<MobStateChangedEvent>(OnMobStateChanged);

        // 🔑 Реагируем на приспускание/поднятие маски (ItemMaskToggledEvent
        // поднимается на самой сущности маски из MaskSystem.ToggleMask).
        SubscribeLocalEvent<EmitSoundWhileWornComponent, ItemMaskToggledEvent>(OnMaskToggled);
    }

    private void OnEquipped(EntityUid uid, EmitSoundWhileWornComponent comp, GotEquippedEvent args)
    {
        comp.Wearer = args.EquipTarget;
        comp.IsValidSlot = !(comp.IgnorePockets && args.SlotFlags.HasFlag(SlotFlags.POCKET));
        UpdateState(uid, comp);
    }

    private void OnUnequipped(EntityUid uid, EmitSoundWhileWornComponent comp, GotUnequippedEvent args)
    {
        comp.Wearer = null;
        Stop(comp);
    }

    private void OnShutdown(EntityUid uid, EmitSoundWhileWornComponent comp, ComponentShutdown args)
    {
        Stop(comp);
    }

    private void OnMobStateChanged(MobStateChangedEvent args)
    {
        var query = EntityQueryEnumerator<EmitSoundWhileWornComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Wearer == args.Target)
                UpdateState(uid, comp);
        }
    }

    /// <summary>
    /// Маску приспустили или подняли — пересчитываем состояние звука.
    /// Источник истины — авторитетное <see cref="MaskComponent.IsToggled"/>.
    /// </summary>
    private void OnMaskToggled(EntityUid uid, EmitSoundWhileWornComponent comp, ItemMaskToggledEvent args)
    {
        UpdateState(uid, comp);
    }

    private void UpdateState(EntityUid uid, EmitSoundWhileWornComponent comp)
    {
        // Сначала принудительно останавливаем и удаляем старый звук,
        // чтобы исключить любую возможность дублирования.
        Stop(comp);

        if (ShouldPlay(uid, comp))
            StartSound(comp);
    }

    private bool ShouldPlay(EntityUid uid, EmitSoundWhileWornComponent comp)
    {
        if (!comp.IsValidSlot || comp.Wearer is not { Valid: true } wearer)
            return false;

        // 🔑 Если маска приспущена — звук дыхания не играет.
        // Проверяем ДО остальных условий, чтобы никакая маска в состоянии
        // IsToggled == true не могла «просочиться» через другие флаги.
        if (TryComp<MaskComponent>(uid, out var mask) && mask.IsToggled)
            return false;

        if (comp.RequiresGravity && _gravity.IsWeightless(wearer))
            return false;

        if (comp.RequiresAlive)
        {
            if (!TryComp<MobStateComponent>(wearer, out var mobState) ||
                mobState.CurrentState != MobState.Alive)
                return false;
        }

        return true;
    }

    private void StartSound(EmitSoundWhileWornComponent comp)
    {
        if (comp.Wearer is not { Valid: true } wearer)
            return;

        var loopParams = comp.Sound.Params.WithLoop(true);

        var audioResult = _audio.PlayPvs(comp.Sound, wearer, loopParams);
        if (audioResult is null)
        {
            Log.Error($"[EmitSoundWhileWorn] PlayPvs вернул null для {comp.Sound}");
            return;
        }

        var audioUid = audioResult.Value.Entity;
        if (!audioUid.Valid)
            return;

        comp.SoundEntity = audioUid;

        // Страховка: если по какой-то причине звук уже играет
        // (например, из-за гонки при PVS) — принудительно перезапускаем.
        if (TryComp<AudioComponent>(audioUid, out var audioComp) &&
            audioComp.State == AudioState.Playing)
        {
            _audio.Stop(audioUid);
        }
    }

    private void Stop(EmitSoundWhileWornComponent comp)
    {
        if (comp.SoundEntity is not { } audioUid)
            return;

        comp.SoundEntity = null;

        if (Exists(audioUid))
        {
            _audio.Stop(audioUid);
            QueueDel(audioUid);
        }
    }
}
