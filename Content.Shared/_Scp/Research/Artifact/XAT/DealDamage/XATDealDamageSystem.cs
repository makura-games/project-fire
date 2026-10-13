using Content.Shared._Scp.Other.Events;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Content.Shared.Xenoarchaeology.Artifact.XAT;

namespace Content.Shared._Scp.Research.Artifact.XAT.DealDamage;

public sealed partial class XATDealDamageSystem : BaseXATSystem<XATDealDamageComponent>
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();

        XATSubscribeDirectEvent<DamageChangedOriginEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<XenoArtifactComponent> artifact,
        Entity<XATDealDamageComponent, XenoArtifactNodeComponent> node,
        ref DamageChangedOriginEvent args)
    {
        if (args.Damage.Empty)
            return;

        if (!TryAccumulate(node, args.Damage, args.Target))
            return;

        if (!ReachedRequiredDamage(node))
            return;

        Trigger(artifact, node);
    }

    private bool TryAccumulate(Entity<XATDealDamageComponent, XenoArtifactNodeComponent> node, DamageSpecifier damage, EntityUid target)
    {
        if (!_whitelist.CheckBoth(target, node.Comp1.Blacklist, node.Comp1.Whitelist))
            return false;

        foreach (var (key, value) in damage.DamageDict)
        {
            // Если это первое добавление - не делаем прибавление value,
            // чтобы избежать двойного добавления значения
            if (node.Comp1.AccumulatedDamage.DamageDict.TryAdd(key, value))
                continue;

            node.Comp1.AccumulatedDamage.DamageDict[key] += value;
        }

        Dirty(node, node.Comp1);

        return true;
    }

    private bool ReachedRequiredDamage(Entity<XATDealDamageComponent, XenoArtifactNodeComponent> node)
    {
        foreach (var (key, value) in node.Comp1.RequiredDamage.DamageDict)
        {
            // Нет какого-то из требуемых типов урона?
            if (!node.Comp1.AccumulatedDamage.DamageDict.TryGetValue(key, out var savedDamage))
                return false;

            // Текущий урон данного типа есть, но меньше?
            if (savedDamage < value)
                return false;
        }

        // Все типы урона есть и соответствуют требуемому минимуму для триггера
        return true;
    }
}
