using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client._CE.ZLevels.Core;
using Content.Client.Light;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Robust.Client.ComponentTrees;
using Robust.Client.Graphics;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Light;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Scp.ZLevels;

internal sealed partial class ZLevelLightOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> LightShader = "ScpZLevelTransmittedLight";
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(0.08);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(1);
    private const int SamplesPerDraw = 128;
    private const float TransmissionPerLevel = 0.65f;

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly IEntityManager _entities;
    private readonly CEClientZLevelsSystem _zLevels;
    private readonly LightLevelSystem _lightLevel;
    private readonly LightTreeSystem _lightTree;
    private readonly SharedTransformSystem _transform;
    private readonly List<SourceLevel> _sources = new();
    private readonly List<WorldRect> _rects = new();
    private readonly Dictionary<(MapId Map, Vector2i Tile), LightSample> _samples = new();
    private readonly Dictionary<MapId, (int TileIndex, TimeSpan Time)> _sampleCursors = new();
    private TimeSpan _nextPrune;

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    public ZLevelLightOverlay(IEntityManager entities, CEClientZLevelsSystem zLevels)
    {
        IoCManager.InjectDependencies(this);
        _entities = entities;
        _zLevels = zLevels;
        _lightLevel = entities.System<LightLevelSystem>();
        _lightTree = entities.System<LightTreeSystem>();
        _transform = entities.System<SharedTransformSystem>();
        ZIndex = AfterLightTargetOverlay.ContentZIndex + 2;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { DrawLight: true } ||
            !_entities.TryGetComponent<CEZMapComponent>(args.MapUid, out var zMap))
        {
            return;
        }

        var now = _timing.RealTime;
        if (now >= _nextPrune)
        {
            _nextPrune = now + CacheLifetime;
            foreach (var (key, sample) in _samples)
            {
                if (now - sample.LastSeen >= CacheLifetime)
                    _samples.Remove(key);
            }

            foreach (var (map, cursor) in _sampleCursors)
            {
                if (now - cursor.Time >= CacheLifetime)
                    _sampleCursors.Remove(map);
            }
        }

        var bounds = CollectSources((args.MapUid, zMap), args.WorldAABB.Enlarged(1f));
        if (bounds is not { } litBounds)
            return;

        var start = litBounds.BottomLeft.Floored();
        var end = litBounds.TopRight.Ceiled();
        var width = end.X - start.X;
        var tileCount = width * (end.Y - start.Y);
        if (tileCount <= 0)
            return;

        var nextTile = _sampleCursors.GetValueOrDefault(args.MapId).TileIndex % tileCount;
        var tileIndex = nextTile;
        var remaining = SamplesPerDraw;
        _rects.Clear();

        // Карты Z-сети используют общую сетку мировых координат.
        for (var i = 0; i < tileCount; i++)
        {
            var tile = start + new Vector2i(tileIndex % width, tileIndex / width);
            var position = (Vector2) tile + new Vector2(0.5f);
            var light = Vector3.Zero;

            foreach (var source in _sources)
            {
                if (_zLevels.HasOpaqueAbove(position, source.LowerMap))
                    break;

                if (source.Bounds is { } sourceBounds && sourceBounds.Contains(position))
                    light += SampleLight(source, tile, position, now, ref remaining) * source.Transmission;
            }

            tileIndex = (tileIndex + 1) % tileCount;
            // Продолжаем с оставшегося участка в следующем кадре, когда исчерпан бюджет проб.
            if (remaining > 0)
                nextTile = tileIndex;

            if (light != Vector3.Zero)
                _rects.Add(new WorldRect(Box2.CenteredAround(position, Vector2.One), new Color(light.X, light.Y, light.Z)));
        }

        _sampleCursors[args.MapId] = (nextTile, now);

        if (_rects.Count == 0)
            return;

        // Clyde уже привязал LightRenderTarget для прохода BeforeLighting.
        var world = args.WorldHandle;
        world.UseShader(_prototypes.Index(LightShader).Instance());
        world.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
        world.UseShader(null);
    }

    private Box2? CollectSources(Entity<CEZMapComponent?> lowerMap, Box2 visibleBounds)
    {
        var maxRadius = _configuration.GetCVar(CVars.MaxLightRadius);
        var searchBounds = visibleBounds.Enlarged(maxRadius);
        var transmission = TransmissionPerLevel;
        Box2? litBounds = null;
        _sources.Clear();

        for (var depth = 1; depth <= CESharedZLevelsSystem.MaxZLevelsAboveRendering; depth++)
        {
            if (!_zLevels.TryMapOffset(lowerMap, 1, out var upperMap, out var map))
                break;

            var state = new LightBoundsQuery(_transform, visibleBounds, maxRadius);
            if (map.LightingEnabled)
                _lightTree.QueryAabb(ref state, CollectLightBounds, map.MapId, searchBounds);

            var ambient = _entities.GetComponentOrNull<MapLightComponent>(upperMap)?.AmbientLightColor
                ?? MapLightComponent.DefaultColor;
            var bounds = state.Bounds?.Intersect(visibleBounds);
            _sources.Add(new SourceLevel(lowerMap, map.MapId, bounds,
                new Vector3(ambient.R, ambient.G, ambient.B), transmission));

            if (bounds is { } sourceBounds)
                litBounds = litBounds?.Union(sourceBounds) ?? sourceBounds;

            lowerMap = upperMap.AsNullable();
            transmission *= TransmissionPerLevel;
        }

        return litBounds;
    }

    private static bool CollectLightBounds(ref LightBoundsQuery query, in ComponentTreeEntry<SharedPointLightComponent> entry)
    {
        var light = entry.Component;
        if (!light.Enabled || light.Energy <= 0f || light.Radius <= 0f)
            return true;

        var (position, rotation) = query.Transform.GetWorldPositionRotation(entry.Transform);
        position += rotation.RotateVec(light.Offset);
        var bounds = Box2.CenteredAround(position, new Vector2(2f * Math.Min(light.Radius, query.MaxRadius)));
        if (bounds.Intersects(query.VisibleBounds))
            query.Bounds = query.Bounds?.Union(bounds) ?? bounds;

        return true;
    }

    private Vector3 SampleLight(SourceLevel source, Vector2i tile, Vector2 position, TimeSpan now, ref int remaining)
    {
        var key = (source.Map, tile);
        if (_samples.TryGetValue(key, out var sample) && (now - sample.Time < SampleInterval || remaining == 0))
        {
            _samples[key] = sample with { LastSeen = now };
            return sample.Color;
        }

        // Переиспользуем недавние пробы, чтобы ограничить количество проверок теней за проход.
        if (remaining == 0)
            return Vector3.Zero;

        remaining--;
        var color = Vector3.Zero;
        if (_lightLevel.TryCalculateLightColor(new MapCoordinates(position, source.Map), out var pointColor))
        {
            // Рассеянное освещение передаётся системой крыш. Здесь добавляем только вклад источников.
            color = Vector3.Max(Vector3.Zero,
                new Vector3(pointColor.R, pointColor.G, pointColor.B) - source.Ambient);
        }

        _samples[key] = new LightSample(color, now, now);
        return color;
    }

    private struct LightBoundsQuery(SharedTransformSystem transform, Box2 visibleBounds, float maxRadius)
    {
        public readonly SharedTransformSystem Transform = transform;
        public readonly Box2 VisibleBounds = visibleBounds;
        public readonly float MaxRadius = maxRadius;
        public Box2? Bounds;
    }

    private readonly record struct SourceLevel(
        Entity<CEZMapComponent?> LowerMap,
        MapId Map,
        Box2? Bounds,
        Vector3 Ambient,
        float Transmission);

    private readonly record struct LightSample(Vector3 Color, TimeSpan Time, TimeSpan LastSeen);
}
