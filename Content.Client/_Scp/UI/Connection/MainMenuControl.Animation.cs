

using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Sunrise.Lobby;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Client.Graphics;

// ReSharper disable once CheckNamespace
namespace Content.Client.MainMenu.UI;

public sealed partial class MainMenuControl
{
    [Dependency] private IResourceCache _resource = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private const string AnimationId = "DeepFacility";

    private RSI.State? _animationState;
    private float _animationTime;
    private int _animationFrame;

    private RSIResource? _rsiResource;

    protected override void EnteredTree()
    {
        base.EnteredTree();

        SetAnimation();

        var logoTexture = _resource.GetTexture("/Textures/_Scp/Logo/logo-hollow.png");
        Logo.Texture = logoTexture;
        Logo.TextureScale = new Vector2(0.125f, 0.125f);
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _rsiResource?.Dispose();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_animationState == null)
            return;

        _animationTime += args.DeltaSeconds;

        var delay = _animationState.GetDelay(_animationFrame);
        if (_animationTime >= delay)
        {
            _animationTime -= delay;
            _animationFrame++;

            if (_animationFrame >= _animationState.Icons[0].Length)
                _animationFrame = 0;

            ConnectionAnimation.DisplayRect.Texture = _animationState.GetFrame(RsiDirection.South, _animationFrame);
        }
    }

    private void SetAnimation()
    {
        if (!_prototype.TryIndex<LobbyAnimationPrototype>(AnimationId, out var lobbyAnimationPrototype))
            return;

        if (!_resource.TryGetResource(new ResPath(lobbyAnimationPrototype.Animation).ToRootedPath(), out _rsiResource))
            return;

        if (!_rsiResource.RSI.TryGetState(lobbyAnimationPrototype.State, out var state))
            return;

        _animationState = state;
        _animationFrame = 0;
        _animationTime = 0;

        ConnectionAnimation.DisplayRect.Texture = _animationState.GetFrame(RsiDirection.South, 0);
        ConnectionAnimation.DisplayRect.TextureScale = lobbyAnimationPrototype.Scale;
        ConnectionAnimation.DisplayRect.Stretch = TextureRect.StretchMode.Scale;
    }
}
