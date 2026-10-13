using System.Diagnostics.CodeAnalysis;
using Content.Server.Light.EntitySystems;
using Content.Shared._Scp.LightFlicking;
using Content.Shared._Scp.LightFlicking.MalfunctionLight;
using Content.Shared._Scp.Other.Events;
using Content.Shared.Light.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Random;

namespace Content.Server._Scp.LightFlicking;

public sealed partial class LightFlickingSystem : SharedLightFlickingSystem
{
    [Dependency] private PointLightSystem _pointLight = default!;
    [Dependency] private LightBulbSystem _bulb = default!;
    [Dependency] private PoweredLightSystem _poweredLight = default!;

    private const float FlickingStartChance = 0.1f;

    private static readonly TimeSpan FlickCheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FlickCheckVariation = TimeSpan.FromMinutes(15);

    private static readonly Color MalfunctionBulbColor = Color.FromHex("#997e65"); // коричневый

    public override void Initialize()
    {
        base.Initialize();

        InitializeCommands();

        SubscribeLocalEvent<LightFlickingComponent, MapInitEvent>(OnMapInit, after: [typeof(PoweredLightSystem)]);
        SubscribeLocalEvent<ActiveLightFlickingComponent, MapInitEvent>(OnActiveMapInit, after: [typeof(PoweredLightSystem)]);

        SubscribeLocalEvent<ActiveLightFlickingComponent, LightEjectEvent>(OnLightEject);
        SubscribeLocalEvent<LightFlickingComponent, LightInsertEvent>(OnLightInsert);
    }

    #region Event handlers

    private void OnMapInit(Entity<LightFlickingComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<PoweredLightComponent>(ent))
            return;

        SetNextFlickingStartTime(ent);
    }

    private void OnActiveMapInit(Entity<ActiveLightFlickingComponent> ent, ref MapInitEvent args)
    {
        var light = _pointLight.EnsureLight(ent);
        ent.Comp.CachedRadius = light.Radius;
        ent.Comp.CachedEnergy = light.Energy;

        if (TryGetBulbEntity(ent, out var bulb))
            ent.Comp.CachedColor = bulb.Value.Comp.Color;

        Dirty(ent);

        SetNextFlickingTime(ent);
    }

    private void OnLightEject(Entity<ActiveLightFlickingComponent> ent, ref LightEjectEvent args)
    {
        RemComp<ActiveLightFlickingComponent>(ent);
    }

    private void OnLightInsert(Entity<LightFlickingComponent> ent, ref LightInsertEvent args)
    {
        if (!HasComp<MalfunctionLightComponent>(args.Bulb))
            return;

        EnsureComp<ActiveLightFlickingComponent>(ent);
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = AllEntityQuery<LightFlickingComponent, PoweredLightComponent>();

        // Обработка "поврждения" лампочек для пометки их как мигающие
        while (query.MoveNext(out var uid, out var flicking, out _))
        {
            if (Timing.CurTime <= flicking.NextFlickStartChanceTime)
                continue;

            if (HasComp<ActiveLightFlickingComponent>(uid))
                continue;

            if (!Random.Prob(FlickingStartChance))
            {
                SetNextFlickingStartTime((uid, flicking));
                continue;
            }

            TryStartFlicking((uid, flicking));
        }
    }

    private bool TryStartFlicking(Entity<LightFlickingComponent> ent)
    {
        if (!TryMalfunctionBulb(ent))
            return false;

        AddComp<ActiveLightFlickingComponent>(ent);

        return true;
    }

    private bool TryGetBulb(EntityUid lightUid, [NotNullWhen(true)] out EntityUid? bulb)
    {
        bulb = _poweredLight.GetBulb(lightUid);

        if (!bulb.HasValue)
            return false;

        bulb = bulb.Value;

        return true;
    }

    private bool TryGetBulbEntity(EntityUid lightUid, [NotNullWhen(true)] out Entity<LightBulbComponent>? bulb)
    {
        bulb = null;

        if (!TryGetBulb(lightUid, out var bulbUid))
            return false;

        if (!TryComp<LightBulbComponent>(bulbUid, out var lightBulbComponent))
            return false;

        bulb = (bulbUid.Value, lightBulbComponent);

        return true;
    }

    #region Flicking start time

    private void SetNextFlickingStartTime(Entity<LightFlickingComponent> ent)
    {
        var variation = FlickCheckInterval - Random.Next(FlickCheckVariation);
        ent.Comp.NextFlickStartChanceTime = Timing.CurTime + variation;
    }

    private bool TryMalfunctionBulb(EntityUid lightUid)
    {
        if (!TryGetBulb(lightUid, out var bulb))
            return false;

        if (HasComp<MalfunctionLightComponent>(bulb.Value))
            return false;

        // TODO: Пофиксить, что после вставления лампы она принимает этот коричневый цвет, вместо нужного оригинального
        _bulb.SetColor(bulb.Value, MalfunctionBulbColor);
        AddComp<MalfunctionLightComponent>(bulb.Value);

        return true;
    }

    #endregion

}
