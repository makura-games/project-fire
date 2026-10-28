using Content.Shared.Procedural;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Scp.Procedural.PostGeneration;

public sealed partial class LimitedEntityDunGen : IDunGenLayer
{
    /// <summary>
    /// How many are we spawning
    /// </summary>
    [DataField("limit")]
    public int Limit = 1;

    /// <summary>
    /// Proto.
    /// </summary>
    [DataField("entity", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string Entity = string.Empty;
}
