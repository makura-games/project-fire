using Content.Server.Actions;
using Content.Shared._Scp.Blinking;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

namespace Content.Server._Scp.Blinking;

public sealed partial class BlinkingSystem : SharedBlinkingSystem
{
    [Dependency] private ActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BlinkableComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BlinkableComponent, PlayerSpawnCompleteEvent>(OnPlayerSpawn);
    }

    private void OnMapInit(Entity<BlinkableComponent> ent, ref MapInitEvent _)
    {
        _actions.AddAction(ent, ref ent.Comp.EyeToggleActionEntity, ent.Comp.EyeToggleAction);
        _actions.SetUseDelay(ent.Comp.EyeToggleActionEntity, ent.Comp.BlinkingDuration);
        Dirty(ent);

        ResetBlink(ent.AsNullable());
    }

    private void OnPlayerSpawn(Entity<BlinkableComponent> ent, ref PlayerSpawnCompleteEvent args)
    {
        RaiseNetworkEvent(new PlayerOpenEyesAnimation(GetNetEntity(ent)), Filter.SinglePlayer(args.Player));
    }
}
