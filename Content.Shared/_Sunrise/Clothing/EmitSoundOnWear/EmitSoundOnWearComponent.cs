using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Sunrise.Clothing.Components;

/// <summary>
/// Указывает, что предмет одежды издаёт зацикленный звук, пока надет.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EmitSoundWhileWornComponent : Component
{
    /// <summary>
    /// Звук, который будет проигрываться.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public SoundSpecifier Sound = default!;

    /// <summary>
    /// Требуется ли гравитация для работы предмета.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequiresGravity = true;

    /// <summary>
    /// Требуется ли, чтобы носитель был жив.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool RequiresAlive = true;

    /// <summary>
    /// Не проигрывать, если предмет в карманах.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool IgnorePockets = true;

    /// <summary>
    /// Текущий носитель (runtime, сервер).
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? Wearer;

    /// <summary>
    /// Активный аудио-стрим (runtime, сервер).
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? SoundEntity;

    /// <summary>
    /// Надет ли предмет в корректный слот.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool IsValidSlot = true;
}
