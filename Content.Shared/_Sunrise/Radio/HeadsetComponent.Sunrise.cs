using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

// ReSharper disable once CheckNamespace
namespace Content.Shared.Radio.Components;

public sealed partial class HeadsetComponent
{
    [DataField, AutoNetworkedField]
    public Dictionary<string, bool> EnabledChannels = new();

    [DataField, AutoNetworkedField]
    public Dictionary<string, float> ChannelVolumes = new();

    [DataField]
    public EntProtoId ToggleAction = "ActionToggleHeadset";

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    [DataField]
    public int ReceivedMessagesSinceLastNotify;

    [DataField]
    public SoundSpecifier LowBatteryNotifySound = new SoundPathSpecifier("/Audio/_Sunrise/Effects/beeps.ogg");
}
