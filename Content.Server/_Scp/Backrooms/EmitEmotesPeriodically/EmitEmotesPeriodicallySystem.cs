using Content.Server.Chat.Systems;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Backrooms.EmitEmotesPeriodically;

public sealed partial class EmitEmotesPeriodicallySystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<EmitEmotesPeriodicallyComponent>();

        while (query.MoveNext(out var uid, out var component))
        {
            if (_timing.CurTime - component.LastTimeEmit < component.Cooldown + component.CooldownAddition)
                continue;

            EmitEmote(uid, component.Emotes, component.Mode);

            component.LastTimeEmit = _timing.CurTime;
            component.CooldownAddition = TimeSpan.FromSeconds(_random.Next(-component.CooldownVariations, component.CooldownVariations));
        }

    }

    private void EmitEmote(EntityUid uid, HashSet<ProtoId<EmotePrototype>> emotes, EmitMode mode)
    {
        switch (mode)
        {
            case EmitMode.All:
                foreach (var emote in emotes)
                {
                    _chat.TryEmoteWithChat(uid, emote);
                }

                break;

            case EmitMode.Random:
                var chosenEmote = _random.Pick(emotes);

                _chat.TryEmoteWithChat(uid, chosenEmote);

                break;
        }
    }
}
