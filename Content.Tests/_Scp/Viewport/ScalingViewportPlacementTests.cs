using System;
using System.Numerics;
using Content.Client.Viewport;
using Moq;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Client.Placement;
using Robust.Client.Placement.Modes;
using Robust.Shared.Graphics;
using Robust.Shared.Map;

namespace Content.Tests._Scp.Viewport;

[TestFixture]
[TestOf(typeof(ScalingViewport))]
public sealed class ScalingViewportPlacementTests
{
    [TestCase(1000)]
    [TestCase(1001)]
    public void PrimaryMapKeepsPlacementVisible(int mapId)
    {
        var eye = new Eye { Position = new MapCoordinates(Vector2.Zero, new MapId(mapId)) };

        AssertPlacementDuringRender(eye, new MapId(mapId), true);
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void AdjacentZLevelHidesPlacement(int depth)
    {
        var eye = new ScalingViewport.ZEye(-1, depth, 1)
        {
            Position = new MapCoordinates(Vector2.Zero, new MapId(1000 + depth)),
        };

        AssertPlacementDuringRender(eye, new MapId(1000), false);
    }

    [Test]
    public void ZLevelEyeHidesPlacementEvenOnMatchingMap()
    {
        var eye = new ScalingViewport.ZEye(-1, -1, 0)
        {
            Position = new MapCoordinates(Vector2.Zero, new MapId(1000)),
        };

        AssertPlacementDuringRender(eye, new MapId(1000), false);
    }

    [TestCase(1000, 1001)]
    [TestCase(1001, 1000)]
    public void StalePlacementOnAnotherMapIsHidden(int viewportMapId, int placementMapId)
    {
        var eye = new Eye { Position = new MapCoordinates(Vector2.Zero, new MapId(viewportMapId)) };

        AssertPlacementDuringRender(eye, new MapId(placementMapId), false);
    }

    [Test]
    public void MissingPlacementMapHidesPlacement()
    {
        var eye = new Eye { Position = new MapCoordinates(Vector2.Zero, new MapId(1000)) };

        AssertPlacementDuringRender(eye, null, false);
    }

    [Test]
    public void MissingEyeHidesPlacement()
    {
        AssertPlacementDuringRender(null, new MapId(1000), false);
    }

    [Test]
    public void RenderExceptionRestoresPlacement()
    {
        var mode = new SnapgridCenter(null);
        var placement = new Mock<IPlacementManager>();
        placement.SetupProperty(p => p.CurrentMode, mode);
        var viewport = new Mock<IClydeViewport>();
        viewport.SetupGet(v => v.Eye).Returns(new ScalingViewport.ZEye(-1, -1, 0));
        PlacementMode observedMode = mode;
        viewport.Setup(v => v.Render()).Callback(() =>
        {
            observedMode = placement.Object.CurrentMode;
            throw new InvalidOperationException();
        });

        Assert.Throws<InvalidOperationException>(() =>
            ScalingViewport.RenderZLevelViewport(viewport.Object, placement.Object, new MapId(1000)));

        Assert.That(observedMode, Is.Null);
        Assert.That(placement.Object.CurrentMode, Is.SameAs(mode));
    }

    [Test]
    public void ViewportStillRendersWithoutPlacementMode()
    {
        var placement = new Mock<IPlacementManager>();
        placement.SetupProperty(p => p.CurrentMode, null);
        var viewport = new Mock<IClydeViewport>();
        var rendered = false;
        viewport.Setup(v => v.Render()).Callback(() => rendered = true);

        ScalingViewport.RenderZLevelViewport(viewport.Object, placement.Object, null);

        Assert.That(rendered, Is.True);
        Assert.That(placement.Object.CurrentMode, Is.Null);
    }

    private static void AssertPlacementDuringRender(IEye eye, MapId? placementMap, bool expectedVisible)
    {
        var mode = new SnapgridCenter(null);
        var placement = new Mock<IPlacementManager>();
        placement.SetupProperty(p => p.CurrentMode, mode);
        var viewport = new Mock<IClydeViewport>();
        viewport.SetupGet(v => v.Eye).Returns(eye);
        PlacementMode observedMode = expectedVisible ? null : mode;
        viewport.Setup(v => v.Render()).Callback(() => observedMode = placement.Object.CurrentMode);

        ScalingViewport.RenderZLevelViewport(viewport.Object, placement.Object, placementMap);

        if (expectedVisible)
            Assert.That(observedMode, Is.SameAs(mode));
        else
            Assert.That(observedMode, Is.Null);
        Assert.That(placement.Object.CurrentMode, Is.SameAs(mode));
    }
}
