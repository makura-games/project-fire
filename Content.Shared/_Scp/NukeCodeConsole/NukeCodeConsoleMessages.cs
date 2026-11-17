using Robust.Shared.Serialization;

namespace Content.Shared._Scp.NukeCodeConsole;

[Serializable, NetSerializable]
public sealed class NukeCodeConsoleKeypadMessage : BoundUserInterfaceMessage
{
    public int Value;

    public NukeCodeConsoleKeypadMessage(int value)
    {
        Value = value;
    }
}

[Serializable, NetSerializable]
public sealed class NukeCodeConsoleKeypadEnterMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class NukeCodeConsoleKeypadClearMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class NukeCodeConsoleActivateButtonMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class NukeCodeConsoleLockButtonMessage : BoundUserInterfaceMessage { }

[Serializable, NetSerializable]
public sealed class NukeCodeConsoleUiState : BoundUserInterfaceState
{
    public bool HasKey;
    public bool IsLocked;
    public int EnteredCodeLength;
    public int MaxCodeLength;
}

[Serializable, NetSerializable]
public enum NukeCodeConsoleUiKey : byte
{
    Key
}
