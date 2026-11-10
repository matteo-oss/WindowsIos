using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluidBar.Platform.Windows.Interop;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace FluidBar.App.Views.Notifications;

/// <summary>
/// Borderless, translucent, rounded toast window featuring multi-platform social styles (WeChat, DingTalk, Feishu, WeCom),
/// dynamic diagonal streamer light effect, smooth fade-in/fade-out, and double-click window jumping.
/// </summary>
public sealed class SocialNotificationToastWindow : Window
{
    private readonly SocialNotificationModel _model;
    private readonly Border _cardBorder;
    private readonly StreamerEffectOverlay _streamer;
    private readonly TextBlock _feedbackText;
    private readonly Border _feedbackBanner;
    private readonly DispatcherTimer _autoCloseTimer;
    private readonly DispatcherTimer _fadeTimer;

    private double _targetOpacity = 1.0;
    private double _currentOpacity = 0.0;
    private bool _isClosing = false;
    private bool _isPaused = false;
    private int _displayRemainingSeconds = 7;

    public SocialNotificationModel Model => _model;

    public SocialNotificationToastWindow(SocialNotificationModel? model = null)
    {
        _model = model ?? new SocialNotificationModel();

        // 1. Frameless, transparent, always-on-top window configuration
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        Width = 430;
        Height = 120;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        TransparencyBackgroundFallback = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0.0;

        var brandColor = SocialPlatformStyles.GetPrimaryColor(_model.Platform);
        var brandSecondary = SocialPlatformStyles.GetSecondaryColor(_model.Platform);
        var sourceName = _model.DisplaySourceName;

        // 2. Icon glyph with brand gradient background
        var iconPathGeometry = Geometry.Parse(SocialPlatformStyles.GetIconPath(_model.Platform));
        var iconPath = new ShapePath
        {
            Data = iconPathGeometry,
            Fill = Brushes.White,
            Width = 26,
            Height = 26,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconContainer = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(12),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(brandColor, 0.0),
                    new GradientStop(brandSecondary, 1.0)
                }
            },
            BoxShadow = BoxShadows.Parse($"0 4 12 0 {brandColor.ToString()}50"),
            Child = iconPath,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0)
        };

        // 3. Sender and App Chip
        var senderBlock = new TextBlock
        {
            Text = _model.Sender,
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.Parse("#F4F4F8")),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 180
        };

        var chipBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(40, brandColor.R, brandColor.G, brandColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, brandColor.R, brandColor.G, brandColor.B)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = sourceName,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(brandColor)
            }
        };

        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { senderBlock, chipBorder },
            Margin = new Thickness(0, 0, 0, 4)
        };

        // 4. Message content preview
        var previewBlock = new TextBlock
        {
            Text = _model.Content,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.Parse("#C0C0D0")),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 17
        };

        // 5. Interactive double-click hint / feedback banner
        _feedbackText = new TextBlock
        {
            Text = "💡 双击卡片快速跳转置顶聊天窗口",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#8E8EA8")),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _feedbackBanner = new Border
        {
            Margin = new Thickness(0, 6, 0, 0),
            Child = _feedbackText
        };

        var contentColumn = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { headerRow, previewBlock, _feedbackBanner }
        };

        // 6. Right side: Timestamp & Close button
        var timeBlock = new TextBlock
        {
            Text = _model.Timestamp,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#767688")),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var closeButton = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock
            {
                Text = "✕",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#A0A0B0")),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        closeButton.PointerEntered += (_, _) =>
        {
            closeButton.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
        };
        closeButton.PointerExited += (_, _) =>
        {
            closeButton.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        };
        closeButton.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Dismiss();
        };

        var rightColumn = new StackPanel
        {
            Width = 65,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { timeBlock, closeButton }
        };

        // 7. Grid Layout inside card
        var cardGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children = { iconContainer, contentColumn, rightColumn }
        };
        Grid.SetColumn(iconContainer, 0);
        Grid.SetColumn(contentColumn, 1);
        Grid.SetColumn(rightColumn, 2);

        // 8. Streamer effect overlay
        _streamer = new StreamerEffectOverlay
        {
            CornerRadius = new CornerRadius(16),
            SheenColor = Color.FromArgb(160, 255, 255, 255),
            IsContinuous = false
        };

        // 9. Root Card Border with semi-transparent frosted styling & shadow
        _cardBorder = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(228, 20, 22, 28)), // Translucent obsidian
            BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 16 42 0 #90000000, 0 2 10 0 #45000000"),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(10),
            ClipToBounds = true,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var layerHost = new Panel
        {
            Children = { _cardBorder, _streamer }
        };

        _cardBorder.Child = cardGrid;
        Content = layerHost;

        // 10. Double click interaction: Activate chat software
        _cardBorder.DoubleTapped += OnCardDoubleTapped;

        // 11. Hover effects: triggers streamer sweep and pauses auto-dismiss
        _cardBorder.PointerEntered += (_, _) =>
        {
            _isPaused = true;
            _streamer.SetHovered(true);
            _cardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, brandColor.R, brandColor.G, brandColor.B));
        };

        _cardBorder.PointerExited += (_, _) =>
        {
            _isPaused = false;
            _streamer.SetHovered(false);
            _cardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        };

        // 12. Fade and auto-close timers
        _fadeTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _fadeTimer.Tick += OnFadeTick;

        _autoCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _autoCloseTimer.Tick += OnAutoCloseTick;

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Position at top-right corner of screen
        PositionTopRight();

        // Start smooth fade-in
        _currentOpacity = 0.0;
        _targetOpacity = 1.0;
        Opacity = 0.0;
        _fadeTimer.Start();
        _autoCloseTimer.Start();

        // Trigger dynamic diagonal streamer light effect on appearance!
        _streamer.PlaySweep();
    }

    private void PositionTopRight()
    {
        try
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var workArea = screen.WorkingArea;
                var x = workArea.X + workArea.Width - (int)(Width * screen.Scaling) - 20;
                var y = workArea.Y + 36;
                Position = new PixelPoint(x, y);
            }
        }
        catch { }
    }

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;

        // Execute Windows API / command line chat app window activation
        var result = ChatAppActivator.Activate(_model.Platform);

        // Visual feedback inside card
        if (result.Status == ChatActivationStatus.FoundAndFocused)
        {
            _feedbackText.Text = $"✅ {result.Message}";
            _feedbackText.Foreground = new SolidColorBrush(Color.Parse("#38E07A"));
        }
        else if (result.Status == ChatActivationStatus.LaunchedProcess)
        {
            _feedbackText.Text = $"🚀 {result.Message}";
            _feedbackText.Foreground = new SolidColorBrush(Color.Parse("#4EA5FF"));
        }
        else
        {
            _feedbackText.Text = $"💡 {result.Message}";
            _feedbackText.Foreground = new SolidColorBrush(Color.Parse("#FFB049"));
        }

        // Play high-tech streamer sheen on trigger!
        _streamer.PlaySweep();

        // Extend display time so user can read message
        _displayRemainingSeconds = Math.Max(_displayRemainingSeconds, 4);
    }

    private void OnAutoCloseTick(object? sender, EventArgs e)
    {
        if (_isPaused || _isClosing) return;

        _displayRemainingSeconds--;
        if (_displayRemainingSeconds <= 0)
        {
            Dismiss();
        }
    }

    /// <summary>
    /// Smoothly fades out the toast and closes the window.
    /// </summary>
    public void Dismiss()
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer.Stop();
        _targetOpacity = 0.0;
        if (!_fadeTimer.IsEnabled)
        {
            _fadeTimer.Start();
        }
    }

    private void OnFadeTick(object? sender, EventArgs e)
    {
        var step = 0.08;
        if (_currentOpacity < _targetOpacity)
        {
            _currentOpacity = Math.Min(_targetOpacity, _currentOpacity + step);
            Opacity = _currentOpacity;
            if (_currentOpacity >= _targetOpacity)
            {
                _fadeTimer.Stop();
            }
        }
        else if (_currentOpacity > _targetOpacity)
        {
            _currentOpacity = Math.Max(_targetOpacity, _currentOpacity - step);
            Opacity = _currentOpacity;
            if (_currentOpacity <= _targetOpacity)
            {
                _fadeTimer.Stop();
                if (_isClosing)
                {
                    Close();
                }
            }
        }
        else
        {
            _fadeTimer.Stop();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Dismiss();
        }
    }
}
