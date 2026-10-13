using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;

namespace Content.Server._Scp.Backrooms.ChangeLightOnDamage;

public sealed partial class ChangeLightOnDamageSystem : EntitySystem
{
    [Dependency] private SharedPointLightSystem _pointLight = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ChangeLightOnDamageComponent, DamageChangedEvent>(OnDamage);

        _sawmill = Logger.GetSawmill(SawmillName);
    }

    private void OnDamage(Entity<ChangeLightOnDamageComponent> entity, ref DamageChangedEvent args)
    {
        SharedPointLightComponent? pointLightComponent = null;

        if (!_pointLight.ResolveLight(entity, ref pointLightComponent))
            return;

        if (args.DamageDelta == null)
            return;

        var newColor = GetInterpolatedColor(pointLightComponent.Color,
            entity.Comp.TargetLightColor,
            args.DamageDelta.GetTotal(),
            entity.Comp.MaxDamage);

        _pointLight.SetColor(entity, newColor);

    }

    /// <summary>
    /// Метод, который возвращает цвет в зависимости от текущей хп ентити от его максимума.
    /// Чем больше урона, тем ближе цвет к максимуму. И наоборот
    /// </summary>
    /// <param name="startColor">Начальный цвет</param>
    /// <param name="endColor">Конечный цвет</param>
    /// <param name="currentValue">Текущее значение урона</param>
    /// <param name="maxValue">Максимум возможного урона</param>
    /// <returns></returns>
    private Color GetInterpolatedColor(Color startColor, Color endColor, FixedPoint2 currentValue, FixedPoint2 maxValue)
    {
        // Проверяем, что максимальное значение не равно нулю
        if (maxValue <= 0)
        {
            _sawmill.Error("maxValue must be greater than 0");
            return startColor;
        }

        // Нормализуем текущее значение
        var value = currentValue / maxValue;

        // Вычисляем процентное соотношение
        var percentage = Math.Clamp(value.Float(), 0f, 1f);

        // Интерполируем цвет
        var red = startColor.R + (endColor.R - startColor.R) * percentage;
        var green = startColor.G + (endColor.G - startColor.G) * percentage;
        var blue = startColor.B + (endColor.B - startColor.B) * percentage;

        // Создаем новый цвет
        return new Color(red, green, blue);
    }
}
