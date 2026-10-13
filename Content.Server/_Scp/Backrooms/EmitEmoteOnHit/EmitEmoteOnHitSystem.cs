using Content.Server.Chat.Systems;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Scp.Backrooms.EmitEmoteOnHit;

public sealed partial class EmitEmoteOnHitSystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmitEmoteOnHitComponent, MeleeHitEvent>(OnHit);
    }

    private void OnHit(Entity<EmitEmoteOnHitComponent> entity, ref MeleeHitEvent args)
    {
        if (!args.IsHit)
            return;

        if (args.HitEntities.Count == 0)
            return;

        var user = args.User;

        _chat.TryEmoteWithChat(user, entity.Comp.Emote);
    }
}
