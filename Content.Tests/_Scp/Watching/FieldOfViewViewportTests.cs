#nullable enable
using System.Numerics;
using Content.Client._Scp.Shaders.FieldOfView;
using Content.Client._Scp.Shaders.FieldOfView.Overlays;
using Content.Client.Viewport;
using Moq;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Tests._Scp.Watching;

[TestFixture]
public sealed class FieldOfViewViewportTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ThePlayersEyeReceivesBothFovPasses(bool includeLowerLevels)
    {
        var eye = new Eye();

        Assert.That(FieldOfViewOverlayManagementSystem.IsPlayerEye(eye, eye, includeLowerLevels), Is.True);
    }

    [TestCase(-5, true, true)]
    [TestCase(-1, true, true)]
    [TestCase(1, true, true)]
    [TestCase(-5, false, false)]
    [TestCase(-1, false, false)]
    [TestCase(1, false, true)]
    public void ZLevelSpritesAreOccludedButTheConeOnlyRunsAboveThePlayer(
        int depth, bool includeLowerLevels, bool expected)
    {
        var playerEye = new Eye();
        var zEye = new ScalingViewport.ZEye(-5, depth, 1) { SourceEye = playerEye };

        Assert.That(FieldOfViewOverlayManagementSystem.IsPlayerEye(zEye, playerEye, includeLowerLevels),
            Is.EqualTo(expected));
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void AnotherPlayersZLevelEyeDoesNotReceiveTheLocalFov(int depth)
    {
        var playerEye = new Eye();
        var zEye = new ScalingViewport.ZEye(-1, depth, 1) { SourceEye = new Eye() };

        Assert.That(FieldOfViewOverlayManagementSystem.IsPlayerEye(zEye, playerEye), Is.False);
    }

    [Test]
    public void UnrelatedAndMissingEyesDoNotReceiveTheLocalFov()
    {
        var eye = new Eye();

        Assert.Multiple(() =>
        {
            Assert.That(FieldOfViewOverlayManagementSystem.IsPlayerEye(new Eye(), eye), Is.False);
            Assert.That(FieldOfViewOverlayManagementSystem.IsPlayerEye(null, eye), Is.False);
            Assert.That(FieldOfViewOverlayManagementSystem.IsPlayerEye(new ScalingViewport.ZEye(-1, 1, 1), eye),
                Is.False);
        });
    }

    [TestCase(-1, 0f)]
    [TestCase(1, 0f)]
    [TestCase(-1, 90f)]
    [TestCase(1, 90f)]
    public void TheConeCenterUsesTheZViewportsProjection(int depth, float rotationDegrees)
    {
        var playerPosition = new Vector2(8f, 12f);
        var eye = new ScalingViewport.ZEye(-1, depth, 1)
        {
            Position = new MapCoordinates(playerPosition, new MapId(1000 + depth)),
            Offset = new Vector2(0.25f, depth * 0.7f),
            Rotation = Angle.FromDegrees(rotationDegrees),
            Zoom = new Vector2(2f),
        };
        var viewport = new Mock<IClydeViewport>();
        var size = new Vector2i(800, 600);
        var scale = new Vector2(2f);
        viewport.SetupGet(v => v.Size).Returns(size);
        viewport.Setup(v => v.WorldToLocal(It.IsAny<Vector2>())).Returns((Vector2 position) =>
        {
            eye.GetViewMatrix(out var matrix, scale);
            return Vector2.Transform(position, matrix) * new Vector2(1f, -1f) * EyeManager.PixelsPerMeter + size / 2f;
        });

        var expected = -eye.Rotation.RotateVec(eye.Offset) * eye.Scale * scale * EyeManager.PixelsPerMeter;
        var offset = FieldOfViewConeOverlay.GetZLevelOffset(viewport.Object, playerPosition);

        Assert.Multiple(() =>
        {
            Assert.That(offset.X, Is.EqualTo(expected.X).Within(0.001f));
            Assert.That(offset.Y, Is.EqualTo(expected.Y).Within(0.001f));
        });
    }
}
