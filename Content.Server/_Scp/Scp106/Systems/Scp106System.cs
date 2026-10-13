using System.Linq;
using System.Threading.Tasks;
using Content.Server._Scp.Fear;
using Content.Server._Sunrise.Helpers;
using Content.Server.DoAfter;
using Content.Server.GameTicking;
using Content.Server.Gateway.Systems;
using Content.Server.Mind;
using Content.Server.Store.Systems;
using Content.Server.Stunnable;
using Content.Shared._Scp.Fear;
using Content.Shared._Scp.Scp106;
using Content.Shared._Scp.Scp106.Components;
using Content.Shared._Scp.Scp106.Systems;
using Content.Shared.Alert;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.SSDIndicator;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Scp.Scp106.Systems;

public sealed partial class Scp106System : SharedScp106System
{
    [Dependency] private StairsSystem _stairs = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private SunriseHelpersSystem _helpers = default!;
    [Dependency] private StoreSystem _store = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private StunSystem _stun = default!;
    [Dependency] private DoAfterSystem _doAfter = default!;
    [Dependency] private FearSystem _fear = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly FixedPoint2 EssenceRate = 1f;
    private static readonly TimeSpan AddEssenceCooldown = TimeSpan.FromSeconds(1);

    private const string BackroomsCurrencyPrototype = "LifeEssence";
    private static readonly FixedPoint2 BackroomsEssenceRate = 2f;

    public const int HumansInBackroomsRequiredToAscent = 10;
    public static readonly EntProtoId AscentRule = "Scp106AscentRule";

    private TimeSpan _defaultOnBackroomsStunTime = TimeSpan.FromSeconds(5f);

    private static readonly SoundSpecifier SendBackroomsSound =
        new SoundPathSpecifier("/Audio/_Scp/Scp106/onbackrooms.ogg");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<Scp106Component, MapInitEvent>(OnMapInit);

        SubscribeLocalEvent((Entity<Scp106BackRoomMapComponent> _, ref AttemptGatewayOpenEvent args) => args.Cancelled = true);

        SubscribeLocalEvent<Scp106Component, Scp106TeleportationDelayActionEvent>(OnTeleportationDelay);
        SubscribeLocalEvent<HumanoidProfileComponent, FearCalmDownAttemptEvent>(OnFearCalmDownAttempt);

        InitializeAbilities();
        InitializeStore();
        InitializePhantom();
        InitializePortals();
    }

    private void OnMapInit(Entity<Scp106Component> ent, ref MapInitEvent args)
    {
        _alerts.ShowAlert(ent.Owner, ent.Comp.Scp106EssenceAlert);

        var marks = SearchForMarks();
        if (marks.Count == 0)
            _ = _stairs.GenerateFloor();

        ent.Comp.NextEssenceAddedTime = _timing.CurTime;

        _defaultOnBackroomsStunTime = ent.Comp.TeleportationDuration;
    }

    private void OnFearCalmDownAttempt(Entity<HumanoidProfileComponent> ent, ref FearCalmDownAttemptEvent args)
    {
        if (IsInDimension(ent))
            args.Cancel();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Пополнение маны для 106го
        var queryScp106 = EntityQueryEnumerator<Scp106Component>();
        while (queryScp106.MoveNext(out var uid, out var component))
        {
            if (component.NextEssenceAddedTime > _timing.CurTime)
                continue;

            component.Essence += EssenceRate;
            Dirty(uid, component);

            component.NextEssenceAddedTime = _timing.CurTime + AddEssenceCooldown;
        }

        // Спавн мобов из портала 106
        CheckPortals();
    }

    #region Teleport and related code

    public override async Task SendToBackrooms(EntityUid target, Entity<Scp106Component>? scp106 = null)
    {
        // You already here.
        if (IsInDimension(target))
            return;

        if (TryComp<Scp106Component>(target, out var scp106Component))
        {
            await TeleportToBackroomsInternal(target);
            Scp106FinishTeleportation(target, scp106Component.TeleportationDuration);

            return;
        }

        // Телепортировать только людей
        if (!HasComp<HumanoidProfileComponent>(target))
            return;

        // Не телепортировать трупы
        if (_mobState.IsDead(target) && scp106 != null)
            return;

        await TeleportToBackroomsInternal(target);

        _stun.TryAddParalyzeDuration(target, _defaultOnBackroomsStunTime);
        _fear.TrySetFearLevel(target, FearState.Fear);

        _audio.PlayGlobal(SendBackroomsSound, target);

        if (scp106 != null)
        {
            if (TryComp<SSDIndicatorComponent>(target, out var ssd) && ssd.IsSSD)
                return;

            AddCurrencyInStore(scp106.Value);
            CheckHumansInBackrooms();
        }
    }

    private async Task TeleportToBackroomsInternal(EntityUid target)
    {
        var mark = await GetTransferMark();

        _transform.SetCoordinates(target, mark);
        _transform.AttachToGridOrMap(target);
    }

    private bool CheckHumansInBackrooms()
    {
        var humansInBackrooms = CountHumansInBackrooms();

#if !DEBUG && !TOOLS
        if (humansInBackrooms < HumansInBackroomsRequiredToAscent)
            return false;
#endif

        OnAscent();

        return true;
    }

    private void OnAscent()
    {
        if (_gameTicker.IsGameRuleAdded(AscentRule))
            return;

        _gameTicker.StartGameRule(AscentRule);
    }

    private void Scp106FinishTeleportation(EntityUid uid, TimeSpan teleportationDelay)
    {
        _stun.TryAddStunDuration(uid, teleportationDelay + TeleportTimeCompensation);
        _appearance.SetData(uid, Scp106Visuals.Visuals, Scp106VisualsState.Exiting);

        var doAfterEventArgs = new DoAfterArgs(EntityManager, uid, teleportationDelay, new Scp106TeleportationDelayActionEvent(), uid)
        {
            BreakOnDamage = false,
            BreakOnMove = false,
            RequireCanInteract = false,
        };

        _doAfter.TryStartDoAfter(doAfterEventArgs);
    }

    public override void SendToStation(EntityUid target)
    {
        if (!_helpers.TryFindRandomTile(out _, out _, out _, out var targetCoords))
            return;

        _transform.SetCoordinates(target, targetCoords);
        _transform.AttachToGridOrMap(target);

        if (TryComp<Scp106Component>(target, out var scp106Component))
        {
            HideBlade((target, scp106Component));
            Scp106FinishTeleportation(target, scp106Component.TeleportationDuration);
        }
    }

    private void OnTeleportationDelay(Entity<Scp106Component> ent, ref Scp106TeleportationDelayActionEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        _appearance.SetData(ent, Scp106Visuals.Visuals, Scp106VisualsState.Default);

        args.Handled = true;
    }

    #endregion

    #region Helpers

    public int CountHumansInBackrooms()
    {
        var humansInBackrooms = 0;

        var queryHumans = AllEntityQuery<HumanoidProfileComponent, MobStateComponent>();

        while (queryHumans.MoveNext(out var humanUid, out _, out var mobStateComponent))
        {
            if (!HasComp<Scp106BackRoomMapComponent>(Transform(humanUid).MapUid))
                continue;

            if (mobStateComponent.CurrentState != MobState.Alive)
                continue;

            if (TryComp<SSDIndicatorComponent>(humanUid, out var ssd) && ssd.IsSSD)
                continue;

            humansInBackrooms += 1;
        }

        return humansInBackrooms;
    }

    private async Task<EntityCoordinates> GetTransferMark()
    {
        var marks = SearchForMarks();
        if (marks.Count != 0)
            return _random.Pick(marks);

        // Impossible, but just to be sure.
        await _stairs.GenerateFloor();
        return _random.Pick(SearchForMarks());
    }

    private HashSet<EntityCoordinates> SearchForMarks()
    {
        return _helpers.GetAll<Scp106BackRoomMarkComponent>()
            .Select(entity => Transform(entity.Owner).Coordinates)
            .ToHashSet();
    }

    #endregion
}
