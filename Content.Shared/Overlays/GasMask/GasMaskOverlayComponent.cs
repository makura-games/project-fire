using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Overlays.GasMask;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class GasMaskOverlayComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public bool RelayOverlay = true;

    [DataField, AutoNetworkedField]
    public string TexturePath = "/Textures/Interface/GasmaskOverlay.png";
}
