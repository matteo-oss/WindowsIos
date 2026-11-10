using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluidBar.App.Localization;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Tasks;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.App.Views;

/// <summary>Keyboard-capable task editor backed by the same task service as the island card.</summary>
internal sealed class TaskManagerWindow : Window
{
    private readonly TaskListService _service;
    private readonly ILocalizer _localizer;
    private readonly SettingsController? _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _title = new() { Name = "task-title", MaxLength = 500 };
    private readonly CalendarDatePicker _date = new() { Name = "task-date", Watermark = "选择日程日期", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _time = new() { Name = "task-time", Watermark = "开始时间（小时:分钟）", MinWidth = 150 };
    private readonly TextBox _endTime = new() { Name = "task-end-time", Watermark = "结束时间（小时:分钟）", MinWidth = 150 };
    private readonly ComboBox _reminderMode = new() { Name = "task-reminder-mode", MinWidth = 130,
        ItemsSource = new[] { "不提醒", "准时提醒", "提前 15 分钟（会议预备）", "提前 1 小时", "下班前 17:30 提醒", "自定义时间" }, SelectedIndex = 0 };
    private readonly TextBox _customReminder = new() { Name = "task-reminder-time", Watermark = "提醒时间（小时:分钟）", MinWidth = 170, IsVisible = false };
    private readonly ComboBox _priority = new()
    {
        Name = "task-priority",
        MinWidth = 100,
        ItemsSource = new[] { "🔴 紧急 P0", "🟡 重要 P1", "🔵 普通 P2" },
        SelectedIndex = 2,
    };
    private readonly TextBox _category = new() { Name = "task-category", Watermark = "分类", MinWidth = 120, MaxLength = 40 };
    private readonly Button _save = new() { Name = "task-add" };
    private readonly Button _cancel = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Opacity = .6, FontSize = 12 };
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly StackPanel _editor = new() { Spacing = 10 };
    private readonly Border _editorSurface = new() { Padding = new Thickness(14), CornerRadius = new CornerRadius(8) };
    private readonly Border _root = new() { Padding = new Thickness(18) };
    private TaskListSnapshot? _snapshot;
    private TaskListSnapshot? _editingSnapshot;
    private int _editingIndex = -1;
    private bool _busy;
    private bool _closed;
    private SettingsPalette? _palette;
    private readonly WrapPanel _datePresets = new();
    private readonly WrapPanel _timePresets = new();
    private readonly WrapPanel _categoryPresets = new();
    private string? _messageKey;

    public TaskManagerWindow(TaskListService service, ILocalizer localizer, SettingsController? settings = null, DateOnly? initialDate = null)
    {
        _service = service;
        _localizer = localizer;
        _settings = settings;
        var selectedDate = initialDate ?? DateOnly.FromDateTime(DateTime.Today);
        SetSelectedDate(selectedDate);
        Width = 640;
        Height = 800;
        MinWidth = 360;
        MinHeight = 340;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _save, _cancel } };
        _editor.Children.Add(new TextBlock { Text = "日程规划", FontSize = 22, FontWeight = FontWeight.SemiBold });
        _editor.Children.Add(new TextBlock { Text = "安排工作与生活，保存后即时同步胶囊与备忘录", FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        _editor.Children.Add(_title);
        _editor.Children.Add(_datePresets);
        _editor.Children.Add(_date);
        _editor.Children.Add(_timePresets);
        var timeGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        timeGrid.Children.Add(_time);
        Grid.SetColumn(_endTime, 1);
        timeGrid.Children.Add(_endTime);
        _editor.Children.Add(timeGrid);
        var detailGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        detailGrid.Children.Add(_priority);
        Grid.SetColumn(_category, 1);
        detailGrid.Children.Add(_category);
        _editor.Children.Add(detailGrid);
        _editor.Children.Add(_categoryPresets);
        var reminderGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        reminderGrid.Children.Add(_reminderMode);
        Grid.SetColumn(_customReminder, 1);
        reminderGrid.Children.Add(_customReminder);
        _editor.Children.Add(reminderGrid);
        _reminderMode.SelectionChanged += (_, _) => _customReminder.IsVisible = _reminderMode.SelectedIndex == 5;
        _editor.Children.Add(buttons);
        _editor.Children.Add(_hint);
        _editorSurface.Child = _editor;
        var layout = new StackPanel { Spacing = 12 };
        layout.Children.Add(_editorSurface);
        layout.Children.Add(_message);
        layout.Children.Add(_rows);
        // The whole form remains reachable at high display scaling and on short screens.
        _root.Child = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Content = _root;
        _save.Click += async (_, _) => await SaveAsync();
        _cancel.Click += (_, _) => CancelEdit();
        _title.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; await SaveAsync(); }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Opened += (_, _) =>
        {
            FitToWorkingArea();
            FocusInput();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _service.SnapshotChanged -= OnSnapshotChanged;
            _localizer.Changed -= OnLocaleChanged;
            if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeChanged;
            if (_settings is not null) _settings.Changed -= OnSettingsChanged;
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
        _service.SnapshotChanged += OnSnapshotChanged;
        _localizer.Changed += OnLocaleChanged;
        if (Application.Current is { } app) app.ActualThemeVariantChanged += OnThemeChanged;
        if (_settings is not null) _settings.Changed += OnSettingsChanged;
        ApplyTheme();
        _busy = true;
        _messageKey = "Tasks.Manage.Loading";
        Render();
        _ = ReloadAsync();
    }

    public void FocusInput() => _title.Focus();

    private void OnThemeChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed) { ApplyTheme(); Render(); } });

    private void OnSettingsChanged(AppSettings previous, AppSettings current)
        => Dispatcher.UIThread.Post(() => { if (!_closed) { ApplyTheme(); Render(); } });

    private void ApplyTheme()
    {
        if (_settings is not null)
        {
            SettingsPopupTheme.Apply(this, _root, _editorSurface, SettingsPopupTheme.Palette(_settings));
            ApplyControlTheme(SettingsPopupTheme.Palette(_settings));
            return;
        }
        var fallback = SettingsPalette.For(Application.Current?.ActualThemeVariant == ThemeVariant.Light ? AppTheme.Light : AppTheme.Dark);
        SettingsPopupTheme.Apply(this, _root, _editorSurface, fallback);
        ApplyControlTheme(fallback);
    }

    private void ApplyControlTheme(SettingsPalette palette)
    {
        _palette = palette;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur };
        foreach (var control in new Avalonia.Controls.Primitives.TemplatedControl[] { _title, _time, _endTime, _customReminder, _category, _priority, _reminderMode, _date, _save, _cancel })
        {
            control.Background = palette.FieldBrush;
            control.Foreground = palette.ForegroundBrush;
            control.BorderBrush = palette.LineBrush;
            control.BorderThickness = new Thickness(1);
            control.CornerRadius = new CornerRadius(10);
            control.FontSize = 13;
        }
        _save.Background = palette.ActiveBrush;
        _save.Foreground = palette.ActiveForegroundBrush;
        _datePresets.Children.Clear();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var friday = today.AddDays(((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7);
        var monday = today.AddDays(((int)DayOfWeek.Monday - (int)today.DayOfWeek + 6) % 7 + 1);
        foreach (var (label, date) in new[] { ("今天", today), ("明天", today.AddDays(1)), ("本周五", friday), ("下周一", monday) })
            _datePresets.Children.Add(Preset(label, () => SetSelectedDate(date)));
        _timePresets.Children.Clear();
        foreach (var (label, start, end) in new[] { ("全天 09:00–18:00", "09:00", "18:00"), ("上午场 09:30", "09:30", ""), ("下午场 14:00", "14:00", "") })
            _timePresets.Children.Add(Preset(label, () => { _time.Text = start; _endTime.Text = end; }));
        _categoryPresets.Children.Clear();
        foreach (var label in new[] { "工作", "个人", "紧急" })
            _categoryPresets.Children.Add(Preset(label, () => _category.Text = label));
    }

    private Button Preset(string label, Action action)
    {
        var button = SettingsControls.Button(label, _palette!, onClick: action);
        button.Margin = new Thickness(0, 0, 6, 6);
        return button;
    }

    private TaskPriority SelectedPriority => _priority.SelectedIndex switch { 0 => TaskPriority.High, 1 => TaskPriority.Normal, _ => TaskPriority.Low };

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

    private DateOnly? SelectedDate() => _date.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null;

    private void SetSelectedDate(DateOnly? date)
    {
        var value = date ?? DateOnly.FromDateTime(DateTime.Today);
        _date.SelectedDate = value.ToDateTime(TimeOnly.MinValue);
    }

    private async Task ReloadAsync()
    {
        var snapshot = await Task.Run(_service.Load);
        if (_closed) return;
        _snapshot = snapshot;
        _busy = false;
        _messageKey = null;
        Render();
    }

    private void OnSnapshotChanged(object? sender, SnapshotEventArgs<TaskListSnapshot> e)
        => Dispatcher.UIThread.Post(() => { if (!_closed) { _snapshot = e.Snapshot; Render(); } });

    private void OnLocaleChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => { if (!_closed) Render(); });

    private async Task SaveAsync()
    {
        if (_busy) return;
        var title = _title.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            _messageKey = "Tasks.Manage.Error";
            Render();
            return;
        }
        var index = _editingIndex;
        var expected = _editingSnapshot;
        var hasTime = TimeOnly.TryParse(_time.Text, out var scheduledTime);
        var hasEndTime = TimeOnly.TryParse(_endTime.Text, out var scheduledEndTime);
        var selectedDate = SelectedDate();
        DateTimeOffset? reminderAt = null;
        if (_reminderMode.SelectedIndex > 0 && (!hasTime || selectedDate is null))
        {
            _messageKey = "Tasks.Manage.Error";
            Render();
            return;
        }
        if (selectedDate is not null && hasTime && _reminderMode.SelectedIndex > 0)
        {
            var localDate = selectedDate.Value;
            var eventLocal = localDate.ToDateTime(scheduledTime);
            var eventAt = new DateTimeOffset(eventLocal, TimeZoneInfo.Local.GetUtcOffset(eventLocal));
            if (_reminderMode.SelectedIndex is 1 or 2 or 3)
                reminderAt = eventAt.AddMinutes(_reminderMode.SelectedIndex switch { 2 => -15, 3 => -60, _ => 0 });
            else if (_reminderMode.SelectedIndex == 4 || TimeOnly.TryParse(_customReminder.Text, out _))
            {
                var customTime = _reminderMode.SelectedIndex == 4 ? new TimeOnly(17, 30) : TimeOnly.Parse(_customReminder.Text!);
                var reminderLocal = localDate.ToDateTime(customTime);
                reminderAt = new DateTimeOffset(reminderLocal, TimeZoneInfo.Local.GetUtcOffset(reminderLocal));
            }
            else
            {
                _messageKey = "Tasks.Manage.Error";
                Render();
                return;
            }
            if (reminderAt > eventAt && _reminderMode.SelectedIndex != 4)
            {
                _messageKey = "Tasks.Manage.Error";
                Render();
                return;
            }
        }
        if ((!string.IsNullOrWhiteSpace(_time.Text) && (!hasTime || selectedDate is null))
            || (!string.IsNullOrWhiteSpace(_endTime.Text) && (!hasEndTime || !hasTime || scheduledEndTime <= scheduledTime)))
        {
            _messageKey = "Tasks.Manage.Error";
            Render();
            return;
        }
        await ExecuteAsync(() => index < 0
            ? selectedDate is not null && hasTime
                ? _service.AddScheduledAsync(title, selectedDate.Value, scheduledTime,
                    hasEndTime ? scheduledEndTime : null, SelectedPriority,
                    _category.Text, reminderAt, _lifetime.Token)
                : _service.AddAsync(title, _lifetime.Token)
            : _service.UpdateAsync(index, title, selectedDate,
                hasTime ? scheduledTime : null, hasEndTime ? scheduledEndTime : null,
                SelectedPriority, _category.Text,
                expected, reminderAt, _lifetime.Token), clearEditor: true);
    }

    private async Task ExecuteAsync(Func<Task<ToggleTaskResult>> operation, bool clearEditor = false)
    {
        if (_busy || _closed) return;
        _busy = true;
        _messageKey = null;
        Render();
        try
        {
            var result = await operation();
            if (_closed) return;
            _snapshot = result.Snapshot;
            if (result.Succeeded)
            {
                if (clearEditor) CancelEdit();
            }
            else
            {
                _messageKey = result.FailureReason?.StartsWith("tasks changed", StringComparison.Ordinal) == true
                    ? "Tasks.Manage.Changed" : "Tasks.Manage.Error";
                // A refreshed index may refer to another task. Keep the typed text, return to add mode.
                if (_messageKey == "Tasks.Manage.Changed") { _editingIndex = -1; _editingSnapshot = null; }
            }
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception)
        {
            if (!_closed) _messageKey = "Tasks.Manage.Error";
        }
        finally
        {
            _busy = false;
            if (!_closed) Render();
        }
    }

    private void CancelEdit()
    {
        _editingIndex = -1;
        _editingSnapshot = null;
        _title.Text = string.Empty;
        _time.Text = string.Empty;
        _endTime.Text = string.Empty;
        _reminderMode.SelectedIndex = 0;
        _customReminder.Text = string.Empty;
        _priority.SelectedIndex = 2;
        _category.Text = string.Empty;
        Render();
        FocusInput();
    }

    private void Render()
    {
        Title = _localizer.Get("Tasks.Manage.Title");
        AutomationProperties.SetName(this, Title);
        _title.Watermark = _localizer.Get("Tasks.Manage.Input");
        AutomationProperties.SetName(_title, _title.Watermark.ToString());
        AutomationProperties.SetName(_date, "日程日期");
        AutomationProperties.SetName(_time, "开始时间（小时:分钟）");
        AutomationProperties.SetName(_endTime, "结束时间（小时:分钟）");
        _save.Content = _localizer.Get(_editingIndex < 0 ? "Tasks.Manage.Add" : "Tasks.Manage.Save");
        _cancel.Content = _localizer.Get("Tasks.Manage.Cancel");
        _cancel.IsVisible = _editingIndex >= 0;
        _hint.Text = _localizer.Get("Tasks.Manage.Hint");
        var invalid = _snapshot?.Status == TaskListStatus.Invalid;
        _editor.IsEnabled = !_busy && !invalid;
        _message.Text = _messageKey is not null ? _localizer.Get(_messageKey)
            : invalid ? _localizer.Get("Tasks.Manage.Invalid")
            : _snapshot?.Count == 0 ? _localizer.Get("Tasks.Manage.Empty") : string.Empty;
        _rows.Children.Clear();
        if (_snapshot is not { } snapshot) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var groups = new[] { "今天", "近期 / 未安排", "已完成" };
        var panels = groups.Select(_ => new StackPanel { Spacing = 8 }).ToArray();
        for (var g = 0; g < groups.Length; g++)
            _rows.Children.Add(new Expander { Header = groups[g], IsExpanded = g != 2, HorizontalAlignment = HorizontalAlignment.Stretch, Content = panels[g] });
        for (var i = 0; i < snapshot.Count; i++)
        {
            var index = i;
            var item = snapshot.Items[i];
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6, IsEnabled = !_busy };
            var title = new TextBlock { Text = item.Title, TextWrapping = TextWrapping.Wrap,
                TextDecorations = item.Done ? TextDecorations.Strikethrough : null, Opacity = item.Done ? .55 : 1 };
            var details = new WrapPanel();
            var timeLabel = item.Date == today ? "今天" : item.Date?.ToString("MM月dd日") ?? "未安排";
            if (item.StartTime is { } start) timeLabel += $" {start:HH:mm}";
            if (item.EndTime is { } end) timeLabel += $"–{end:HH:mm}";
            details.Children.Add(SettingsControls.Chip(timeLabel, _palette!, SettingsChipKind.Tag));
            var priorityLabel = item.Priority switch { TaskPriority.High => "紧急 P0", TaskPriority.Normal => "重要 P1", _ => "普通 P2" };
            var color = item.Priority switch { TaskPriority.High => Colors.OrangeRed, TaskPriority.Normal => Colors.Gold, _ => Colors.DodgerBlue };
            details.Children.Add(SettingsControls.Chip(priorityLabel, _palette!, SettingsChipKind.Tag, dot: color));
            if (!string.IsNullOrEmpty(item.Category))
                details.Children.Add(SettingsControls.Chip(item.Category, _palette!, SettingsChipKind.Tag, dot: item.Category == "紧急" ? Colors.OrangeRed : Colors.MediumSeaGreen));
            var text = new StackPanel { Spacing = 6, Children = { title, details } };
            if (item.ReminderAt is { } reminderAt) text.Children.Add(new TextBlock { Text = $"提醒 {reminderAt.ToLocalTime():MM月dd日 HH:mm}", FontSize = 11, Foreground = _palette!.SecondaryBrush });
            var check = new CheckBox
            {
                IsChecked = item.Done,
                Content = text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(check, item.Title);
            check.IsCheckedChanged += async (_, _) => await ExecuteAsync(() => _service.ToggleAsync(index, snapshot, _lifetime.Token));
            row.Children.Add(check);
            var edit = SettingsControls.Button("✎", _palette!, SettingsButtonVariant.Ghost, SettingsButtonSize.Icon);
            edit.Name = "task-edit-" + index;
            ToolTip.SetTip(edit, "编辑");
            AutomationProperties.SetName(edit, "编辑 " + item.Title);
            Grid.SetColumn(edit, 1);
            edit.Click += (_, _) =>
            {
                _editingIndex = index;
                _editingSnapshot = snapshot;
                _title.Text = item.Title;
                SetSelectedDate(item.Date);
                _time.Text = item.StartTime?.ToString("HH:mm");
                _endTime.Text = item.EndTime?.ToString("HH:mm");
                _priority.SelectedIndex = item.Priority switch { TaskPriority.High => 0, TaskPriority.Normal => 1, _ => 2 };
                _category.Text = item.Category ?? string.Empty;
                _reminderMode.SelectedIndex = item.ReminderAt is null ? 0 : 5;
                _customReminder.Text = item.ReminderAt?.ToLocalTime().ToString("HH:mm");
                _messageKey = null;
                Render();
                FocusInput();
                _title.SelectAll();
            };
            row.Children.Add(edit);
            var delete = SettingsControls.IconButton(SettingsIconKind.Trash, _palette!, () => { });
            delete.Name = "task-delete-" + index;
            ToolTip.SetTip(delete, "删除");
            AutomationProperties.SetName(delete, "删除 " + item.Title);
            Grid.SetColumn(delete, 2);
            delete.Click += async (_, _) => await ExecuteAsync(() =>
                _service.RemoveAsync(index, _lifetime.Token, snapshot));
            row.Children.Add(delete);
            panels[item.Done ? 2 : item.Date == today ? 0 : 1].Children.Add(SettingsControls.Card(row, _palette!, SettingsCardKind.Row));
        }
    }
}
