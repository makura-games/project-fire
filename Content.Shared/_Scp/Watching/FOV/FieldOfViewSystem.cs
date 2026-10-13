using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode;
using Content.Shared.MouseRotator;

namespace Content.Shared._Scp.Watching.FOV;

public sealed partial class FieldOfViewSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;

    private EntityQuery<FieldOfViewComponent> _fovQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private const float DefaultAngleToleranceForOverride = 2f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FieldOfViewComponent, BuckledEvent>(OnBuckle);
        SubscribeLocalEvent<FieldOfViewComponent, UnbuckledEvent>(OnUnbuckle);

        _fovQuery = GetEntityQuery<FieldOfViewComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
    }

    private void OnBuckle(Entity<FieldOfViewComponent> ent, ref BuckledEvent args)
    {
        EnsureComp<MouseRotatorComponent>(ent);
    }

    private void OnUnbuckle(Entity<FieldOfViewComponent> ent, ref UnbuckledEvent args)
    {
        if (TryComp<CombatModeComponent>(ent, out var combat) && combat.IsInCombatMode)
            return;

        RemComp<MouseRotatorComponent>(ent);
    }

    public float GetVisibilityAlpha(
        EntityUid viewer,
        EntityUid target,
        float angle,
        float angleTolerance,
        bool checkCircle = false,
        float ignoreRadius = 0f,
        float ignoreFeather = 0f)
    {
        if (!_xformQuery.TryComp(viewer, out var viewerXform))
            return 1f;

        if (!_xformQuery.TryComp(target, out var targetXform))
            return 1f;

        return GetVisibilityAlpha((viewer, viewerXform), (target, targetXform), angle, angleTolerance, checkCircle, ignoreRadius, ignoreFeather);
    }

    /// <summary>
    /// Вычисляет процент видимости цели для смотрящего.
    /// </summary>
    /// <returns>Значение от 0.0f (полностью невидима) до 1.0f (полностью видима).</returns>
    /// <param name="viewer">Сущность, которая смотрит.</param>
    /// <param name="target">Сущность, которую проверяют.</param>
    /// <param name="angle">Угол обзора смотрящего в градусах.</param>
    /// <param name="angleTolerance">Дополнительный "размытый" угол обзора.</param>
    /// <param name="checkCircle">Проверять ли "слепую зону" в виде круга вокруг смотрящего.</param>
    /// <param name="ignoreRadius">Радиус "слепой зоны".</param>
    /// <param name="ignoreFeather">"Размытая" граница "слепой зоны".</param>
    public float GetVisibilityAlpha(
        Entity<TransformComponent> viewer,
        Entity<TransformComponent> target,
        float angle,
        float angleTolerance,
        bool checkCircle = false,
        float ignoreRadius = 0f,
        float ignoreFeather = 0f)
    {
        // Получаем позицию и вращение смотрящего
        var viewerPos = _transform.GetWorldPosition(viewer.Comp, _xformQuery);
        var viewerRot = _transform.GetWorldRotation(viewer.Comp, _xformQuery);

        // Получаем позицию цели
        var targetPos = _transform.GetWorldPosition(target.Comp, _xformQuery);

        // Вектор должен быть направлен ОТ смотрящего К цели
        var dist = targetPos - viewerPos;
        var distLength = dist.Length();
        var targetAngle = dist.ToWorldAngle();
        var angleDist = Angle.ShortestDistance(viewerRot, targetAngle);

        var radConeAngle = MathHelper.DegreesToRadians(angle);
        var radConeFeather = MathHelper.DegreesToRadians(angleTolerance);

        var angleInvisibility = 0f;
        var halfCone = radConeAngle * 0.5f;
        // Если вдруг angleTolerance будет 0, то нам нужно применять другую логику, чтобы не выйти на деление на 0.
        if (radConeFeather > 0f)
        {
            // Вычисляем "коэффициент невидимости" по углу.
            // 0 = в центре конуса, 1 = за пределами конуса + допуска.
            var halfFeather = radConeFeather * 0.5f;
            var angleDeviation = Math.Abs(angleDist.Theta) - halfCone + halfFeather;
            angleInvisibility = (float) Math.Clamp(angleDeviation, 0f, radConeFeather) / radConeFeather;
        }
        else
        {
            angleInvisibility = Math.Abs(angleDist.Theta) > halfCone ? 1f : 0f;
        }

        // Вычисляем "коэффициент невидимости" по дистанции (для круга).
        var distInvisibility = 1f; // По умолчанию не влияет (считаем, что цель видима)
        if (checkCircle)
        {
            // 0 = близко к центру круга, 1 = за пределами круга + допуска.
            distInvisibility = Math.Clamp((distLength - ignoreRadius) + (ignoreFeather * 0.5f), 0f, ignoreFeather) / ignoreFeather;
        }

        // Целевая альфа (видимость):
        // Сущность видима (альфа > 0), если она находится внутри конуса ИЛИ внутри круга.
        // 1.0f - angleInvisibility -> видимость по углу
        // 1.0f - distInvisibility -> видимость по кругу
        // Math.Max берет максимальное значение видимости из двух.
        var targetAlpha = Math.Max(1f - angleInvisibility, 1f - distInvisibility);

        return targetAlpha;
    }

    /// <summary>
    /// Метод прослойка, который решает какой из реальных методов будет использован.
    /// </summary>
    /// <param name="viewer">Смотрящий</param>
    /// <param name="target">Цель, котору проверяем</param>
    /// <param name="angleOverride">Кастомный угол поля зрения. Если имеется - будет использован метод не использующий компонент FOV. Если не имеется - стандартный метод</param>
    /// <returns>Находится ли сущность в поле зрения смотрящего?</returns>
    public bool IsInFov(EntityUid viewer, EntityUid target, float? angleOverride)
    {
        if (angleOverride.HasValue)
            return IsInViewAngle(viewer, target, angleOverride.Value);

        return IsInFov(viewer, target);
    }

    /// <summary>
    /// Проверяет, находится ли цель в поле зрения смотрящего.
    /// </summary>
    /// <param name="viewer">Сущность, которая смотрит.</param>
    /// <param name="target">Сущность, которую проверяют.</param>
    /// <param name="checkCircle">Если true, будет произведена проверка нахождения в круге вокруг персонажа.</param>
    /// <param name="epsilon">Какой процент видимости должен быть, чтобы мы засчитали сущность как видимую. По умолчанию любой больше 0</param>
    /// <param name="logIfMissingComponent">Будут ли логгироваться ошибки при отсутствии компонентов у переданных сущностей?</param>
    /// <returns>True, если цель хотя бы частично видна.</returns>
    public bool IsInFov(Entity<FieldOfViewComponent?, TransformComponent?> viewer, Entity<TransformComponent?> target, bool checkCircle = false, float epsilon = 0.05f, bool logIfMissingComponent = true)
    {
        if (!Resolve(viewer, ref viewer.Comp1, ref viewer.Comp2, logIfMissingComponent))
            return true;

        if (!Resolve(target, ref target.Comp, logIfMissingComponent))
            return true;

        var alpha = GetVisibilityAlpha(
            (viewer, viewer.Comp2),
            (target, target.Comp),
            viewer.Comp1.Angle,
            viewer.Comp1.AngleFeather,
            checkCircle,
            viewer.Comp1.ConeIgnoreRadius,
            viewer.Comp1.ConeIgnoreFeather
            );

        // Проверка с небольшим эпсилоном
        return alpha > epsilon;
    }

    /// <summary>
    /// Проверяет, находится ли цель в заданном угловом диапазоне поля зрения смотрящего.
    /// </summary>
    /// <param name="viewer">Сущность, которая смотрит.</param>
    /// <param name="target">Сущность, которую проверяют.</param>
    /// <param name="fovAngleOverride">Полный угол поля зрения в градусах (например, 120).</param>
    /// <param name="epsilon">Процентное значение видимости цели, которое мы будем использовать как сравнительную меру. Если итого получится меньше - считаем цель невидимой</param>
    /// <returns>True, если цель в поле зрения.</returns>
    public bool IsInViewAngle(Entity<TransformComponent?> viewer, Entity<TransformComponent?> target, float fovAngleOverride, float epsilon = 0.05f)
    {
        if (!Resolve(viewer, ref viewer.Comp))
            return false;

        if (!Resolve(target, ref target.Comp))
            return false;

        var alpha = GetVisibilityAlpha(
            (viewer, viewer.Comp),
            (target, target.Comp),
            fovAngleOverride,
            DefaultAngleToleranceForOverride
        );

        return alpha > epsilon; // Проверка с небольшим эпсилоном
    }
}
