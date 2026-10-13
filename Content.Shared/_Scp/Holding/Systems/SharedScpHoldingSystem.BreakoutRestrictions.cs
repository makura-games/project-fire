using Content.Shared._Scp.Holding.Components;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;

namespace Content.Shared._Scp.Holding.Systems;

public abstract partial class SharedScpHoldingSystem
{
    [Dependency] private SharedCuffableSystem _cuffable = default!;

    private bool IsBreakoutBlockedByCuffs(EntityUid heldUid)
    {
        return TryComp<CuffableComponent>(heldUid, out var cuffable) && !cuffable.CanStillInteract;
    }

    private bool TryRedirectBreakoutAlertToUncuff(Entity<ActiveScpHoldableComponent> held, EntityUid user)
    {
        if (!TryComp<CuffableComponent>(held, out var cuffable) || cuffable.CanStillInteract)
            return false;

        _cuffable.TryUncuff((held.Owner, cuffable), user);
        return true;
    }
}
