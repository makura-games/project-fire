using Content.Shared.Destructible;
using Content.Shared.Light.Components;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared.Light.EntitySystems;

public abstract partial class SharedLightBulbSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    // Fire added start
    [Dependency] private TagSystem _tag = default!;
    private static readonly ProtoId<TagPrototype> SavableTag = "MetaGarbageSavable";
    private static readonly ProtoId<TagPrototype> ContainerTag = "MetaGarbageCanBeSpawnedInContainer";
    private static readonly ProtoId<TagPrototype> ReplaceTag = "MetaGarbageReplace";
    // Fire added end

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LightBulbComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LightBulbComponent, LandEvent>(HandleLand);
        SubscribeLocalEvent<LightBulbComponent, BreakageEventArgs>(OnBreak);
    }

    private void OnInit(EntityUid uid, LightBulbComponent bulb, ComponentInit args)
    {
        // update default state of bulbs
        UpdateAppearance(uid, bulb);
    }

    private void HandleLand(EntityUid uid, LightBulbComponent bulb, ref LandEvent args)
    {
        PlayBreakSound(uid, bulb);
        SetState(uid, LightBulbState.Broken, bulb);
    }

    private void OnBreak(EntityUid uid, LightBulbComponent component, BreakageEventArgs args)
    {
        SetState(uid, LightBulbState.Broken, component);
    }

    /// <summary>
    ///     Set a new color for a light bulb and raise event about change
    /// </summary>
    public void SetColor(EntityUid uid, Color color, LightBulbComponent? bulb = null)
    {
        if (!Resolve(uid, ref bulb) || bulb.Color.Equals(color))
            return;

        bulb.Color = color;
        Dirty(uid, bulb);
        UpdateAppearance(uid, bulb);
    }

    /// <summary>
    ///     Set a new state for a light bulb (broken, burned) and raise event about change
    /// </summary>
    public void SetState(EntityUid uid, LightBulbState state, LightBulbComponent? bulb = null)
    {
        if (!Resolve(uid, ref bulb) || bulb.State == state)
            return;

        // Fire added start - для сохранения между раундами битых лампочек
        if (state != LightBulbState.Normal)
            _tag.AddTags(uid, ReplaceTag, ContainerTag, SavableTag);
        else
            _tag.RemoveTags(uid, ReplaceTag, ContainerTag, SavableTag);
        // Fire added end

        bulb.State = state;
        Dirty(uid, bulb);
        UpdateAppearance(uid, bulb);
    }

    public void PlayBreakSound(EntityUid uid, LightBulbComponent? bulb = null, EntityUid? user = null)
    {
        if (!Resolve(uid, ref bulb))
            return;

        _audio.PlayPredicted(bulb.BreakSound, uid, user: user);
    }

    private void UpdateAppearance(EntityUid uid, LightBulbComponent? bulb = null,
        AppearanceComponent? appearance = null)
    {
        if (!Resolve(uid, ref bulb, ref appearance, logMissing: false))
            return;

        // try to update appearance and color
        _appearance.SetData(uid, LightBulbVisuals.State, bulb.State, appearance);
        _appearance.SetData(uid, LightBulbVisuals.Color, bulb.Color, appearance);
    }
}
