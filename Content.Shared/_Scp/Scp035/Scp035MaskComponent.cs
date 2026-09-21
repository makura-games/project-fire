using System.Numerics;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost.Roles.Raffles;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Scp.Scp035;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Scp035MaskComponent : Component
{
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? User;

    [DataField]
    public TimeSpan NextMessageDelay = TimeSpan.FromSeconds(60);

    [DataField]
    public List<LocId> Messages = [];

    [DataField]
    public ProtoId<ReagentPrototype> ReagentName = "Scp035Reagent";

    [DataField]
    public float ReagentRangeAvailable = 5;

    [DataField]
    public int ReagentDestructLevel = 200;

    [DataField]
    public TimeSpan NextLiquidSpawnDelay = TimeSpan.FromSeconds(300);

    [DataField]
    public Vector2 CorrosionBox = new (6f, 3f);

    [DataField]
    public int MaxTilesCorrosionPry = 10;

    [DataField]
    public float EntityCorrosionRange = 3f;

    [DataField]
    public DamageSpecifier DamageSpecif = new()
    {
        DamageDict = new() { { "Structural", 60f } }
    };

    [DataField]
    public EntityWhitelist? WhitelistStructures;

    [DataField]
    public EntityWhitelist? BlacklistStructures;

    [DataField]
    public SoundSpecifier? EquipSound = new SoundCollectionSpecifier("EquipScp035");

    [DataField]
    public TimeSpan EquippedParalyzeDuration = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan EquipAttemptParalyzeDuration = TimeSpan.FromSeconds(5);

    [DataField]
    public EntProtoId SpawnWeaponProto = "Chainsaw";

    [DataField]
    public ProtoId<NpcFactionPrototype> NewUserFaction = "SimpleHostile";

    [DataField]
    public FixedPoint2 NewCriticalThreshold = FixedPoint2.New(790);

    [DataField]
    public FixedPoint2 NewDeadThreshold = FixedPoint2.New(800);

    [DataField]
    public float ImpulseModificator = 10000;

    [DataField]
    public GhostRoleRaffleSettings GhostSettings = new GhostRoleRaffleSettings()
    {
        InitialDuration = 5,
        JoinExtendsDurationBy = 2,
        MaxDuration = 10
    };

    [DataField]
    public List<EntProtoId> Actions = new()
    {
        "ActionScp035RaiseArmy",
        "ActionScp035Stun"
    };

    [DataField]
    public Dictionary<MaskOrderType, EntProtoId> OrderActions = new()
    {
        { MaskOrderType.Stay, "ActionScp035OrderStay" },
        { MaskOrderType.Follow, "ActionScp035OrderFollow" },
        { MaskOrderType.Kill, "ActionScp035OrderKill" },
        { MaskOrderType.Loose, "ActionScp035OrderLoose" }
    };

    [ViewVariables]
    public TimeSpan NextMessaging = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextLiquidSpawning = TimeSpan.Zero;
}
