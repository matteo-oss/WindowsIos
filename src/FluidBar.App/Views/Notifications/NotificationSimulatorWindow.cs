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
/// Dedicated Simulator Window for Multi-Platform Social Notifications (WeChat, DingTalk, Feishu, WeCom),
/// supporting customized sources, live preview with diagonal streamer glow, and double-click window jumping.
/// </summary>
public sealed class NotificationSimulatorWindow : Window
{
    public static event EventHandler<SocialNotificationModel>? NotificationSpawned;
    private readonly ComboBox _platformCombo;
    private readonly TextBox _senderInput;
    private readonly TextBox _contentInput;
    private readonly TextBox _timeInput;
    private readonly TextBox _customSourceInput;
    private readonly TextBlock _statusBlock;

    // In-place live preview elements
    private readonly Border _previewCard;
    private readonly Border _previewIconContainer;
    private readonly ShapePath _previewIconPath;
    private readonly TextBlock _previewSender;
    private readonly TextBlock _previewChip;
    private readonly Border _previewChipBorder;
    private readonly TextBlock _previewContent;
    private readonly TextBlock _previewTime;
    private readonly StreamerEffectOverlay _previewStreamer;

    public NotificationSimulatorWindow()
    {
        Title = "多平台消息通知模拟与跳转中心";
        Width = 620;
        Height = 650;
        MinWidth = 540;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = false;

        var dark = true;
        Background = new SolidColorBrush(Color.Parse(dark ? "#16181F" : "#F6F7FA"));
        Foreground = new SolidColorBrush(Color.Parse(dark ? "#E2E2EC" : "#202028"));

        // Header section
        var headerTitle = new TextBlock
        {
            Text = "多平台消息通知模拟器",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var headerSubtitle = new TextBlock
        {
            Text = "支持 微信、钉钉、飞书、企业微信 通知卡片样式，具备斜向流光划过、无边框半透明及双击置顶跳转功能。",
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.Parse("#8E8E9E")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        };

        // Form controls
        _platformCombo = new ComboBox
        {
            ItemsSource = new[]
            {
                "微信 (WeChat)",
                "钉钉 (DingTalk)",
                "飞书 (Feishu)",
                "企业微信 (WeCom)"
            },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _senderInput = new TextBox
        {
            Text = "张经理",
            Watermark = "发送人名称",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _contentInput = new TextBox
        {
            Text = "项目下周一正式上线，请大家核对一下最新交付清单与测试报告。",
            Watermark = "消息内容预览",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _timeInput = new TextBox
        {
            Text = "刚刚",
            Watermark = "时间戳 (如: 刚刚, 10:24)",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _customSourceInput = new TextBox
        {
            Watermark = "自定义来源标签 (留空则使用默认平台名称)",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var presetBtn = new Button
        {
            Content = "✨ 载入当前平台推荐预设",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };
        presetBtn.Click += (_, _) => LoadPreset();

        // Layout the form in a 2-column grid
        var formGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 10,
            ColumnSpacing = 12
        };

        AddFormRow(formGrid, 0, "社交软件平台:", _platformCombo);
        AddFormRow(formGrid, 1, "发送人姓名:", _senderInput);
        AddFormRow(formGrid, 2, "消息内容预览:", _contentInput);
        AddFormRow(formGrid, 3, "显示时间戳:", _timeInput);
        AddFormRow(formGrid, 4, "自定义来源名:", _customSourceInput);

        Grid.SetRow(presetBtn, 5);
        Grid.SetColumn(presetBtn, 1);
        formGrid.Children.Add(presetBtn);

        // Live Preview Section
        var previewTitle = new TextBlock
        {
            Text = "卡片实时交互预览 (可悬浮看流光 / 双击卡片测试置顶跳转):",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#B4B4C8")),
            Margin = new Thickness(0, 16, 0, 8)
        };

        // Create live preview elements
        _previewIconPath = new ShapePath
        {
            Data = Geometry.Parse(SocialPlatformStyles.WeChatIconPath),
            Fill = Brushes.White,
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _previewIconContainer = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(SocialPlatformStyles.WeChatPrimary),
            Child = _previewIconPath,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 12, 0)
        };

        _previewSender = new TextBlock
        {
            Text = "张经理",
            FontWeight = FontWeight.Bold,
            FontSize = 13.5,
            Foreground = new SolidColorBrush(Color.Parse("#F4F4F8")),
            VerticalAlignment = VerticalAlignment.Center
        };

        _previewChip = new TextBlock
        {
            Text = "微信",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(SocialPlatformStyles.WeChatPrimary)
        };

        _previewChipBorder = new Border
        {
            CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(40, 7, 193, 96)),
            Padding = new Thickness(6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _previewChip
        };

        _previewContent = new TextBlock
        {
            Text = _contentInput.Text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#BEBED0")),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 16
        };

        _previewTime = new TextBlock
        {
            Text = "刚刚",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#767688")),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var previewContentStack = new StackPanel
        {
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { _previewSender, _previewChipBorder }, Margin = new Thickness(0, 0, 0, 4) },
                _previewContent
            }
        };

        var previewGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children = { _previewIconContainer, previewContentStack, _previewTime }
        };
        Grid.SetColumn(_previewIconContainer, 0);
        Grid.SetColumn(previewContentStack, 1);
        Grid.SetColumn(_previewTime, 2);

        _previewStreamer = new StreamerEffectOverlay
        {
            CornerRadius = new CornerRadius(14),
            SheenColor = Color.FromArgb(160, 255, 255, 255),
            IsContinuous = false
        };

        _previewCard = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 22, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 8 24 0 #60000000"),
            Padding = new Thickness(14, 12),
            ClipToBounds = true,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = previewGrid
        };

        var previewHost = new Panel
        {
            Children = { _previewCard, _previewStreamer },
            Margin = new Thickness(0, 0, 0, 14)
        };

        _previewCard.DoubleTapped += (_, _) => TestJump();
        _previewCard.PointerEntered += (_, _) => _previewStreamer.SetHovered(true);
        _previewCard.PointerExited += (_, _) => _previewStreamer.SetHovered(false);

        // Buttons
        var spawnToastBtn = new Button
        {
            Content = "🚀 弹出无边框流光通知卡片",
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(16, 8),
            Background = new SolidColorBrush(Color.Parse("#2563EB")),
            Foreground = Brushes.White
        };
        spawnToastBtn.Click += (_, _) => SpawnToast();

        var triggerStreamerBtn = new Button
        {
            Content = "✨ 触发流光高光特效",
            Padding = new Thickness(14, 8)
        };
        triggerStreamerBtn.Click += (_, _) => _previewStreamer.PlaySweep();

        var testJumpBtn = new Button
        {
            Content = "⚡ 测试 Windows API 置顶跳转",
            Padding = new Thickness(14, 8)
        };
        testJumpBtn.Click += (_, _) => TestJump();

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 14),
            Children = { spawnToastBtn, triggerStreamerBtn, testJumpBtn }
        };

        // Status block
        _statusBlock = new TextBlock
        {
            Text = "准备就绪。点击上方按钮可发送通知弹窗，或双击预览卡片直接测试窗口跳转。",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#8E8EA0")),
            TextWrapping = TextWrapping.Wrap
        };

        var statusBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10),
            Child = _statusBlock
        };

        var mainStack = new StackPanel
        {
            Children =
            {
                headerTitle,
                headerSubtitle,
                formGrid,
                previewTitle,
                previewHost,
                buttonBar,
                statusBorder
            }
        };

        var scroll = new ScrollViewer
        {
            Content = mainStack,
            Padding = new Thickness(24)
        };

        Content = scroll;

        // Wire change events
        _platformCombo.SelectionChanged += (_, _) => UpdatePreview();
        _senderInput.TextChanged += (_, _) => UpdatePreview();
        _contentInput.TextChanged += (_, _) => UpdatePreview();
        _timeInput.TextChanged += (_, _) => UpdatePreview();
        _customSourceInput.TextChanged += (_, _) => UpdatePreview();

        UpdatePreview();
    }

    private static void AddFormRow(Grid grid, int row, string label, Control control)
    {
        var textBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#C8C8D4"))
        };
        Grid.SetRow(textBlock, row);
        Grid.SetColumn(textBlock, 0);
        grid.Children.Add(textBlock);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
    }

    private SocialPlatform SelectedPlatform => (SocialPlatform)Math.Clamp(_platformCombo.SelectedIndex, 0, 3);

    private void UpdatePreview()
    {
        var platform = SelectedPlatform;
        var color = SocialPlatformStyles.GetPrimaryColor(platform);
        var sourceName = !string.IsNullOrWhiteSpace(_customSourceInput.Text)
            ? _customSourceInput.Text
            : ChatAppActivator.GetDisplayName(platform);

        _previewIconPath.Data = Geometry.Parse(SocialPlatformStyles.GetIconPath(platform));
        _previewIconContainer.Background = new SolidColorBrush(color);
        _previewSender.Text = string.IsNullOrWhiteSpace(_senderInput.Text) ? "发送人" : _senderInput.Text;
        _previewChip.Text = sourceName;
        _previewChip.Foreground = new SolidColorBrush(color);
        _previewChipBorder.Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
        _previewContent.Text = string.IsNullOrWhiteSpace(_contentInput.Text) ? "（消息内容预览）" : _contentInput.Text;
        _previewTime.Text = string.IsNullOrWhiteSpace(_timeInput.Text) ? "刚刚" : _timeInput.Text;
    }

    private void LoadPreset()
    {
        var platform = SelectedPlatform;
        switch (platform)
        {
            case SocialPlatform.WeChat:
                _senderInput.Text = "张经理";
                _contentInput.Text = "项目下周一正式上线，请大家核对一下最新交付清单与测试报告。";
                _timeInput.Text = "刚刚";
                break;
            case SocialPlatform.DingTalk:
                _senderInput.Text = "运维监控群";
                _contentInput.Text = "【紧急告警】集群节点 CPU 使用率超过 85%，请值班工程师立即介入排查。";
                _timeInput.Text = "1 分钟前";
                break;
            case SocialPlatform.Feishu:
                _senderInput.Text = "李思齐 (产品部)";
                _contentInput.Text = "在文档《2026 Q4 产品架构路线图》中提及了你，并留下了 3 条待办评论。";
                _timeInput.Text = "刚刚";
                break;
            case SocialPlatform.WeCom:
                _senderInput.Text = "人事行政部";
                _contentInput.Text = "您的年假审批申请已通过主管审核，可在企业微信移动端查看审批流程。";
                _timeInput.Text = "10:32";
                break;
        }
        UpdatePreview();
        _previewStreamer.PlaySweep();
    }

    private void SpawnToast()
    {
        var model = new SocialNotificationModel
        {
            Platform = SelectedPlatform,
            Sender = string.IsNullOrWhiteSpace(_senderInput.Text) ? "发送人" : _senderInput.Text,
            Content = string.IsNullOrWhiteSpace(_contentInput.Text) ? "消息内容预览" : _contentInput.Text,
            Timestamp = string.IsNullOrWhiteSpace(_timeInput.Text) ? "刚刚" : _timeInput.Text,
            CustomSourceName = string.IsNullOrWhiteSpace(_customSourceInput.Text) ? null : _customSourceInput.Text
        };

        var toast = new SocialNotificationToastWindow(model);
        toast.Show();
        NotificationSpawned?.Invoke(this, model);

        _statusBlock.Text = $"已弹出【{model.DisplaySourceName}】通知卡片。可直接双击弹窗卡片触发窗口跳转！";
        _statusBlock.Foreground = new SolidColorBrush(Color.Parse("#38E07A"));
    }

    private void TestJump()
    {
        var platform = SelectedPlatform;
        var result = ChatAppActivator.Activate(platform);

        if (result.Status == ChatActivationStatus.FoundAndFocused)
        {
            _statusBlock.Text = $"✅ {result.Message} (句柄: 0x{result.Hwnd.ToString("X")})";
            _statusBlock.Foreground = new SolidColorBrush(Color.Parse("#38E07A"));
        }
        else if (result.Status == ChatActivationStatus.LaunchedProcess)
        {
            _statusBlock.Text = $"🚀 {result.Message}";
            _statusBlock.Foreground = new SolidColorBrush(Color.Parse("#4EA5FF"));
        }
        else
        {
            _statusBlock.Text = $"💡 {result.Message}";
            _statusBlock.Foreground = new SolidColorBrush(Color.Parse("#FFB049"));
        }

        _previewStreamer.PlaySweep();
    }
}
