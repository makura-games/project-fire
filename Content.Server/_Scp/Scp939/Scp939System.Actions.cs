using Content.Server.Chat.Systems;
using Content.Server.Examine;
using Content.Shared._Scp.Helpers;
using Content.Shared._Scp.Scp939;
using Content.Shared._Scp.ScpMask;
using Content.Shared._Sunrise.TTS;
using Content.Shared.Bed.Sleep;
using Content.Shared.Chat;
using Content.Shared.Coordinates.Helpers;
using Content.Shared.IdentityManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Scp.Scp939;

public sealed partial class Scp939System
{
    [Dependency] private ExamineSystem _examine = default!;
    [Dependency] private EntityLookupSystem _entityLookup = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private ScpMaskSystem _scpMask = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly EntProtoId StatusEffect = "StatusEffectForcedSleeping";

    private void InitializeActions()
    {
        SubscribeLocalEvent<Scp939Component, Scp939SleepAction>(OnSleepAction);
        SubscribeLocalEvent<Scp939Component, Scp939GasAction>(OnGasAction);
        SubscribeLocalEvent<Scp939Component, Scp939MimicActionEvent>(OnMimic);
    }

    private void OnSleepAction(Entity<Scp939Component> ent, ref Scp939SleepAction args)
    {
        args.Handled = TrySleep(ent);
    }

    public bool TrySleep(Entity<Scp939Component> ent, float hibernationDuration = 0)
    {
        if (HasComp<SleepingComponent>(ent))
            return false;

        if (!_sleepingSystem.TrySleeping(ent.Owner))
            return false;

        hibernationDuration = hibernationDuration == 0 ? ent.Comp.HibernationDuration : hibernationDuration;
        _statusEffects.TryAddStatusEffectDuration(ent, StatusEffect, TimeSpan.FromSeconds(hibernationDuration));

        return true;
    }

    private void OnGasAction(Entity<Scp939Component> ent, ref Scp939GasAction args)
    {
        if (_scpMask.TryGetScpMask(ent, out var scpMask))
        {
            _scpMask.TryCreatePopup(ent, scpMask);
            return;
        }

        var xform = Transform(ent);
        var smokeEntity = Spawn(ent.Comp.SmokeProtoId, xform.Coordinates.SnapToGrid());

        _smokeSystem.StartSmoke(smokeEntity, ent.Comp.SmokeSolution, ent.Comp.SmokeDuration, ent.Comp.SmokeSpreadRadius);

        args.Handled = true;
    }

    private void OnMimic(Entity<Scp939Component> ent, ref Scp939MimicActionEvent args)
    {
        if (ent.Comp.RememberedMessages.Count == 0)
            return;

        var messagePair = _random.Pick(ent.Comp.RememberedMessages);

        if (TryComp<TTSComponent>(ent, out var ttsComponent))
        {
            ttsComponent.VoicePrototypeId = messagePair.Value.Value;
            Dirty(ent, ttsComponent);
        }

        _chat.TrySendInGameICMessage(ent,
            messagePair.Key,
            InGameICChatType.Speak,
            ChatTransmitRange.Normal,
            nameOverride: messagePair.Value.Key,
            ignoreActionBlocker: true);

        args.Handled = true;
    }

    /// <summary>
    /// Запоминание последних сказанных возле 939 слов
    /// </summary>
    private void TryRememberPhrase(Entity<ActiveScp939VisibilityComponent> ent, string message)
    {
        using var scp939Set = HashSetPoolEntity<Scp939Component>.Rent();
        _entityLookup.GetEntitiesInRange(Transform(ent).Coordinates, 16f, scp939Set.Value, LookupFlags.Dynamic | LookupFlags.Approximate);
        if (scp939Set.Value.Count == 0)
            return;

        string? voicePrototype = null;

        if (TryComp<TTSComponent>(ent, out var ttsComponent))
            voicePrototype = ttsComponent.VoicePrototypeId;

        foreach (var scp in scp939Set.Value)
        {
            if (!_examine.InRangeUnOccluded(ent, scp))
                continue;

            if (scp.Comp.RememberedMessages.Count >= scp.Comp.MaxRememberedMessages)
            {
                var randomKey = _random.Pick(scp.Comp.RememberedMessages.Keys);
                scp.Comp.RememberedMessages.Remove(randomKey);
            }

            var username = Identity.Name(ent, EntityManager);
            scp.Comp.RememberedMessages.TryAdd(message, new(username, voicePrototype));
        }
    }
}
