/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Numerics;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Shared;
using Robust.Shared.Audio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Client._CE.ZLevels.Audio;

public sealed partial class CEZLevelAudioSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private AudioSystem _audioSystem = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedTransformSystem _xformSys = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    [Dependency] private EntityQuery<PhysicsComponent> _physicsQuery = default!;

    private float _maxRayLength;
    private float _perLevelAttenuationDb;
    private float _floorOcclusion;

    public override void Initialize()
    {
        base.Initialize();

        _audioSystem.ProcessStreamOverride += ProcessStreamZAware;
        _audioSystem.GetOcclusionOverride += GetOcclusionZAware;

        Subs.CVar(_cfg, CVars.AudioRaycastLength, value => _maxRayLength = value, true);
        Subs.CVar(_cfg, CCVars.CEZLevelsAudioPerLevelAttenuation, value => _perLevelAttenuationDb = value, true);
        Subs.CVar(_cfg, CCVars.CEZLevelsAudioFloorOcclusion, value => _floorOcclusion = value, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _audioSystem.ProcessStreamOverride -= ProcessStreamZAware;
        _audioSystem.GetOcclusionOverride -= GetOcclusionZAware;
    }

    /// <summary>
    /// Mirrors <c>Robust.Client.Audio.AudioSystem.ProcessStream</c>, extended with cross-Z-level audibility.
    /// This fully replaces the vanilla method (only one subscriber is permitted), so any upstream change to
    /// ProcessStream needs to be mirrored here too.
    /// </summary>
    private void ProcessStreamZAware(EntityUid entity, AudioComponent component, TransformComponent xform, MapCoordinates listener)
    {
        if (!component.Started)
        {
            component.Started = true;
            component.StartPlaying();
        }

        // Global audio (e.g. map-wide ambience) keeps vanilla same-map behaviour.
        if (component.Global)
        {
            if (xform.MapID != MapId.Nullspace && listener.MapId != xform.MapID)
            {
                component.Gain = 0f;
                return;
            }

            component.Volume = component.Params.Volume;
            return;
        }

        var sameMap = listener.MapId == xform.MapID;
        var levelOffset = 0;
        EntityUid entityMapUid = default;
        EntityUid listenerMapUid = default;

        if (!sameMap)
        {
            if (xform.MapUid is not { } eMapUid || !_maps.TryGetMap(listener.MapId, out var lMapUid))
            {
                component.Gain = 0f;
                return;
            }

            entityMapUid = eMapUid;
            listenerMapUid = lMapUid.Value;

            if (!_zLevels.TryGetZLevelOffset(listenerMapUid, entityMapUid, out levelOffset) ||
                !IsWithinRenderedRange(levelOffset))
            {
                component.Gain = 0f;
                return;
            }
        }

        var parentUid = xform.ParentUid;
        Vector2 worldPos;
        component.Volume = component.Params.Volume - MathF.Abs(levelOffset) * _perLevelAttenuationDb;

        // Handle grid audio differently by using grid position.
        if ((component.Flags & AudioFlags.GridAudio) != 0x0)
            worldPos = _maps.GetGridPosition(parentUid);
        else
            worldPos = _xformSys.GetWorldPosition(entity);


        // Max distance check
        var delta = worldPos - listener.Position;
        var distance = delta.Length();

        // Out of range so just clip it for us.
        if (_audioSystem.GetAudioDistance(distance) > component.MaxDistance)
        {
            component.Gain = 0f;
            return;
        }

        if (distance > 0f && distance < 0.01f)
        {
            worldPos = listener.Position;
            delta = Vector2.Zero;
            distance = 0f;
        }

        // Cross-map occlusion can't be raycast (different physics worlds), so instead we check for an
        // opaque tile on the floor/ceiling boundary between the source and the listener.
        if ((component.Flags & AudioFlags.NoOcclusion) == AudioFlags.NoOcclusion)
            component.Occlusion = 0f;
        else if (sameMap)
            component.Occlusion = GetOcclusionZAware(listener, delta, distance, parentUid);
        else
            component.Occlusion = GetCrossLevelOcclusion(levelOffset, worldPos, entityMapUid, listener.Position, listenerMapUid);

        component.Position = worldPos;

        if (_physicsQuery.TryGetComponent(parentUid, out var physicsComp))
            component.Velocity = _physics.GetMapLinearVelocity(parentUid, physicsComp);
    }

    /// <summary>
    /// Mirrors <c>Robust.Client.Audio.AudioSystem.GetOcclusion</c>. Only ever meaningful for same-map
    /// listener/source pairs; cross-Z-level pairs never reach this since ProcessStreamZAware skips occlusion.
    /// </summary>
    private float GetOcclusionZAware(MapCoordinates listener, Vector2 delta, float distance, EntityUid? ignoredEnt = null)
    {
        float occlusion = 0;

        if (distance > 0.1)
        {
            var rayLength = MathF.Min(distance, _maxRayLength);
            var ray = new CollisionRay(listener.Position, delta / distance, _audioSystem.OcclusionCollisionMask);
            occlusion = _physics.IntersectRayPenetration(listener.MapId, ray, rayLength, ignoredEnt);
        }

        return occlusion;
    }

    /// <summary>
    /// Approximates floor/ceiling muffling between Z-levels using the same opaque-tile check the vision
    /// system uses (<see cref="CESharedZLevelsSystem.HasOpaqueAbove"/>), since a real raycast can't cross
    /// maps. Only the boundary nearest the "listening" side is checked, not every level in between.
    /// </summary>
    private float GetCrossLevelOcclusion(int levelOffset, Vector2 entityWorldPos, EntityUid entityMapUid, Vector2 listenerWorldPos, EntityUid listenerMapUid)
    {
        // Source is above us: the relevant boundary is the ceiling directly above the listener.
        // Source is below us: the relevant boundary is the ceiling directly above the source.
        var opaque = levelOffset > 0
            ? _zLevels.HasOpaqueAbove(listenerWorldPos, listenerMapUid)
            : _zLevels.HasOpaqueAbove(entityWorldPos, entityMapUid);

        return opaque ? _floorOcclusion : 0f;
    }

    /// <summary>
    /// Caps audibility to the same range the ghost-eye PVS subscribers use
    /// (<see cref="CESharedZLevelsSystem.MaxZLevelsAboveRendering"/> / <see cref="CESharedZLevelsSystem.MaxZLevelsBelowRendering"/>),
    /// so this never claims a sound is audible further than what's actually replicated to the client.
    /// </summary>
    private static bool IsWithinRenderedRange(int levelOffset)
    {
        return levelOffset switch
        {
            > 0 => levelOffset <= CESharedZLevelsSystem.MaxZLevelsAboveRendering,
            < 0 => -levelOffset <= CESharedZLevelsSystem.MaxZLevelsBelowRendering,
            _ => true,
        };
    }
}
