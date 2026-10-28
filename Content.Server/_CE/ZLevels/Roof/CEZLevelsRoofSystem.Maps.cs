/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using System.Linq;
using Robust.Shared.Analyzers;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Light.Components;
using Content.Shared.Maps;

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
        _roofMap.Clear();

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

            var enumerator = Map.GetAllTilesEnumerator(map, mapGrid);
            var roofComp = EnsureComp<RoofComponent>(map);

            while (enumerator.MoveNext(out var tileRef))
            {
                var covered = _roofMap.Contains(tileRef.Value.GridIndices);
                Roof.SetRoof((map, mapGrid, roofComp), tileRef.Value.GridIndices, covered, occludeWeather: covered);

                var tileDef = (ContentTileDefinition)TilDefMan[tileRef.Value.Tile.TypeId];

                if (!tileDef.Transparent)
                    _roofMap.Add(tileRef.Value.GridIndices);
            }
        }
    }
}
