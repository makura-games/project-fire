using System.Numerics;
using Content.Shared._Scp.Animations.Offset;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Scp.Animations.Offset;

public sealed partial class OffsetAnimationSystem : EntitySystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OffsetAnimationComponent, ComponentStartup>(OnStart);
    }

    private void OnStart(Entity<OffsetAnimationComponent> ent, ref ComponentStartup args)
    {
        if (_animation.HasRunningAnimation(ent, ent.Comp.AnimationKey))
            return;

        _animation.Play(ent, GetAnimation(ent), ent.Comp.AnimationKey);
    }

    private static Animation GetAnimation(Entity<OffsetAnimationComponent> ent)
    {
        var length = ent.Comp.AnimationTime;
        return new Animation
        {
            Length = length,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Cubic,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, 0f),
                        new AnimationTrackProperty.KeyFrame(ent.Comp.PeakOffset, ent.Comp.PeakKeyTime),
                        new AnimationTrackProperty.KeyFrame(Vector2.Zero, (float) length.TotalSeconds),
                    },
                },
            },
        };
    }
}
