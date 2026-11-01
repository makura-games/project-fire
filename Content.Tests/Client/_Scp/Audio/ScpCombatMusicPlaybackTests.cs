using System;
using Content.Client._Scp.Audio;
using NUnit.Framework;

namespace Content.Tests.Client._Scp.Audio;

[TestFixture]
public sealed class ScpCombatMusicPlaybackTests
{
    [Test]
    public void EnteringCombatStartsOnceAndFadesIn()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();

        Assert.That(playback.SetCombatMode(true, TimeSpan.Zero, false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Start));
        Assert.That(playback.Gain, Is.Zero);

        Assert.That(playback.SetCombatMode(true, Seconds(0.5), true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Gain, Is.EqualTo(0.5f).Within(0.0001f));

        playback.Update(Seconds(1), true);
        Assert.That(playback.Gain, Is.EqualTo(1f));
    }

    [Test]
    public void ReenteringAfterNineSecondsKeepsTheOriginalTimeline()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();
        var trackStartedAt = TimeSpan.Zero;
        Assert.That(playback.SetCombatMode(true, trackStartedAt, false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Start));

        Assert.That(playback.SetCombatMode(false, Seconds(10), true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Gain, Is.EqualTo(1f));

        playback.Update(Seconds(11), true);
        Assert.That(playback.Gain, Is.EqualTo(0.5f).Within(0.0001f));
        playback.Update(Seconds(12), true);
        Assert.That(playback.Gain, Is.Zero);

        var returnedAt = Seconds(19);
        Assert.That(playback.SetCombatMode(true, returnedAt, true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Gain, Is.Zero);

        playback.Update(Seconds(19.5), true);
        Assert.That(playback.Gain, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(playback.Update(Seconds(20), true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Gain, Is.EqualTo(1f));
    }

    [TestCase(19.999, ScpCombatMusicPlaybackAction.None)]
    [TestCase(20, ScpCombatMusicPlaybackAction.Start)]
    [TestCase(20.001, ScpCombatMusicPlaybackAction.Start)]
    public void ReturningAtTheRetentionBoundarySelectsTheCorrectTrack(
        double returnedAt,
        ScpCombatMusicPlaybackAction expected)
    {
        var playback = new ScpCombatMusicPlayback();
        playback.SetCombatMode(true, TimeSpan.Zero, false);
        playback.SetCombatMode(false, Seconds(10), true);

        Assert.That(playback.SetCombatMode(true, Seconds(returnedAt), true), Is.EqualTo(expected));
    }

    [Test]
    public void RetentionExpiryStopsTheStreamExactlyOnce()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();
        playback.SetCombatMode(true, TimeSpan.Zero, false);
        playback.SetCombatMode(false, Seconds(10), true);

        Assert.That(playback.Update(Seconds(19.999), true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Update(Seconds(20), true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Stop));
        Assert.That(playback.Gain, Is.Zero);
        Assert.That(playback.Update(Seconds(21), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
    }

    [Test]
    public void NaturalTrackEndDoesNotRestartUntilCombatIsToggled()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();
        playback.SetCombatMode(true, TimeSpan.Zero, false);

        Assert.That(playback.Update(Seconds(30), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Stop));
        Assert.That(playback.SetCombatMode(true, Seconds(31), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Update(Seconds(32), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.Gain, Is.Zero);

        playback.SetCombatMode(false, Seconds(33), false);
        Assert.That(playback.SetCombatMode(true, Seconds(34), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Start));
    }

    [Test]
    public void ATrackThatEndsWhileMutedCannotBeResumed()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();
        playback.SetCombatMode(true, TimeSpan.Zero, false);
        playback.SetCombatMode(false, Seconds(10), true);

        Assert.That(playback.Update(Seconds(15), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Stop));
        Assert.That(playback.SetCombatMode(true, Seconds(19), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Start));
        Assert.That(playback.Gain, Is.Zero);
    }

    [Test]
    public void RapidTogglesFadeFromTheCurrentGain()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();
        playback.SetCombatMode(true, TimeSpan.Zero, false);

        playback.SetCombatMode(false, Seconds(0.5), true);
        Assert.That(playback.Gain, Is.EqualTo(0.5f).Within(0.0001f));
        playback.SetCombatMode(true, Seconds(1), true);
        Assert.That(playback.Gain, Is.EqualTo(0.375f).Within(0.0001f));
        playback.SetCombatMode(false, Seconds(1.5), true);
        Assert.That(playback.Gain, Is.EqualTo(0.6875f).Within(0.0001f));

        playback.Update(Seconds(2.5), true);
        Assert.That(playback.Gain, Is.EqualTo(0.34375f).Within(0.0001f));
        playback.Update(Seconds(3.5), true);
        Assert.That(playback.Gain, Is.Zero);
    }

    [Test]
    public void RepeatedInactiveUpdatesDoNotExtendRetention()
    {
        var playback = new ScpCombatMusicPlayback();
        playback.SetCombatMode(true, TimeSpan.Zero, false);
        playback.SetCombatMode(false, Seconds(10), true);
        playback.SetCombatMode(false, Seconds(15), true);

        Assert.That(playback.SetCombatMode(false, Seconds(20), true),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Stop));
    }

    [Test]
    public void ResetDiscardsRetentionAndAllowsANewTrack()
    {
        var playback = new ScpCombatMusicPlayback();
        using var assertions = Assert.EnterMultipleScope();
        playback.SetCombatMode(true, TimeSpan.Zero, false);
        playback.SetCombatMode(false, Seconds(10), true);

        playback.Reset();

        Assert.That(playback.Gain, Is.Zero);
        Assert.That(playback.Update(Seconds(20), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.None));
        Assert.That(playback.SetCombatMode(true, Seconds(21), false),
            Is.EqualTo(ScpCombatMusicPlaybackAction.Start));
    }

    private static TimeSpan Seconds(double seconds)
    {
        return TimeSpan.FromSeconds(seconds);
    }
}
