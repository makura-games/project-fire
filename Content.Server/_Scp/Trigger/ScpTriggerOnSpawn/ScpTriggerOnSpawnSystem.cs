using Content.Server.GameTicking;
using Content.Shared.Trigger.Systems;

namespace Content.Server._Scp.Trigger.ScpTriggerOnSpawn;

public sealed partial class ScpTriggerOnSpawnSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ScpTriggerOnSpawnComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ScpTriggerOnSpawnComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.RequiredGameRunLevel != null && _gameTicker.RunLevel != ent.Comp.RequiredGameRunLevel)
            return;

        _trigger.Trigger(ent, key: ent.Comp.KeyOut);
    }
}
