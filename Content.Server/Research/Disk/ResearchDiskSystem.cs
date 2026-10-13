using System.Linq;
using Content.Shared.Interaction;
using Content.Server.Popups;
using Content.Shared.Research.Prototypes;
using Content.Server.Research.Systems;
using Content.Shared._Scp.Helpers;
using Content.Shared.Research;
using Content.Shared.Research.Components;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Disk
{
    public sealed partial class ResearchDiskSystem : EntitySystem
    {
        [Dependency] private IPrototypeManager _prototype = default!;
        [Dependency] private PopupSystem _popupSystem = default!;
        [Dependency] private ResearchSystem _research = default!;
        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ResearchDiskComponent, AfterInteractEvent>(OnAfterInteract);
            SubscribeLocalEvent<ResearchDiskComponent, MapInitEvent>(OnMapInit);
        }

        private void OnAfterInteract(EntityUid uid, ResearchDiskComponent component, AfterInteractEvent args)
        {
            if (!args.CanReach)
                return;

            if (!TryComp<ResearchServerComponent>(args.Target, out var server))
                return;

            // Fire edit start - поддержка разных поинтов рнд
            foreach (var (pointPrototype, value) in component.Points)
            {
                _research.ModifyServerPoints(args.Target.Value, pointPrototype, value, server);
                _popupSystem.PopupEntity(Loc.GetString("research-disk-inserted", ("points", component.Points)), args.Target.Value, args.User);
            }
            // Fire edit end

            QueueDel(uid);
            args.Handled = true;
        }

        private void OnMapInit(EntityUid uid, ResearchDiskComponent component, MapInitEvent args)
        {
            if (!component.UnlockAllTech)
                return;

            var allTechValue = new Dictionary<ProtoId<ResearchPointPrototype>, int>();

            foreach (var prototype in _prototype.EnumeratePrototypes<TechnologyPrototype>())
            {
                // Fire edit - поддержка несколько видов очков исследований
                foreach (var (pointType, value) in ResearchPointsHelper.GetPoints(prototype))
                {
                    allTechValue.TryGetValue(pointType, out var totalPoints);
                    allTechValue[pointType] = totalPoints + value;
                }
            }

            component.Points = allTechValue;
        }
    }
}
