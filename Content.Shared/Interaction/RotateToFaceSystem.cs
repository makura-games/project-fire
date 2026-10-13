using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Rotatable;
using JetBrains.Annotations;

namespace Content.Shared.Interaction
{
    /// <summary>
    /// Contains common code used to rotate a player to face a given target or direction.
    /// This interaction in itself is useful for various roleplay purposes.
    /// But it needs specialized code to handle chairs and such.
    /// Doesn't really fit with SharedInteractionSystem so it's not there.
    /// </summary>
    [UsedImplicitly]
    public sealed partial class RotateToFaceSystem : EntitySystem
    {
        [Dependency] private ActionBlockerSystem _actionBlockerSystem = default!;
        [Dependency] private SharedTransformSystem _transform = default!;

        // Fire edit start - для осматривания на стульях
        private EntityQuery<StrapComponent> _strapQuery;
        private EntityQuery<BuckleComponent> _buckleQuery;

        public override void Initialize()
        {
            base.Initialize();

            _strapQuery = GetEntityQuery<StrapComponent>();
            _buckleQuery = GetEntityQuery<BuckleComponent>();
        }
        // Fire edit end

        /// <summary>
        /// Tries to rotate the entity towards the target rotation. Returns false if it needs to keep rotating.
        /// </summary>
        public bool TryRotateTo(EntityUid uid,
            Angle goalRotation,
            float frameTime,
            Angle tolerance,
            double rotationSpeed = float.MaxValue,
            TransformComponent? xform = null)
        {
            if (!Resolve(uid, ref xform))
                return true;

            // If we have a max rotation speed then do that.
            // We'll rotate even if we can't shoot, looks better.
            if (rotationSpeed < float.MaxValue)
            {
                var worldRot = _transform.GetWorldRotation(xform);

                var rotationDiff = Angle.ShortestDistance(worldRot, goalRotation).Theta;
                var maxRotate = rotationSpeed * frameTime;

                if (Math.Abs(rotationDiff) > maxRotate)
                {
                    var goalTheta = worldRot + Math.Sign(rotationDiff) * maxRotate;
                    TryFaceAngle(uid, goalTheta, xform);
                    rotationDiff = (goalRotation - goalTheta);

                    if (Math.Abs(rotationDiff) > tolerance)
                    {
                        return false;
                    }

                    return true;
                }

                TryFaceAngle(uid, goalRotation, xform);
            }
            else
            {
                TryFaceAngle(uid, goalRotation, xform);
            }

            return true;
        }

        public bool TryFaceCoordinates(EntityUid user, Vector2 coordinates, TransformComponent? xform = null)
        {
            if (!Resolve(user, ref xform))
                return false;

            var diff = coordinates - _transform.GetMapCoordinates(user, xform: xform).Position;
            if (diff.LengthSquared() <= 0.01f)
                return true;

            var diffAngle = Angle.FromWorldVec(diff);
            return TryFaceAngle(user, diffAngle);
        }

        public bool TryFaceAngle(EntityUid user, Angle diffAngle, TransformComponent? xform = null)
        {
            if (!_actionBlockerSystem.CanChangeDirection(user))
                return false;

            // Fire edit start - для осматривания на стульях
            if (_buckleQuery.TryComp(user, out var buckle) && buckle.BuckledTo is { } strap)
            {
                if (_strapQuery.TryComp(strap, out var strapComp) && strapComp.AllowRotation)
                {
                    var baseRotation = _transform.GetWorldRotation(strap);
                    var delta = Angle.ShortestDistance(baseRotation, diffAngle);
                    var clampedTheta = Math.Clamp(delta.Theta, -strapComp.MaxAngle.Theta, strapComp.MaxAngle.Theta);

                    var finalAngle = baseRotation + clampedTheta;

                    if (!Resolve(user, ref xform))
                        return false;

                    _transform.SetWorldRotation(xform, finalAngle);
                    return true;
                }

                if (TryComp<RotatableComponent>(strap, out var rotatable) && rotatable.RotateWhileAnchored)
                {
                    _transform.SetWorldRotation(Transform(strap), diffAngle);
                    return true;
                }

                return false;
            }
            // Fire edit end

            if (!Resolve(user, ref xform))
                return false;

            _transform.SetWorldRotation(xform, diffAngle);
            return true;
        }
    }
}
