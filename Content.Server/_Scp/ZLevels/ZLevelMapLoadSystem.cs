using System.Numerics;
using Content.Server._CE.ZLevels.Core.Components;
using Content.Server.GameTicking;

namespace Content.Server._Scp.ZLevels;

/// <summary>
/// Keeps station maps aligned with the maps loaded above and below them.
/// </summary>
public sealed partial class ZLevelMapLoadSystem : EntitySystem
{
    [Dependency] private IComponentFactory _componentFactory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PreGameMapLoad>(OnPreGameMapLoad);
    }

    private void OnPreGameMapLoad(PreGameMapLoad args)
    {
        foreach (var station in args.GameMap.Stations.Values)
        {
            if (!station.StationComponentOverrides.TryGetComponent<CEStationZLevelsComponent>(
                    _componentFactory, out var zLevels) ||
                zLevels.MapsAbove.Count == 0 && zLevels.MapsBelow.Count == 0)
                continue;

            // Дополнительные карты CE загружаются без смещения и поворота.
            args.Offset = Vector2.Zero;
            args.Rotation = Angle.Zero;
            return;
        }
    }
}
