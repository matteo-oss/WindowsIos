using FluidBar.App.Hosting.Skia;
using FluidBar.Core.Activities;
using FluidBar.Core.Layout;
using FluidBar.Core.Motion;
using FluidBar.Island;

namespace FluidBar.Ui.Tests;

/// <summary>
/// Compact media has exactly two physical sizes. Playback, lyrics and hover remain paint-only while
/// compact; clicking switches directly to the expanded endpoint.
/// </summary>
public sealed class CompactMediaWidthTests
{
    private const float TokenWidth = 132f;
    private const float TokenHeight = 36f;

    private static IslandRenderState MediaPill(double width) => new()
    {
        Mode = IslandMode.Float,
        Form = ShellForm.Compact,
        ShellWidth = width,
        ShellHeight = TokenHeight,
        Compact = new CompactActivity(ActivityKind.Media, new CompactModel
        {
            Title = "Miss You a Little",
            Subtitle = "Bryce Vine",
            IsActive = true,
            ShowLyrics = true,
            LyricLine = "I miss you a little all the time",
        }),
    };

    [Fact]
    public void Compact_media_pill_has_no_play_control_at_the_old_button_position()
    {
        var state = MediaPill(TokenWidth);
        var shell = IslandMetrics.ShellRect(state);

        // The right-hand 26 DIP circle used to be the play/pause control; it must be plain pill now.
        var oldButton = SkiaIslandHitTest.Hit(state, shell.Right - 20, shell.MidY);
        Assert.Equal(IslandHitKind.Compact, oldButton.Kind);
        Assert.Equal(IslandPageControl.None, oldButton.Control);

        // Nothing else in the pill is a control either.
        for (var x = shell.Left + 4; x < shell.Right - 2; x += 10)
        {
            Assert.Equal(IslandPageControl.None, SkiaIslandHitTest.Hit(state, x, shell.MidY).Control);
        }
    }

    [Fact]
    public void Compact_media_token_matches_idle_capsule()
    {
        Assert.Equal(new VisualSize(TokenWidth, TokenHeight), LayoutTokens.CompactSize(ActivityKind.Media));
        Assert.Equal(LayoutTokens.CompactSize(ActivityKind.Idle), LayoutTokens.CompactSize(ActivityKind.Media));
    }

    [Theory]
    [InlineData(ActivityKind.Idle)]
    [InlineData(ActivityKind.Media)]
    [InlineData(ActivityKind.Notification)]
    [InlineData(ActivityKind.Clipboard)]
    [InlineData(ActivityKind.FileShelf)]
    [InlineData(ActivityKind.Agent)]
    [InlineData(ActivityKind.Timer)]
    public void All_activity_kinds_share_the_same_compact_geometry(ActivityKind kind)
        => Assert.Equal(LayoutTokens.CompactSize(ActivityKind.Idle), LayoutTokens.CompactSize(kind));

    [Fact]
    public void Playback_lyrics_and_hover_cannot_resize_compact_media()
    {
        using var motion = new SkiaIslandMotion();

        motion.SetTarget(Target(ShellForm.Compact, TokenWidth));
        Assert.Equal(TokenWidth, motion.Width, 0.5);
        Assert.Equal(TokenHeight, motion.Height, 0.5);

        motion.SetTarget(Target(ShellForm.Peek, TokenWidth));
        for (var frame = 0; frame < 60; frame++)
        {
            motion.Step(1.0 / 60.0);
            Assert.Equal(TokenWidth, motion.Width, 0.01);
            Assert.Equal(TokenHeight, motion.Height, 0.01);
        }
    }

    [Fact]
    public void Expanded_media_switches_only_between_endpoint_sizes()
    {
        using var motion = new SkiaIslandMotion();
        motion.SetTarget(Target(ShellForm.Compact, TokenWidth));

        motion.SetTarget(Target(ShellForm.Expanded, TokenWidth));
        Assert.Equal(LayoutTokens.ExpandedWidth, motion.Width, 0.01);
        Assert.Equal(LayoutTokens.ExpandedHeight, motion.Height, 0.01);

        motion.SetTarget(Target(ShellForm.Compact, TokenWidth));
        Assert.Equal(TokenWidth, motion.Width, 0.01);
        Assert.Equal(TokenHeight, motion.Height, 0.01);
    }

    private static SkiaMotionTarget Target(ShellForm form, double compactWidth) => new(
        form,
        IslandMode.Float,
        IslandAnchor.TopCenter,
        new VisualSize(compactWidth, TokenHeight),
        0,
        0,
        false,
        LockGeometry: true);
}
