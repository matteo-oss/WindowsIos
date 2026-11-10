using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace FluidBar.App.Views.Notifications;

/// <summary>
/// Dynamic diagonal streamer / shimmer light effect overlay that animates a smooth diagonal highlight sweep
/// across the card surface and border upon appearance or when hovered.
/// </summary>
public sealed class StreamerEffectOverlay : Control
{
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<StreamerEffectOverlay, CornerRadius>(nameof(CornerRadius), new CornerRadius(16));

    public static readonly StyledProperty<Color> SheenColorProperty =
        AvaloniaProperty.Register<StreamerEffectOverlay, Color>(nameof(SheenColor), Color.FromArgb(140, 255, 255, 255));

    public static readonly StyledProperty<bool> IsContinuousProperty =
        AvaloniaProperty.Register<StreamerEffectOverlay, bool>(nameof(IsContinuous), false);

    private readonly DispatcherTimer _timer;
    private double _phase = -0.4;
    private bool _isPlaying;
    private bool _isHovered;
    private DateTime _lastTick;

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color SheenColor
    {
        get => GetValue(SheenColorProperty);
        set => SetValue(SheenColorProperty, value);
    }

    public bool IsContinuous
    {
        get => GetValue(IsContinuousProperty);
        set => SetValue(IsContinuousProperty, value);
    }

    public StreamerEffectOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps smooth animation
        };
        _timer.Tick += OnTick;
    }

    /// <summary>
    /// Triggers one complete smooth diagonal sheen sweep.
    /// </summary>
    public void PlaySweep()
    {
        _phase = -0.4;
        _isPlaying = true;
        _lastTick = DateTime.UtcNow;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
        InvalidateVisual();
    }

    /// <summary>
    /// Notifies the overlay about pointer hover state (streamer activates on hover).
    /// </summary>
    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        if (hovered)
        {
            if (!_isPlaying)
            {
                PlaySweep();
            }
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var delta = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        if (delta > 0.1) delta = 0.016;

        // Speed: complete sweep in ~1.1s
        _phase += delta * 1.35;

        if (_phase > 1.4)
        {
            if (IsContinuous || _isHovered)
            {
                _phase = -0.4; // Loop while hovered or continuous
            }
            else
            {
                _isPlaying = false;
                _timer.Stop();
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 || !_isPlaying)
        {
            return;
        }

        var w = bounds.Width;
        var h = bounds.Height;

        // Calculate diagonal gradient coordinates: from top-left (offset by phase) to bottom-right
        var slant = h * 0.75;
        var centerX = _phase * (w + slant * 2) - slant;
        var startX = centerX - (w * 0.22);
        var endX = centerX + (w * 0.22) + slant;

        var sheen = SheenColor;
        var coreAlpha = sheen.A;
        var midAlpha = (byte)(coreAlpha * 0.45);
        var lowAlpha = (byte)(coreAlpha * 0.15);

        var surfaceBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(startX, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(endX, h, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, sheen.R, sheen.G, sheen.B), 0.0),
                new GradientStop(Color.FromArgb(lowAlpha, sheen.R, sheen.G, sheen.B), 0.35),
                new GradientStop(Color.FromArgb(midAlpha, sheen.R, sheen.G, sheen.B), 0.45),
                new GradientStop(Color.FromArgb(coreAlpha, 255, 255, 255), 0.50), // Ultra-bright specular highlight
                new GradientStop(Color.FromArgb(midAlpha, sheen.R, sheen.G, sheen.B), 0.55),
                new GradientStop(Color.FromArgb(lowAlpha, sheen.R, sheen.G, sheen.B), 0.65),
                new GradientStop(Color.FromArgb(0, sheen.R, sheen.G, sheen.B), 1.0)
            }
        };

        var borderBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(startX, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(endX, h, RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, sheen.R, sheen.G, sheen.B), 0.0),
                new GradientStop(Color.FromArgb((byte)Math.Min(255, coreAlpha * 1.5), 255, 255, 255), 0.50),
                new GradientStop(Color.FromArgb(0, sheen.R, sheen.G, sheen.B), 1.0)
            }
        };

        var rect = new Rect(0.75, 0.75, Math.Max(0, w - 1.5), Math.Max(0, h - 1.5));
        var radius = Math.Max(CornerRadius.TopLeft, 8);

        // 1. Surface diagonal glare
        context.DrawRectangle(surfaceBrush, null, rect, radius, radius);

        // 2. High-light border rim gleam
        var borderPen = new Pen(borderBrush, 1.5);
        context.DrawRectangle(null, borderPen, rect, radius, radius);
    }
}
