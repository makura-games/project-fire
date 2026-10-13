using Content.Shared._Scp.Other.ClassDAppearance;
using Robust.Shared.Random;

namespace Content.Server._Scp.ClassDAppearance;

public sealed partial class ClassDAppearanceSystem : EntitySystem
{
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ClassDAppearanceComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ClassDAppearanceComponent> ent, ref MapInitEvent args)
    {
        var name = "D-" + _random.Next(1000, 9999);

        _metaData.SetEntityName(ent, name);
    }
}
