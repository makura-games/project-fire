using Content.Shared.Whitelist;
using Robust.Shared.GameStates;

namespace Content.Shared._Scp.Mobs.Components;

/// <summary>
/// Компонент, позволяющий расширить возможности сущности
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ScpPossibilitiesComponent : Component
{
    /// <summary>
    /// Предоставляет сущности возможность выбрасывать
    /// людей из транспорта с помощью удара
    /// </summary>
    [DataField]
    public bool CanEjectPilotFromVehicle = true;

    /// <summary>
    /// Предоставляет сущности возможность открывать
    /// контейнеры ударом
    /// </summary>
    [DataField]
    public bool OpenContainer = true;

    /// <summary>
    /// Белый список контейнеров, которые сущность
    /// может открыть с помощью удара
    /// </summary>
    [DataField]
    public EntityWhitelist? OpenContainerWhitelist;

    /// <summary>
    /// Чёрный список контейнеров, которые сущность
    /// не может открыть с помощью удара
    /// </summary>
    [DataField]
    public EntityWhitelist? OpenContainerBlacklist;

    /// <summary>
    /// Шанс открытия контейнера с помощью удара
    /// </summary>
    [DataField]
    public float OpenContainerChance = 1;
}
