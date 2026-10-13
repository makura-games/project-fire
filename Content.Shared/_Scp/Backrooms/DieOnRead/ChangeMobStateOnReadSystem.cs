using Content.Shared.Mobs.Systems;

namespace Content.Shared._Scp.Backrooms.DieOnRead;

public sealed partial class ChangeMobStateOnReadSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChangeMobStateOnReadComponent, BoundUIOpenedEvent>(OnRead);
    }

    private void OnRead(Entity<ChangeMobStateOnReadComponent> book, ref BoundUIOpenedEvent args)
    {
        var reader = args.Actor;

        _mobState.ChangeMobState(reader, book.Comp.State, origin: book);
    }
}
