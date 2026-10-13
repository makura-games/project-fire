using Content.Server.Popups;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Scp.Other.Deployable;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;

namespace Content.Server._Scp.Misc.Deployable;

public sealed partial class DeployableSystem : SharedDeployableSystem
{
    [Dependency] private EntityStorageSystem _entityStorage = default!;
    [Dependency] private PopupSystem _popup = default!;

    protected override void DoServerShit(Entity<DeployableComponent> ent, EntityUid user)
    {
        var message = Loc.GetString("deployable-deploy-success", ("deployer", Identity.Name(user, EntityManager)), ("target", Name(ent)));
        _popup.PopupCoordinates(message, Transform(ent).Coordinates, PopupType.MediumCaution);

        if (!ent.Comp.DeployStates.TryGetValue(ent.Comp.Deployed, out var toSpawn))
            return;

        // Специально не спавню новый объект перед удалением
        // Если он будет иметь коллизию, то успеет подвинуться от старого объекта до его удаления
        // Поэтому сохраняю нужные данные и спокойно удаляю старого
        var cachedDict = ent.Comp.DeployStates;
        var cachedCoords = Transform(ent).Coordinates;

        // Чтобы развертываемые контейнеры не удаляли ничего, особенно игроков
        _entityStorage.EmptyContents(ent);

        Del(ent);

        var newEntity = Spawn(toSpawn, cachedCoords);

        if (!TryComp<DeployableComponent>(newEntity, out var deployableComponent))
            return;

        // Переношу словарик со стейтами, чтобы не повторять его в прототипах
        deployableComponent.DeployStates = cachedDict;
        Dirty(newEntity, deployableComponent);
    }
}
