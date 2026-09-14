using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Scp.Other.ScpBlind;

public sealed partial class ScpBlindActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class ScpActionStartBlind : SimpleDoAfterEvent;
