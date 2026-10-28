using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Content.Shared.Containers.ItemSlots;
using System.Linq;

namespace Content.Shared._Scp.NukeCodeConsole;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NukeCodeConsoleComponent : Component
{
    public const string KeySlotId = "NukeCodeConsoleKey";

    /// <summary>
    /// Слот для ключа, необходимого для работы консоли.
    /// </summary>
    [DataField(required: true)]
    public ItemSlot KeySlot = new();

    [DataField, AutoNetworkedField]
    public bool IsLocked = true;

    [DataField, AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public int CodeLength = 6;

    [DataField(serverOnly: true)]
    public string Code
    {
        get => _code;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !value.All(char.IsDigit))
                return;

            CodeLength = value.Length;
            _code = value;
        }
    }

    [DataField, AutoNetworkedField]
    public bool IsSealed = false;

    private string _code = string.Empty;

    [DataField(serverOnly: true)]
    public string EnteredCode = string.Empty;

    [DataField]
    public TimeSpan ActivateButtonDelay = TimeSpan.FromMinutes(1);

    [DataField]
    public float NukeRemainingTimeStart = 120f;

    [DataField]
    public SoundSpecifier KeypadPressSound = new SoundPathSpecifier("/Audio/Machines/Nuke/general_beep.ogg");

    [DataField]
    public SoundSpecifier AccessGrantedSound = new SoundPathSpecifier("/Audio/Machines/Nuke/confirm_beep.ogg");

    [DataField]
    public SoundSpecifier AccessDeniedSound = new SoundPathSpecifier("/Audio/Machines/Nuke/angry_beep.ogg");

    [ViewVariables]
    public TimeSpan? NextActivateButtonAvailable;
}



