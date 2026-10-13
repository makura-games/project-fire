using Robust.Shared.Configuration;
using Robust.Shared.Serialization;

namespace Content.Shared._Scp.Other.AutoOpenCharacterMenu;

public abstract partial class SharedAutoOpenCharacterMenuSystem : EntitySystem
{
    [Dependency] protected IConfigurationManager Configuration = default!;
}

[Serializable, NetSerializable]
public sealed class OpenCharacterMenuRequest(NetEntity entity) : EntityEventArgs
{
    public readonly NetEntity Entity = entity;
}
