using Content.Shared._Scp.Proximity;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Fear.Components;

/// <summary>
/// Компонент, отвечающий за возможность пугаться.
/// Обрабатывает уровни страха и хранит текущий.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class FearComponent : Component
{
    /// <inheritdoc cref="FearState"/>
    [AutoNetworkedField, ViewVariables]
    public FearState State = FearState.None;

    /// <summary>
    /// Strength retained from the accepted fright. Interfloor sightings use half strength.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public float EffectStrength = 1f;

    /// <summary>
    /// Strength and expiry of the last accepted sight mood, independent of the retained fear state.
    /// </summary>
    public float SeenMoodStrength;
    public TimeSpan SeenMoodExpiry;

    /// <summary>
    /// Список, содержащий информацию о фобиях персонажа
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<ProtoId<PhobiaPrototype>> Phobias = [];

    /// <summary>
    /// Время, через которое уровень страха понизится.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TimeToDecreaseFearLevel = TimeSpan.FromSeconds(180); // 3 минуты

    /// <summary>
    /// Следующее время, когда будут понижен уровень страха со временем.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextTimeDecreaseFearLevel = TimeSpan.Zero;

    #region Shader strength

    /// <summary>
    /// Этот словарь описывает зависимость силы шейдера зернистости от уровня страха у владельца компонента.
    /// Эти значения будут суммироваться с другими при добавлении.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<FearState, float> FearBasedGrainStrength = new ()
    {
        { FearState.None , 0f },
        { FearState.Anxiety, 100f },
        { FearState.Fear , 300f },
        { FearState.Terror, 700f },
    };

    /// <summary>
    /// Этот словарь описывает зависимость силы шейдера виньетки от уровня страха у владельца компонента.
    /// Эти значения будут суммироваться с другими при добавлении.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<FearState, float> FearBasedVignetteStrength = new ()
    {
        { FearState.None , 0f },
        { FearState.Anxiety, 30f },
        { FearState.Fear , 70f },
        { FearState.Terror, 400f },
    };

    /// <summary>
    /// Хранит текущие параметры силы шейдера, зависимой от уровня страха.
    /// Каждый шейдер хранится отдельной парой, где ключ компонент шейдера, а значение его fear-based сила.
    /// </summary>
    /// <remarks>
    /// Ключ в виде строки означает имя компонента с силой шейдера.
    /// </remarks>
    [AutoNetworkedField, ViewVariables]
    public Dictionary<string, float> CurrentFearBasedShaderStrength = new ();

    #endregion

    #region Close to scary thing parameters

    /// <summary>
    /// Модификатор силы шейдера, накладываемый, когда сущность приближается к источнику страха через прозрачный объект.
    /// Через прозрачные объекты приближаться к чем-тому не так страшно.
    /// Рассчитанная сила делится на этот модификатор, НЕ умножается.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float TransparentStrengthDecreaseFactor = 2f;

    /// <summary>
    /// Уровень видимости, который требуется, чтобы почувствовать страх.
    /// Если итоговый уровень будет выше, то не будет и эффекта.
    /// А если ниже, то будет.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LineOfSightBlockerLevel ProximityBlockerLevel = LineOfSightBlockerLevel.Transparent;

    #endregion

    #region See scary thing parameters

    /// <summary>
    /// Уровень видимости, который требуется, чтобы почувствовать страх.
    /// Если итоговый уровень будет выше, то не будет и эффекта.
    /// А если ниже, то будет.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LineOfSightBlockerLevel SeenBlockerLevel = LineOfSightBlockerLevel.Transparent;

    /// <summary>
    /// Время, через которое игрок снова сможет испугаться источника страха, когда увидит его.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan TimeToGetScaredAgainOnLookAt = TimeSpan.FromMinutes(2); // 2 минуты

    #endregion

    #region Gameplay

    /// <summary>
    /// Словарь параметров увеличения разброса при стрельбе.
    /// Каждый уровень страха соответствует модификаторы разброса.
    /// </summary>
    /// TODO: Заменить высокие значения при появлении системы навыков
    /// или другой системы, которая сделает не-боевые роли слабыми в бою
    [DataField, AutoNetworkedField]
    public Dictionary<FearState, float> FearBasedSpreadAngleModifier = new ()
    {
        { FearState.Anxiety, 2f },
        { FearState.Fear, 4f },
        { FearState.Terror, 8f },
    };

    /// <summary>
    /// Базовый модификатор трясучки, которая будет возникать при повышении уровня страха.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float BaseJitterTime = 4f;

    /// <summary>
    /// Необходимый уровень страха, чтобы начать падать при хождении.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FearState FallOffRequiredState = FearState.Terror;

    /// <summary>
    /// Шанс упасть при хождении во время страха постигшего <see cref="FallOffRequiredState"/>
    /// </summary>
    [DataField, AutoNetworkedField]
    public float FallOffChance = 0.03f;

    /// <summary>
    /// Какой уровень страха нужен, чтобы у человека появился адреналин.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FearState AdrenalineRequiredState = FearState.Fear;

    /// <summary>
    /// Базовое количество времени действий адреналина при повышении уровня страха.
    /// На него умножается модификатор от текущего уровня страха.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float AdrenalineBaseTime = 5f;

    /// <summary>
    /// Какой уровень страха требуется, чтобы закричать при поднятии до этого уровня.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FearState ScreamRequiredState = FearState.Terror;

    #endregion
}
