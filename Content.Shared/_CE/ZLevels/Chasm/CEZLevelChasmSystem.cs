/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.ActionBlocker;
using Content.Shared.Chasm;
using Robust.Shared.Analyzers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._CE.ZLevels.Chasm;

public sealed partial class CEZLevelChasmSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;

    private static readonly SoundPathSpecifier FallingSound = new("/Audio/Effects/falling.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CEZLevelChasmComponent, CEZLevelFallOutOfBoundsEvent>(OnFallOutOfBounds);
    }

    private void OnFallOutOfBounds(Entity<CEZLevelChasmComponent> ent, ref CEZLevelFallOutOfBoundsEvent args)
    {
        var player = args.Player;

        if (HasComp<ChasmFallingComponent>(player))
            return; //Already falling

        var attempt = new CEZLevelChasmAttempt(player);
        RaiseLocalEvent(player, attempt);

        if (attempt.Cancelled)
            return;

        _audio.PlayPredicted(FallingSound, Transform(player).Coordinates, player);
        var falling = AddComp<ChasmFallingComponent>(player);
        falling.NextDeletionTime = _timing.CurTime + falling.DeletionTime;
        _blocker.UpdateCanMove(player);
    }
}
