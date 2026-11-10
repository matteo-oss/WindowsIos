using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.App.Views;

internal sealed record MemoEntry(Guid Id, string Title, string Content, DateTimeOffset CreatedAt);

/// <summary>Small local memo editor backed by %APPDATA%\FluidBar\memos.json.</summary>
internal sealed class MemoManagerWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "memos.json");
    private readonly TextBox _title = new() { Watermark = "备忘录标题", MaxLength = 80 };
    private readonly TextBox _content = new()
    {
        Watermark = "输入备忘内容",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 78,
        MaxLength = 2000,
    };
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly Border _editorSurface = new() { Padding = new Thickness(14), CornerRadius = new CornerRadius(8) };
    private readonly Func<MemoEntry, Task> _memoAdded;
    private readonly SettingsController _settings;
    private readonly Border _root = new() { Padding = new Thickness(18) };
    private List<MemoEntry> _items = [];
    private bool _closed;

    public MemoManagerWindow(SettingsController settings, Func<MemoEntry, Task> memoAdded)
    {
        _settings = settings;
        _memoAdded = memoAdded;
        Title = "备忘录管理";
        Width = 560;
        Height = 560;
        MinWidth = 360;
        MinHeight = 360;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var add = SettingsControls.Button("添加备忘录", SettingsPopupTheme.Palette(settings), SettingsButtonVariant.Solid);
        add.Click += async (_, _) => await AddAsync();

        var editor = new StackPanel { Spacing = 10, Children = { _title, _content, add } };
        _editorSurface.Child = editor;
        var scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        layout.Children.Add(_editorSurface);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        _root.Child = layout;
        Content = _root;

        Opened += async (_, _) =>
        {
            FitToWorkingArea();
            await LoadAsync();
            _title.Focus();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeChanged;
            _settings.Changed -= OnSettingsChanged;
        };
        if (Application.Current is { } application) application.ActualThemeVariantChanged += OnThemeChanged;
        _settings.Changed += OnSettingsChanged;
        ApplyTheme();
    }

    public void FocusInput() => _title.Focus();

    private async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_filePath))
                _items = JsonSerializer.Deserialize<List<MemoEntry>>(await File.ReadAllTextAsync(_filePath), JsonOptions) ?? [];
        }
        catch
        {
            _items = [];
        }
        if (!_closed) Render();
    }

    private async Task AddAsync()
    {
        var title = _title.Text?.Trim();
        var content = _content.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content)) return;
        var entry = new MemoEntry(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? "备忘录" : title,
            content ?? string.Empty, DateTimeOffset.Now);
        _items.Insert(0, entry);
        await SaveAsync();
        _title.Text = string.Empty;
        _content.Text = string.Empty;
        Render(entry.Id);
        await _memoAdded(entry);
        _title.Focus();
    }

    private async Task DeleteAsync(MemoEntry entry, Border row)
    {
        await FadeAsync(row, 1, 0, TimeSpan.FromMilliseconds(150));
        _items.RemoveAll(item => item.Id == entry.Id);
        await SaveAsync();
        if (!_closed) Render();
    }

    private async Task SaveAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var temporary = _filePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_items, JsonOptions));
        File.Move(temporary, _filePath, true);
    }

    private void Render(Guid? animateId = null)
    {
        _rows.Children.Clear();
        foreach (var entry in _items)
        {
            var text = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = entry.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = entry.Content, Opacity = .72, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = entry.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"), FontSize = 11, Opacity = .5 },
                },
            };
            var palette = SettingsPopupTheme.Palette(_settings);
            var delete = SettingsControls.IconButton(SettingsIconKind.Trash, palette, () => { });
            ToolTip.SetTip(delete, "删除");
            AutomationProperties.SetName(delete, "删除备忘录 " + entry.Title);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            grid.Children.Add(text);
            Grid.SetColumn(delete, 1);
            grid.Children.Add(delete);
            var row = SettingsControls.Card(grid, palette, SettingsCardKind.Row);
            row.Opacity = animateId == entry.Id ? 0 : 1;
            delete.Click += async (_, _) => await DeleteAsync(entry, row);
            _rows.Children.Add(row);
            if (animateId == entry.Id) _ = FadeAsync(row, 0, 1, TimeSpan.FromMilliseconds(180));
        }
    }

    private static async Task FadeAsync(Control control, double from, double to, TimeSpan duration)
    {
        const int frames = 10;
        control.Opacity = from;
        for (var i = 1; i <= frames; i++)
        {
            await Task.Delay(duration / frames);
            control.Opacity = from + ((to - from) * i / frames);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed) { ApplyTheme(); Render(); } });

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
        => Dispatcher.UIThread.Post(() => { if (!_closed) { ApplyTheme(); Render(); } });

    private void ApplyTheme()
    {
        var palette = SettingsPopupTheme.Palette(_settings);
        SettingsPopupTheme.Apply(this, _root, _editorSurface, palette);
        TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur };
        foreach (var box in new[] { _title, _content })
        {
            box.Background = palette.FieldBrush;
            box.Foreground = palette.ForegroundBrush;
            box.BorderBrush = palette.LineBrush;
            box.BorderThickness = new Thickness(1);
            box.CornerRadius = new CornerRadius(10);
        }
        if (_editorSurface.Child is StackPanel { Children: { Count: > 2 } children } && children[2] is Button add)
        {
            add.Background = palette.ActiveBrush;
            add.Foreground = palette.ActiveForegroundBrush;
        }
    }

    private void FitToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null) return;
        var scale = screen.Scaling;
        MaxWidth = Math.Max(MinWidth, screen.WorkingArea.Width / scale - 32);
        MaxHeight = Math.Max(MinHeight, screen.WorkingArea.Height / scale - 32);
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }
}
