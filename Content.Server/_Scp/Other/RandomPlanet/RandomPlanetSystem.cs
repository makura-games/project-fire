using Content.Server._Scp.Other.AutoRoof;
using Content.Server.Parallax;
using Content.Server.Station.Events;
using Content.Server.Weather;
using Content.Shared.Light.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._Scp.Other.RandomPlanet;

/// <summary>
/// Система, позволяющая создавать случайные биомы для комплексов.
/// </summary>
public sealed partial class RandomPlanetSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private WeatherSystem _weather = default!;
    [Dependency] private AutoRoofSystem _autoRoof = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RandomPlanetComponent, StationPostInitEvent>(OnStationPostInit);
        Log.Level = LogLevel.Info;
    }

    private void OnStationPostInit(Entity<RandomPlanetComponent> ent, ref StationPostInitEvent args)
    {
        var (mapUid, mapId) = GetStationMapUid(args.Station);
        var data = _random.Pick(ent.Comp.Data);
        var biome = _prototype.Index(data.Biome);
        int? seed = data.Seed != null ? _random.Pick(data.Seed) : null;

        _biome.EnsurePlanet(mapUid, biome, seed);

        if (ent.Comp.BuildRoof)
            BuildRoof(args.Station);

        TrySetWeather(mapId, data);
    }

    private bool TrySetWeather(MapId mapId, StationPlanetData data)
    {
        if (data.Weather == null || data.Weather.Count == 0)
            return false;

        var weather = _random.Pick(data.Weather);
        var weatherPrototype = _prototype.Index(weather);
        _weather.TrySetWeather(mapId, weatherPrototype, out _, null);

        return true;
    }

    /// <summary>
    /// Получает айди карты, на которой находится станция.
    /// </summary>
    private (EntityUid, MapId) GetStationMapUid(Entity<StationDataComponent> ent)
    {
        foreach (var grid in ent.Comp.Grids)
        {
            var xform = Transform(grid);

            if (xform.MapUid.HasValue && xform.MapID != MapId.Nullspace)
                return (xform.MapUid.Value, xform.MapID);
        }

        // Сюда доходить не должно
        Log.Error("Cannot find station`s MapId and MapUid");
        return (EntityUid.Invalid, MapId.Nullspace);
    }

    private void BuildRoof(Entity<StationDataComponent> ent)
    {
        foreach (var grid in ent.Comp.Grids)
        {
            if (HasComp<RoofComponent>(grid))
                continue;

            _autoRoof.BuildRoof(grid);
        }
    }
}
