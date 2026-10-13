using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.Timing;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Robust.Shared.Physics.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Content.Server.Doors.Systems;

namespace Content.Server._Scp.ComplexElevator;

public sealed partial class ComplexElevatorSystem : EntitySystem
{
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DoorSystem _doorSystem = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private PointLightSystem _pointLight = default!;

    private static readonly Color ElevatorButtonGreen = Color.FromHex("#00FF00");
    private static readonly Color ElevatorButtonYellow = Color.FromHex("#FFFF00");
    private static readonly Color ElevatorButtonRed = Color.FromHex("#FF0000");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ElevatorButtonComponent, InteractHandEvent>(OnButtonInteract);
        SubscribeLocalEvent<ElevatorButtonComponent, ActivateInWorldEvent>(OnButtonActivate);
    }

    private TimeSpan GetButtonUseDelay(Entity<ComplexElevatorComponent> elevator, ElevatorButtonComponent button)
    {
        return elevator.Comp.SendDelay + elevator.Comp.IntermediateDelay + TimeSpan.FromSeconds(1);
    }

    private void SetButtonDelay(EntityUid button, Entity<ComplexElevatorComponent> elevator)
    {
        if (TryComp<UseDelayComponent>(button, out var useDelay))
        {
            _useDelay.SetLength((button, useDelay), GetButtonUseDelay(elevator, Comp<ElevatorButtonComponent>(button)));
        }
    }

    private void StartMovement(Entity<ComplexElevatorComponent> ent, string targetFloor)
    {
        KillEntitiesInTargetArea(ent, ent.Comp.IntermediateFloorId);
        ent.Comp.CurrentFloor = ent.Comp.IntermediateFloorId;
        TeleportToFloor(ent, ent.Comp.IntermediateFloorId);

        _audio.PlayPvs(ent.Comp.TravelSound, ent);
        Timer.Spawn(ent.Comp.IntermediateDelay, () =>
        {
            if (!Exists(ent))
                return;

            KillEntitiesInTargetArea(ent, targetFloor);
            ent.Comp.CurrentFloor = targetFloor;
            TeleportToFloor(ent, targetFloor);
            OpenDoorsForFloor(ent.Comp.ElevatorId, targetFloor);

            _audio.PlayPvs(ent.Comp.ArrivalSound, ent);

            ent.Comp.IsMoving = false;
            UpdateButtonLights(ent);
        });
    }

    private void TeleportToFloor(EntityUid uid, string floorId)
    {
        var query = EntityQueryEnumerator<ElevatorPointComponent>();
        while (query.MoveNext(out var pointUid, out var pointComp))
        {
            if (pointComp.FloorId != floorId)
                continue;

            var pointTransform = Transform(pointUid);
            var elevatorTransform = Transform(uid);

            var aabb = _lookup.GetWorldAABB(uid, elevatorTransform);
            var intersectingEntities = _lookup.GetEntitiesIntersecting(elevatorTransform.MapID, aabb, LookupFlags.Dynamic | LookupFlags.Sensors);

            var entitiesToTeleport = new List<(EntityUid, Vector2)>();
            foreach (var entUid in intersectingEntities)
            {
                if (entUid == uid || HasComp<ElevatorDoorComponent>(entUid))
                    continue;

                var entTransform = Transform(entUid);
                var relativePos = entTransform.LocalPosition - elevatorTransform.LocalPosition;
                entitiesToTeleport.Add((entUid, relativePos));
            }

            _transform.SetCoordinates(uid, pointTransform.Coordinates);

            var newElevatorTransform = Transform(uid);

            foreach (var (entUid, relativePos) in entitiesToTeleport)
            {
                var newCoordinates = new EntityCoordinates(newElevatorTransform.ParentUid, newElevatorTransform.LocalPosition + relativePos);
                _transform.SetCoordinates(entUid, newCoordinates);
            }

            break;
        }
    }

    private void HandleButtonPress(Entity<ElevatorButtonComponent> button, Entity<ComplexElevatorComponent> elevator)
    {
        if (elevator.Comp.IsMoving)
            return;
        {
            switch (button.Comp.ButtonType)
            {
                case ElevatorButtonType.CallButton:
                    MoveToFloor(elevator, button.Comp.Floor);
                    break;
                case ElevatorButtonType.SendElevatorUp:
                    MoveUp(elevator);
                    break;
                case ElevatorButtonType.SendElevatorDown:
                    MoveDown(elevator);
                    break;
            }
            SetButtonDelay(button, elevator);
        }
    }

    private void OnButtonInteract(Entity<ElevatorButtonComponent> ent, ref InteractHandEvent args)
    {
        if (!TryFindElevator(ent.Comp.ElevatorId, out var elevator))
            return;

        HandleButtonPress(ent, elevator.Value);
        args.Handled = true;
    }

    private void OnButtonActivate(Entity<ElevatorButtonComponent> ent, ref ActivateInWorldEvent args)
    {
        if (!TryFindElevator(ent.Comp.ElevatorId, out var elevator))
            return;

        HandleButtonPress(ent, elevator.Value);
        args.Handled = true;
    }

    private bool TryFindElevator(string elevatorId, [NotNullWhen(true)] out Entity<ComplexElevatorComponent>? ent)
    {
        var query = EntityQueryEnumerator<ComplexElevatorComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ElevatorId == elevatorId)
            {
                ent = (uid, comp);
                return true;
            }
        }
        ent = null;
        return false;
    }

    public void MoveToFloor(Entity<ComplexElevatorComponent> ent, string targetFloor)
    {
        if (ent.Comp.IsMoving || !ent.Comp.Floors.Contains(targetFloor) || ent.Comp.CurrentFloor == targetFloor)
            return;

        if (!CanCloseDoorsForFloor(ent.Comp.ElevatorId, ent.Comp.CurrentFloor))
            return;

        ent.Comp.IsMoving = true;
        UpdateButtonLights(ent);

        Timer.Spawn(ent.Comp.SendDelay, () =>
        {
            if (!Exists(ent) || !ent.Comp.IsMoving)
                return;

            StartMovement(ent, targetFloor);
        });

        Timer.Spawn(ent.Comp.DoorCloseDelay, () =>
        {
            if (!Exists(ent) || !ent.Comp.IsMoving)
                return;

            if (!CanCloseDoorsForFloor(ent.Comp.ElevatorId, ent.Comp.CurrentFloor))
            {
                ent.Comp.IsMoving = false;
                UpdateButtonLights(ent);
            }
            else
            {
                TryCloseDoorsForFloor(ent.Comp.ElevatorId, ent.Comp.CurrentFloor);
            }
        });
    }

    public void MoveUp(Entity<ComplexElevatorComponent> ent)
    {
        var nextFloor = GetNextFloorUp(ent);
        if (nextFloor != null)
            MoveToFloor(ent, nextFloor);
    }

    public void MoveDown(Entity<ComplexElevatorComponent> ent)
    {
        var nextFloor = GetNextFloorDown(ent);
        if (nextFloor != null)
            MoveToFloor(ent, nextFloor);
    }

    private string? GetNextFloor(Entity<ComplexElevatorComponent> ent, bool up)
    {
        if (ent.Comp.IsMoving || ent.Comp.Floors.Count == 0)
            return null;

        var currentIndex = ent.Comp.Floors.IndexOf(ent.Comp.CurrentFloor);
        if (currentIndex == -1)
            return null;

        if (up)
        {
            if (currentIndex <= 0)
                return null;
            return ent.Comp.Floors[currentIndex - 1];
        }
        else
        {
            if (currentIndex >= ent.Comp.Floors.Count - 1)
                return null;
            return ent.Comp.Floors[currentIndex + 1];
        }
    }

    private string? GetNextFloorUp(Entity<ComplexElevatorComponent> ent)
    {
        return GetNextFloor(ent, true);
    }

    private string? GetNextFloorDown(Entity<ComplexElevatorComponent> ent)
    {
        return GetNextFloor(ent, false);
    }

    private void OpenDoorsForFloor(string elevatorId, string floor)
    {
        var query = EntityQueryEnumerator<ElevatorDoorComponent>();
        while (query.MoveNext(out var doorUid, out var doorComp))
        {
            if (doorComp.ElevatorId != elevatorId || doorComp.Floor != floor)
                continue;
            _doorSystem.TryOpen(doorUid);
        }
    }

    private bool CanCloseDoorsForFloor(string elevatorId, string floor)
    {
        if (!TryFindElevator(elevatorId, out var elevator))
            return true;

        var query = EntityQueryEnumerator<ElevatorDoorComponent>();
        while (query.MoveNext(out var doorUid, out var doorComp))
        {
            if (doorComp.ElevatorId != elevatorId || doorComp.Floor != floor)
                continue;
            if (IsDoorBlocked(doorUid, elevator.Value.Comp.DoorBlockCheckRange))
                return false;
        }
        return true;
    }

    private bool TryCloseDoorsForFloor(string elevatorId, string floor)
    {
        if (!TryFindElevator(elevatorId, out var elevator))
            return false;

        EntityUid? lastDoor = null;
        var query = EntityQueryEnumerator<ElevatorDoorComponent>();
        while (query.MoveNext(out var doorUid, out var doorComp))
        {
            if (doorComp.ElevatorId != elevatorId || doorComp.Floor != floor)
                continue;
            if (!_doorSystem.TryClose(doorUid))
                return false;
            lastDoor = doorUid;
        }

        if (lastDoor.HasValue)
        {
            _audio.PlayPvs(elevator.Value.Comp.StartSound, lastDoor.Value);
        }

        return true;
    }

    private bool IsDoorBlocked(EntityUid doorUid, float range)
    {
        if (Deleted(doorUid))
            return false;

        var intersectingEntities = _lookup.GetEntitiesInRange<PhysicsComponent>(Transform(doorUid).Coordinates, range, LookupFlags.Dynamic);
        foreach (var ent in intersectingEntities)
        {
            if (ent.Owner != doorUid && !HasComp<ElevatorDoorComponent>(ent.Owner))
                return true;
        }
        return false;
    }

    private void KillEntitiesInTargetArea(Entity<ComplexElevatorComponent> elevator, string floorId)
    {
        var query = EntityQueryEnumerator<ElevatorPointComponent>();
        while (query.MoveNext(out var pointUid, out var pointComp))
        {
            if (pointComp.FloorId != floorId)
                continue;

            var pointTransform = Transform(pointUid);

            var aabb = _lookup.GetWorldAABB(elevator.Owner, pointTransform);
            var intersectingEntities = _lookup.GetEntitiesIntersecting(pointTransform.MapID, aabb, LookupFlags.Dynamic | LookupFlags.Sensors);

            foreach (var entUid in intersectingEntities)
            {
                if (entUid == elevator.Owner)
                    continue;

                var damage = new DamageSpecifier();
                damage.DamageDict["Blunt"] = 2000;
                _damageable.TryChangeDamage(entUid, damage, true);
            }
            break;
        }
    }

    private void UpdateButtonLights(Entity<ComplexElevatorComponent> elevator)
    {
        var query = EntityQueryEnumerator<ElevatorButtonComponent, PointLightComponent>();
        while (query.MoveNext(out var buttonUid, out var buttonComp, out var light))
        {
            if (buttonComp.ElevatorId != elevator.Comp.ElevatorId)
                continue;

            Color color = ElevatorButtonRed;
            if (buttonComp.ButtonType == ElevatorButtonType.CallButton)
            {
                if (elevator.Comp.IsMoving)
                    color = ElevatorButtonYellow;
                else if (buttonComp.Floor == elevator.Comp.CurrentFloor)
                    color = ElevatorButtonGreen;
                else
                    color = ElevatorButtonRed;
            }

            _pointLight.SetColor(buttonUid, color, light);
        }
    }
}


