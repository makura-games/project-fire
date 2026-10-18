/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Robust.Shared.Analyzers;
using Content.Client._CE.ZLevels.Core.Overlays;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.Camera;
using Content.Shared.StatusEffectNew.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Client._CE.ZLevels.Core;

/// <summary>
/// Only process Eye offset and drawdepth on clientside
/// </summary>
public sealed partial class CEClientZLevelsSystem : CESharedZLevelsSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IEyeManager _eye = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CEZPhysicsComponent, GetEyeOffsetEvent>(OnEyeOffset);
        SubscribeLocalEvent<CEZPhysicsComponent, ComponentStartup>(OnStartup);
        _overlay.AddOverlay(new CEZLevelBlurOverlay());
        _overlay.AddOverlay(new CEZLevelFogOverlay());
    }

    private void OnEyeOffset(Entity<CEZPhysicsComponent> ent, ref GetEyeOffsetEvent args)
    {
        Angle rotation = _eye.CurrentEye.Rotation * -1;
        var localPosition = GetVisualLocalPosition(ent, ent.Comp, Transform(ent), ZPhysicsQuery);
        var offset = rotation.RotateVec(new Vector2(0, localPosition * ZLevelOffset));
        args.Offset += offset;
    }

    /// <summary>
    /// Entities riding/parented onto another Z-physics body (e.g. a rider buckled to a flying vehicle)
    /// never step their own LocalPosition, so they must visually follow the parent's height instead.
    /// </summary>
    internal static float GetVisualLocalPosition(EntityUid uid, CEZPhysicsComponent zPhys, TransformComponent xform, EntityQuery<CEZPhysicsComponent> zPhysicsQuery)
    {
        if (xform.ParentUid != xform.MapUid && zPhysicsQuery.TryComp(xform.ParentUid, out var parentZPhys))
            return parentZPhys.LocalPosition;

        return zPhys.LocalPosition;
    }

    private void OnStartup(Entity<CEZPhysicsComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (sprite.SnapCardinals)
            return;

        ent.Comp.DrawDepthDefault = sprite.DrawDepth;
        ent.Comp.SpriteOffsetDefault = sprite.Offset;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlay.RemoveOverlay<CEZLevelBlurOverlay>();
        _overlay.RemoveOverlay<CEZLevelFogOverlay>();
    }
}

/// <summary>
/// Pre-animation pass for Z-level visuals.
/// Runs its <see cref="FrameUpdate"/> BEFORE <see cref="AnimationPlayerSystem"/> every render frame,
/// resetting <see cref="SpriteComponent.Offset"/> to the entity's clean base (no Z).
/// This prevents Z from accumulating across frames when no animation writes to the offset
/// (e.g. entities that only animate scale, like SlimeIceBig).
/// </summary>
internal sealed partial class CEClientZLevelsPreAnimSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<CEZPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    /// <summary>
    /// Per-frame scratch, rebuilt every <see cref="FrameUpdate"/>: entities whose Z visuals need touching this frame.
    /// Shared with <see cref="CEClientZLevelsPostAnimSystem"/> so both passes work on the same small set.
    /// </summary>
    internal readonly HashSet<EntityUid> ZVisualEntities = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        // Most Z-physics entities rest at Z = 0 and need no visual changes, so only collect the ones that have
        // height, still carry Z visuals from last frame (to reset them once), or ride on a body that has height.
        ZVisualEntities.Clear();

        var candidates = EntityQueryEnumerator<CEZPhysicsComponent>();
        while (candidates.MoveNext(out var uid, out var zPhys))
        {
            if (zPhys.LocalPosition == 0f && !zPhys.VisualZApplied)
                continue;

            ZVisualEntities.Add(uid);

            if (zPhys.LocalPosition == 0f || !_xformQuery.TryComp(uid, out var bodyXform) || bodyXform.ChildCount == 0)
                continue;

            // Riders never step their own LocalPosition, they follow this body's height, see GetVisualLocalPosition.
            var children = bodyXform.ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (_zPhysQuery.HasComp(child))
                    ZVisualEntities.Add(child);
            }
        }

        // Phase 1 (per render frame): strip any Z left from last frame so the animation player
        // always starts from a Z-free base, and Phase 2 can add exactly one Z contribution.
        foreach (var uid in ZVisualEntities)
        {
            if (!_zPhysQuery.TryComp(uid, out var zPhys) ||
                !_spriteQuery.TryComp(uid, out var sprite) ||
                !_xformQuery.TryComp(uid, out var xform))
                continue;

            var localPosition = CEClientZLevelsSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);
            _sprite.SetOffset((uid, sprite), zPhys.SpriteOffsetDefault);
            _sprite.SetDrawDepth((uid, sprite), localPosition > 0 ? (int)Shared.DrawDepth.DrawDepth.OverMobs : zPhys.DrawDepthDefault);
            zPhys.VisualZApplied = localPosition != 0f;
        }

        // Set parent-synced status effect offsets to the parent's current Z value each frame — prevents accumulation.
        var syncQuery = EntityQueryEnumerator<StatusEffectComponent, SpriteComponent, TransformComponent>();
        while (syncQuery.MoveNext(out var uid, out _, out var sprite, out var xform))
        {
            var parent = xform.ParentUid;
            if (_mapGridQuery.HasComp(parent))
                continue;
            if (!_zPhysQuery.TryComp(parent, out var parentZPhys))
                continue;
            var zOffset = new Vector2(0, parentZPhys.LocalPosition * CESharedZLevelsSystem.ZLevelOffset);
            _sprite.SetOffset((uid, sprite), zOffset);
        }
    }
}

/// <summary>
/// Post-animation pass for Z-level visuals.
/// Runs its <see cref="FrameUpdate"/> AFTER <see cref="AnimationPlayerSystem"/> so that
/// whatever offset the animation player wrote to <see cref="SpriteComponent.Offset"/> this frame
/// (loop animation, one-shot swing, idle bob, etc.) is preserved, and the Z-height contribution
/// is simply added on top.  No animation code needs to know about Z levels.
/// </summary>
internal sealed partial class CEClientZLevelsPostAnimSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private CEClientZLevelsPreAnimSystem _preAnim = default!;
    [Dependency] private EntityQuery<CEZPhysicsComponent> _zPhysQuery = default!;
    [Dependency] private EntityQuery<SpriteComponent> _spriteQuery = default!;
    [Dependency] private EntityQuery<TransformComponent> _xformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AnimationPlayerSystem));
    }

    public override void FrameUpdate(float frameTime)
    {
        // Phase 2: add the Z-height contribution on top of the animation-player's output.
        // At this point sprite.Offset == animationValue (or SpriteOffsetDefault if no anim ran).
        // The offset is counter-rotated by the entity's world angle so it always points world-up,
        // preventing it from orbiting the pivot when the entity has angular velocity (e.g. shurikens).
        // Only the entities collected by the pre-anim pass can have a non-zero Z this frame.
        foreach (var uid in _preAnim.ZVisualEntities)
        {
            if (!_zPhysQuery.TryComp(uid, out var zPhys) ||
                !_spriteQuery.TryComp(uid, out var sprite) ||
                !_xformQuery.TryComp(uid, out var xform))
                continue;

            var localPosition = CEClientZLevelsSystem.GetVisualLocalPosition(uid, zPhys, xform, _zPhysQuery);

            if (localPosition == 0f)
                continue;

            var rawZ = new Vector2(0, localPosition * CESharedZLevelsSystem.ZLevelOffset);
            Vector2 zOffset;
            if (sprite.NoRotation)
                zOffset = rawZ;
            else
                zOffset = new Angle(-_xform.GetWorldRotation(xform)).RotateVec(rawZ);
            _sprite.SetOffset((uid, sprite), sprite.Offset + zOffset);
        }
    }
}
