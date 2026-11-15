using Content.Shared.Tools;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Scp.Other.Deployable;

/// <summary>
/// Позволяет развертывать и свертывать ентити путем удаления старого и спавна нового по айди
/// Для свертывания или развертывания требуется инструмент из списка RequiredTools
/// Пример использования в прототипе
/// </summary>
/// <code>
///- type: Deployable
///  requiredTools:
///  - Screwing
///  deployed: false
///  deployStates:
///    false: PrototypeFolded
///    true: PrototypeNormal
/// </code>
[RegisterComponent, NetworkedComponent]
public sealed partial class DeployableComponent : Component
{
    /// <summary>
    /// Инструменты, которые требуются для развертывания
    /// </summary>
    [DataField(required: true)]
    public HashSet<ProtoId<ToolQualityPrototype>> RequiredTools = [];

    /// <summary>
    /// Время развертывания. На него влиет качество инструмента, ускоряя или замедляя его
    /// </summary>
    [DataField]
    public float DeployTime = 3f;

    /// <summary>
    /// Прототипы развернутой и свернутой версии ентити. Пример использования в описании компонента
    /// </summary>
    [DataField]
    public Dictionary<bool, EntProtoId> DeployStates = new ();

    /// <summary>
    /// Звук проигрывающийся при успешном развертывании или свертывании
    /// </summary>
    [DataField]
    public SoundSpecifier SuccessSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");

    /// <summary>
    /// Свернуты или развернуты. Прописывается в прототипах, прописывать в коде нет смысла
    /// </summary>
    [DataField(required: true)]
    public bool Deployed;
}
