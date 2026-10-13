using System.Numerics;
using Content.Shared._Scp.Helpers;
using Content.Shared.Light.Components;
using Content.Shared.StatusEffectNew.Components;
using Content.Shared.Weather;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Client.Weather;

public sealed partial class WeatherSystem : SharedWeatherSystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    // Fire added start - чтобы погода не шумела в комплексе
    private float _currentWeatherDb = 10f;
    // Fire added end

    [Dependency] private EntityQuery<AudioComponent> _audioQuery = default!;
    [Dependency] private EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private EntityQuery<RoofComponent> _roofQuery = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeatherStatusEffectComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnComponentShutdown(Entity<WeatherStatusEffectComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Stream = _audio.Stop(ent.Comp.Stream);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        var player = _playerManager.LocalEntity;

        if (player == null)
            return;

        var playerXform = Transform(player.Value);

        var query = EntityQueryEnumerator<WeatherStatusEffectComponent, StatusEffectComponent>();
        while (query.MoveNext(out var uid, out var weather, out var status))
        {
            if (weather.Sound == null || status.AppliedTo != playerXform.MapUid || player == null) // Fire edit - чтобы в лобби не играла амбиент погоды
            {
                weather.Stream = _audio.Stop(weather.Stream);
                return;
            }

            weather.Stream ??= _audio.PlayGlobal(weather.Sound, Filter.Local(), true)?.Entity;

            if (!_audioQuery.TryComp(weather.Stream, out var audio))
                return;

            var occlusion = 0f;
            // Fire added start - чтобы погода не шумела в комплексе
            var additionalVolume = 0f;
            // Fire added end

            // Work out tiles nearby to determine volume.
            if (_gridQuery.TryComp(playerXform.GridUid, out var grid))
            {
                _roofQuery.TryComp(playerXform.GridUid, out var roofComp);
                var gridId = playerXform.GridUid.Value;
                // FloodFill to the nearest tile and use that for audio.
                var seed = _mapSystem.GetTileRef(gridId, grid, playerXform.Coordinates);
                var frontier = new Queue<TileRef>();
                frontier.Enqueue(seed);
                // If we don't have a nearest node don't play any sound.
                EntityCoordinates? nearestNode = null;
                var visited = new HashSet<Vector2i>();

                while (frontier.TryDequeue(out var node))
                {
                    if (!visited.Add(node.GridIndices))
                        continue;

                    if (!CanWeatherAffect((playerXform.GridUid.Value, grid, roofComp), node))
                    {
                        // Add neighbors
                        // TODO: Ideally we pick some deterministically random direction and use that
                        // We can't just do that naively here because it will flicker between nearby tiles.
                        for (var x = -1; x <= 1; x++)
                        {
                            for (var y = -1; y <= 1; y++)
                            {
                                if (Math.Abs(x) == 1 && Math.Abs(y) == 1 ||
                                    x == 0 && y == 0 ||
                                    (new Vector2(x, y) + node.GridIndices - seed.GridIndices).Length() > 3)
                                {
                                    continue;
                                }

                                frontier.Enqueue(_mapSystem.GetTileRef(gridId, grid, new Vector2i(x, y) + node.GridIndices));
                            }
                        }

                        continue;
                    }

                    nearestNode = new EntityCoordinates(playerXform.GridUid.Value,
                        node.GridIndices + grid.TileSizeHalfVector);
                    break;
                }

                // Get occlusion to the targeted node if it exists, otherwise set a default occlusion.
                if (nearestNode != null)
                {
                    var entPos = _transform.GetMapCoordinates(playerXform);
                    var nodePosition = _transform.ToMapCoordinates(nearestNode.Value).Position;
                    var delta = nodePosition - entPos.Position;
                    var distance = delta.Length();
                    occlusion = _audio.GetOcclusion(entPos, delta, distance);

                    // Fire added start - чтобы погода не шумела в комплексе
                    additionalVolume = ScpHelpers.SmoothVolumeDb(distance, 0f, 10f, 10f, -30f, ref _currentWeatherDb, frameTime, 30f);
                    // Fire added end
                }
                else
                {
                    occlusion = 3f;
                    // Fire added start - чтобы погода не шумела в комплексе
                    additionalVolume = -30f;
                    // Fire added end
                }
            }

            var alpha = GetWeatherPercent((uid, status));
            // Fire edit start - чтобы погода не шумела в комплексе
            alpha *= SharedAudioSystem.VolumeToGain(weather.Sound.Params.Volume + additionalVolume);
            // Fire edit end
            _audio.SetGain(weather.Stream, alpha, audio);
            audio.Occlusion = occlusion;
        }
    }
}
