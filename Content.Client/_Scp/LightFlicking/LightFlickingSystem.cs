using Content.Client.Light.Visualizers;
using Content.Shared._Scp.LightFlicking;
using Content.Shared.Light;
using Robust.Client.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Client._Scp.LightFlicking;

public sealed partial class LightFlickingSystem : SharedLightFlickingSystem
{
    [Dependency] private SharedPointLightSystem _pointLight = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float RadiusVariationPercentage = 0.2f;
    private const float EnergyVariationPercentage = 0.2f;

    private readonly SoundSpecifier _flickSound = new SoundPathSpecifier("/Audio/_Scp/Effects/flick.ogg");

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_timing.IsFirstTimePredicted)
            return;

        var query = AllEntityQuery<ActiveLightFlickingComponent, PoweredLightVisualsComponent, SpriteComponent>();

        while (query.MoveNext(out var uid, out var flicking, out _, out var sprite))
        {
            if (Timing.CurTime <= flicking.NextFlickTime)
                continue;

            var newEnergy = Variantize(flicking.CachedEnergy, EnergyVariationPercentage);
            var newRadius = Variantize(flicking.CachedRadius, RadiusVariationPercentage);
            _pointLight.SetEnergy(uid, newEnergy);
            _pointLight.SetRadius(uid, newRadius);

            // Изменяем цвет лампочки в зависимости от того, насколько сильно изменилось свечение
            var newColor = DimColorBasedOnChange(flicking.CachedColor, flicking.CachedRadius, newRadius);
            // _pointLight.SetColor(uid, newColor); слишком вырвиглазно, но работает

            sprite.LayerSetColor(PoweredLightLayers.Glow, newColor);

            // TODO: Возможная реализация звука через AmbientSound
            // Движок не справляется с таким количеством звуков
            //_audio.PlayPvs(_flickSound, uid, AudioParams.Default.WithVolume(-8).WithMaxDistance(2f));

            SetNextFlickingTime((uid, flicking));
        }
    }

    private float Variantize(float origin, float baseVariation)
    {
        var variation = (float)(Random.NextDouble() * (2 * baseVariation) - baseVariation);
        return origin * (1 + variation);
    }

    // Мне это написал чатгпт
    private static Color DimColorBasedOnChange(Color color, float firstNumber, float secondNumber)
    {
        // Вычисляем разницу между вторым и первым числом
        var difference = Math.Abs(secondNumber - firstNumber);

        // Нормализуем разницу в диапазоне от 0 до 1
        var normalizedDifference = Math.Clamp(difference / firstNumber, 0, 1);

        // Уменьшаем яркость цвета на основе нормализованной разницы
        var dimFactor = 1 - normalizedDifference;

        // Применяем уменьшение яркости к каждому компоненту цвета
        var r = color.R * dimFactor;
        var g = color.G * dimFactor;
        var b = color.B * dimFactor;

        // Возвращаем новый цвет с уменьшенной яркостью
        return new Color(r, g, b, color.A);
    }
}
