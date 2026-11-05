using FluidBar.Core.Layout;
using SkiaSharp;

namespace FluidBar.Island.Tests;

public sealed class ExpandedVisualScaleTests
{
    [Fact]
    public void Expanded_controls_paint_and_hit_in_the_same_scaled_coordinate_space()
    {
        var state = IslandTestFixtures.Expanded(0);
        var content = IslandMetrics.ContentRect(
            IslandMetrics.ShellRect(state), state.RailSide, state.TabBarStyle);
        var logicalPage = IslandMetrics.ExpandedLogicalPageRect(state, 0);
        var toggle = PageLayout.HomeToggle(logicalPage, 0);
        var togglePoint = ToPhysical(content, toggle.MidX, toggle.MidY);

        using var renderer = new IslandRenderer(IslandTestFixtures.Theme);
        var toggleHit = renderer.HitTest(state, togglePoint.X, togglePoint.Y);

        Assert.Equal(LayoutTokens.ExpandedVisualScale,
            content.Width / IslandMetrics.ExpandedLogicalContentRect().Width, 3);
        Assert.Equal(IslandHitKind.PageControl, toggleHit.Kind);
        Assert.Equal(IslandPageControl.QuickToggle, toggleHit.Control);
        Assert.Equal(0, toggleHit.Index);

        var collapse = PageLayout.CollapseRect(IslandMetrics.ExpandedLogicalContentRect());
        var collapsePoint = ToPhysical(content, collapse.MidX, collapse.MidY);
        Assert.Equal(IslandHitKind.Collapse,
            renderer.HitTest(state, collapsePoint.X, collapsePoint.Y).Kind);
    }

    private static SKPoint ToPhysical(SKRect content, float logicalX, float logicalY)
    {
        var scale = (float)LayoutTokens.ExpandedVisualScale;
        return new SKPoint(content.Left + (logicalX * scale), content.Top + (logicalY * scale));
    }
}
