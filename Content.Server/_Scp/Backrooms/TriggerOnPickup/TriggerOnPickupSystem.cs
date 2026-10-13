using Content.Shared.Item;
using Content.Shared.Trigger.Systems;

namespace Content.Server._Scp.Backrooms.TriggerOnPickup;

public sealed partial class TriggerOnPickupSystem : EntitySystem
{
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TriggerOnPickupComponent, GettingPickedUpAttemptEvent>(OnPickUp);
    }

    private void OnPickUp(Entity<TriggerOnPickupComponent> entity, ref GettingPickedUpAttemptEvent args)
    {
        _trigger.Trigger(entity, args.User);
    }
}
