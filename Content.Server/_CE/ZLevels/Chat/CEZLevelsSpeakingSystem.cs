/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Robust.Shared.Analyzers;
using Content.Server.Chat.Systems;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Content.Shared.Speech;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Server._CE.ZLevels.Chat;

public sealed partial class CEZLevelsSpeakingSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private CESharedZLevelsSystem _zLevel = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<MapComponent> _mapQuery;

    private const float TransmitterLifetime = 3f;
    private const int MessageDelayMilliseconds = 333;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZLevelViewerComponent, EntitySpokeEvent>(OnSpoke);

        _mapQuery = GetEntityQuery<MapComponent>();
    }

    private void OnSpoke(Entity<CEZLevelViewerComponent> ent, ref EntitySpokeEvent args)
    {
        if (args.ObfuscatedMessage is not null) //Curse of chatcode: this is only way detect whispers
            return;

        TransmitToAdjacentZLevels(ent, args.Message, Identity.Name(ent, EntityManager), InGameICChatType.Whisper);
    }

    /// <summary>
    /// Repeats a message one z-level above and below the source, at the same world position.
    /// </summary>
    /// <param name="speechVerb">If set, the repeated message uses this verb for every suffix.</param>
    public void TransmitToAdjacentZLevels(
        EntityUid source,
        string message,
        string name,
        InGameICChatType chatType,
        ProtoId<SpeechVerbPrototype>? speechVerb = null)
    {
        var xform = Transform(source);
        var sourceMap = xform.MapUid;
        if (sourceMap is null)
            return;

        var globalPosition = _transform.GetWorldPosition(xform);

        //Try transmit message to 1 zlevel down
        if (_zLevel.TryMapDown(sourceMap.Value, out var belowMapUid) &&
            _mapQuery.TryComp(belowMapUid, out var belowMapComp))
        {
            TransmitMessageToZLevel(
                belowMapComp,
                globalPosition,
                message,
                Loc.GetString("ce-zlevel-voice-from-up", ("name", name)),
                chatType,
                speechVerb);
        }

        //Try transmit message to 1 zlevel up
        if (_zLevel.TryMapUp(sourceMap.Value, out var aboveMapUid) &&
            _mapQuery.TryComp(aboveMapUid, out var aboveMapComp))
        {
            TransmitMessageToZLevel(
                aboveMapComp,
                globalPosition,
                message,
                Loc.GetString("ce-zlevel-voice-from-down", ("name", name)),
                chatType,
                speechVerb);
        }
    }

    private void TransmitMessageToZLevel(
        MapComponent mapComp,
        Vector2 position,
        string message,
        string nameOverride,
        InGameICChatType chatType,
        ProtoId<SpeechVerbPrototype>? speechVerb)
    {
        var targetPos = new MapCoordinates(position, mapComp.MapId);
        var transmit = Spawn(null, targetPos);
        EnsureComp<TimedDespawnComponent>(transmit).Lifetime = TransmitterLifetime;
        EnsureComp<CEZLevelSpeechTransmitterComponent>(transmit);

        if (speechVerb is not null)
        {
            var speech = EnsureComp<SpeechComponent>(transmit);
            speech.SpeechVerb = speechVerb.Value;
            speech.SuffixSpeechVerbs.Clear();
        }

        //It's not the most elegant solution, but as far as I understand, the entity doesn't have time to enter
        //the client's PVS after spawning, and we already start communicating through it. A slight delay solves the problem.
        Timer.Spawn(MessageDelayMilliseconds,
            () =>
            {
                _chat.TrySendInGameICMessage(
                    transmit,
                    message,
                    chatType,
                    false,
                    nameOverride: nameOverride,
                    ignoreActionBlocker: true);
            });
    }
}
