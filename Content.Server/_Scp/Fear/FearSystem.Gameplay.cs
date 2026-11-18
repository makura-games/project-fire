using Content.Server.Chat.Systems;
using Content.Shared._Scp.Fear.Components;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Movement.Events;
using Content.Shared.Standing;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Scp.Fear;

public sealed partial class FearSystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IRobustRandom _random = default!;

    private static readonly ProtoId<EmotePrototype> ScreamProtoId = "Scream";

    /// <summary>
    /// Пытается закричать, если увиденный объект настолько страшный.
    /// </summary>
    protected override void TryScream(Entity<FearComponent> ent)
    {
        base.TryScream(ent);

        if (ent.Comp.State < ent.Comp.ScreamRequiredState)
            return;

        if (ent.Comp.EffectStrength < 1f && !_random.Prob(ent.Comp.EffectStrength))
            return;

        _chat.TryEmoteWithChat(ent, ScreamProtoId);
    }
}
