using Content.Shared._Scp.Animations.Scale;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._Scp.Animations.Scale;

/// <summary>
/// Система, обрабатывающая анимацию увеличения размера спрайта при появлении сущности.
/// Основывается на <see cref="ScaleAnimationComponent"/>
/// </summary>
public sealed partial class ScaleAnimationSystem : SharedScaleAnimationSystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ScaleAnimationStartEvent>(OnStart);
    }

    private void OnStart(ScaleAnimationStartEvent args)
    {
        var ent = GetEntity(args.Entity);
        if (!TryComp<ScaleAnimationComponent>(ent, out var scale))
            return;

        if (_animation.HasRunningAnimation(ent, scale.AnimationKey))
            return;

        var sprite = Comp<SpriteComponent>(ent);
        _animation.Play(ent, GetPuddleAnimation((ent, scale), sprite), scale.AnimationKey);
    }

    private static Animation GetPuddleAnimation(Entity<ScaleAnimationComponent> ent, SpriteComponent component)
    {
        var length = ent.Comp.Duration;
        var scale = component.Scale;

        return new Animation
        {
            Length = length,
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(ent.Comp.InitialSize, 0f),
                        new AnimationTrackProperty.KeyFrame(scale, (float) length.TotalSeconds),
                    },
                },
            },
        };
    }
}
