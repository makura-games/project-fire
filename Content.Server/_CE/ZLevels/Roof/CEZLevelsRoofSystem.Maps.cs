/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Robust.Shared.Analyzers;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Roof;
using Content.Shared.Light.Components;

namespace Content.Server._CE.ZLevels.Roof;

public sealed partial class CEZLevelsRoofSystem
{
    private void InitializeMapsSubscriptions()
    {
        SubscribeLocalEvent<CEZLevelMapNetworkUpdatedEvent>(OnMapNetworkUpdated);
    }

    private void InitMaps()
    {
    }

    private void OnMapNetworkUpdated(CEZLevelMapNetworkUpdatedEvent args)
    {
        if (!TryComp<CEZMapNetworkComponent>(args.Network, out var network))
            return;

        RecalculateMapRoofs((args.Network, network));
    }

    private void RecalculateMapRoofs(Entity<CEZMapNetworkComponent> network)
    {
        _lightBlockingTiles.Clear();
        _weatherBlockingTiles.Clear();

        List<EntityUid> sortedMaps = new();
        foreach (var mapUid in network.Comp.ZLevels
                     .OrderByDescending(kv => kv.Key) // depth sorting
                     .Select(kv => kv.Value)
                     .Where(uid => uid.HasValue)
                     .Select(uid => uid!.Value))
        {
            sortedMaps.Add(mapUid);
        }

        foreach (var map in sortedMaps)
        {
            if (!GridQuery.TryComp(map, out var mapGrid))
                continue;

            EnsureComp<CEZLevelRoofComponent>(map);
            var enumerator = Map.GetAllTilesEnumerator(map, mapGrid);
            var roofComp = EnsureComp<RoofComponent>(map);

            while (enumerator.MoveNext(out var tileRef))
            {
                var indices = tileRef.Value.GridIndices;
                Roof.SetRoof((map, mapGrid, roofComp), indices,
                    _lightBlockingTiles.Contains(indices), occludeWeather: _weatherBlockingTiles.Contains(indices));

                _weatherBlockingTiles.Add(indices);
                if (TileBlocksLight(tileRef.Value.Tile))
                    _lightBlockingTiles.Add(indices);
            }
        }
    }
}
