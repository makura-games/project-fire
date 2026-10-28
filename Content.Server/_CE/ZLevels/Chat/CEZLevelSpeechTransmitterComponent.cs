namespace Content.Server._CE.ZLevels.Chat;

/// <summary>
/// Marks the temporary entity that repeats speech on an adjacent z-level, so relays can ignore it.
/// </summary>
[RegisterComponent]
public sealed partial class CEZLevelSpeechTransmitterComponent : Component;
