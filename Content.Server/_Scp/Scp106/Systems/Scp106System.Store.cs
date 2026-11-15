using Content.Shared._Scp.Scp106;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Store.Components;

namespace Content.Server._Scp.Scp106.Systems;

public sealed partial class Scp106System
{
    private void InitializeStore()
    {
        SubscribeLocalEvent<Scp106Component, Scp106ShopAction>(OnShop);
    }

    private void OnShop(Entity<Scp106Component> ent, ref Scp106ShopAction args)
    {
        if (!TryComp<StoreComponent>(ent, out var store))
            return;

        _store.ToggleUi(ent, ent, store);
    }

    private void AddCurrencyInStore(EntityUid uid)
    {
        _store.TryAddCurrency(new() { { BackroomsCurrencyPrototype, BackroomsEssenceRate } }, uid);
    }
}
