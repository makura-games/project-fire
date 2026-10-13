using Content.Server.Chat.Systems;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Proximity;
using Content.Shared._Scp.Scp173;
using Content.Shared.Chat;
using Robust.Shared.Random;

namespace Content.Server._Scp.Other.SpeakOnEyeStateChange;

/// <summary>
/// Система, реализующая автоматическое проигрывание фразы при изменении состояния глаз.
/// Персонаж игрока будет выдавать заданные фразы при закрытии и открытии глаз.
/// </summary>
public sealed partial class SpeakOnEyeStateChangeSystem : EntitySystem
{
    [Dependency] private ProximitySystem _proximity = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SpeakOnEyeStateChangeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SpeakOnEyeStateChangeComponent, EntityClosedEyesEvent>(OnClosedEyes);
        SubscribeLocalEvent<SpeakOnEyeStateChangeComponent, EntityOpenedEyesEvent>(OnOpenedEyes);
    }

    private void OnMapInit(Entity<SpeakOnEyeStateChangeComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.PhraseOnClose != null && ent.Comp.PhraseOnClose.Count != 0)
            ent.Comp.FavoriteClosePhrase = _random.Pick(ent.Comp.PhraseOnClose);

        if (ent.Comp.PhraseOnOpen != null && ent.Comp.PhraseOnOpen.Count != 0)
            ent.Comp.FavoriteOpenPhrase = _random.Pick(ent.Comp.PhraseOnOpen);
    }

    private void OnClosedEyes(Entity<SpeakOnEyeStateChangeComponent> ent, ref EntityClosedEyesEvent args)
    {
        if (ent.Comp.PhraseOnClose == null || ent.Comp.PhraseOnClose.Count == 0)
            return;

        if (!ShouldSpeak(ent, args.Mode))
            return;

        var message = _random.Prob(ent.Comp.FavoriteChance) ? ent.Comp.FavoriteClosePhrase : _random.Pick(ent.Comp.PhraseOnClose);
        _chat.TrySendInGameICMessage(ent, Loc.GetString(message, ("entity", ent.Owner)), InGameICChatType.Speak, ChatTransmitRange.Normal);
    }

    private void OnOpenedEyes(Entity<SpeakOnEyeStateChangeComponent> ent, ref EntityOpenedEyesEvent args)
    {
        if (ent.Comp.PhraseOnOpen == null || ent.Comp.PhraseOnOpen.Count == 0)
            return;

        if (!ShouldSpeak(ent, args.Mode))
            return;

        var message = _random.Prob(ent.Comp.FavoriteChance) ? ent.Comp.FavoriteOpenPhrase : _random.Pick(ent.Comp.PhraseOnOpen);
        _chat.TrySendInGameICMessage(ent, Loc.GetString(message, ("entity", ent.Owner)), InGameICChatType.Speak, ChatTransmitRange.Normal);
    }

    /// <summary>
    /// Должен ли персонаж выдать фразу в соответствии с параметрами в компоненте?
    /// </summary>
    private bool ShouldSpeak(Entity<SpeakOnEyeStateChangeComponent> ent, EyeCloseReason mode)
    {
        if (mode != EyeCloseReason.Action &&
            (mode != EyeCloseReason.Blink || !ent.Comp.SpeakOnAutomaticBlinking))
            return false;

        if (ent.Comp.SpeakOnlyInScp173Chamber && !_proximity.IsNearby<Scp173BlockStructureDamageComponent>(ent, SharedScp173System.ContainmentRoomSearchRadius))
            return false;

        if (ent.Comp.SpeakOnlyWhileScp173Nearby && !_proximity.IsNearby<Scp173Component>(ent, SharedScp173System.ContainmentRoomSearchRadius))
            return false;

        return true;
    }
}
