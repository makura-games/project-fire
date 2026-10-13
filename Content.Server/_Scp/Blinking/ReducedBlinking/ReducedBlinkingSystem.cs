using Content.Server.Popups;
using Content.Shared._Scp.Blinking;
using Content.Shared._Scp.Blinking.ReducedBlinking;

namespace Content.Server._Scp.Blinking.ReducedBlinking;

public sealed partial class ReducedBlinkingSystem : SharedReducedBlinkingSystem
{
    [Dependency] private SharedBlinkingSystem _blinking = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveReducedBlinkingUserComponent, ComponentStartup>(OnUserStartup);
        SubscribeLocalEvent<ActiveReducedBlinkingUserComponent, ComponentShutdown>(OnUserShutdown);
    }

    #region Update

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ActiveReducedBlinkingUserComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            UpdateFirstBonusEnd(uid, active);
            UpdateAllBonusEnd(uid, active);
        }
    }

    private void UpdateFirstBonusEnd(EntityUid uid, ActiveReducedBlinkingUserComponent component)
    {
        if (Timing.CurTime < component.FirstBonusEndTime)
            return;

        if (component.FirstBonusEndPopupShowed)
            return;

        _popup.PopupEntity(Loc.GetString("eye-droplets-first-bonus-end"), uid, uid);
        component.FirstBonusEndPopupShowed = true;
    }

    private void UpdateAllBonusEnd(EntityUid uid, ActiveReducedBlinkingUserComponent component)
    {
        if (Timing.CurTime < component.AllBonusEndTime)
            return;

        RemCompDeferred<ActiveReducedBlinkingUserComponent>(uid);
    }

    #endregion

    #region Events

    private void OnUserStartup(Entity<ActiveReducedBlinkingUserComponent> ent, ref ComponentStartup _)
    {
        if (!TryComp<BlinkableComponent>(ent, out var blinkable))
            return;

        blinkable.BlinkingIntervalBonus += ent.Comp.BlinkingIntervalBonus;
        DirtyField(ent.Owner, blinkable, nameof(BlinkableComponent.BlinkingIntervalBonus));
    }

    private void OnUserShutdown(Entity<ActiveReducedBlinkingUserComponent> ent, ref ComponentShutdown _)
    {
        if (!TryComp<BlinkableComponent>(ent, out var blinkable))
            return;

        blinkable.BlinkingIntervalBonus -= ent.Comp.BlinkingIntervalBonus;
        if (blinkable.BlinkingIntervalBonus < TimeSpan.Zero)
            blinkable.BlinkingIntervalBonus = TimeSpan.Zero;

        DirtyField(ent.Owner, blinkable, nameof(BlinkableComponent.BlinkingIntervalBonus));
        _blinking.ResetBlink(ent.Owner, predicted: false);

        _popup.PopupEntity(Loc.GetString("eye-droplets-end"), ent, ent);
    }

    #endregion
}
