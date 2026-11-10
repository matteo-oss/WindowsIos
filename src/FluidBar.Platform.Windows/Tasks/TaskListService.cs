using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Immutable;
using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Threading;

namespace FluidBar.Platform.Windows.Tasks;

/// <summary>State of the local task file.</summary>
public enum TaskListStatus
{
    /// <summary>The file parsed (it may contain zero tasks).</summary>
    Ok = 0,

    /// <summary>The file does not exist yet.</summary>
    Missing = 1,

    /// <summary>The file exists but could not be parsed as a task array.</summary>
    Invalid = 2,
}

/// <summary>One task row from <c>%APPDATA%\\FluidBar\\tasks.json</c>.</summary>
/// <param name="Title">Task title (never blank).</param>
/// <param name="Done">Checkbox state.</param>
public enum TaskPriority
{
    Low,
    Normal,
    High,
}

public readonly record struct TaskItemData(
    string Title,
    bool Done,
    DateOnly? Date = null,
    TimeOnly? StartTime = null,
    TimeOnly? EndTime = null,
    TaskPriority Priority = TaskPriority.Normal,
    string? Category = null,
    DateTimeOffset? ReminderAt = null,
    DateTimeOffset? ReminderDismissedAt = null);

/// <summary>Immutable snapshot of the task file.</summary>
/// <param name="Items">Tasks in file order (the card paints the first three).</param>
/// <param name="Status">Ok / Missing / Invalid.</param>
/// <param name="FilePath">Path that was read (for the empty-state hint and diagnostics).</param>
/// <param name="FailureReason">Why the file could not be used (diagnostics; never rendered as data).</param>
public sealed record TaskListSnapshot(
    ImmutableArray<TaskItemData> Items,
    TaskListStatus Status,
    string? FilePath,
    string? FailureReason)
{
    /// <summary>Whether the file was read successfully (possibly with zero tasks).</summary>
    public bool IsAvailable => Status == TaskListStatus.Ok;

    /// <summary>Number of tasks in the file.</summary>
    public int Count => Items.IsDefault ? 0 : Items.Length;

    /// <summary>Number of completed tasks.</summary>
    public int DoneCount
    {
        get
        {
            if (Items.IsDefault)
            {
                return 0;
            }

            var done = 0;
            foreach (var item in Items)
            {
                if (item.Done)
                {
                    done++;
                }
            }

            return done;
        }
    }
}

/// <summary>Result of a checkbox toggle.</summary>
/// <param name="Succeeded">Whether the file was rewritten.</param>
/// <param name="FailureReason">Why the toggle failed (diagnostics).</param>
/// <param name="Snapshot">Snapshot after the write attempt.</param>
public sealed record ToggleTaskResult(bool Succeeded, string? FailureReason, TaskListSnapshot Snapshot);

/// <summary>Configuration for <see cref="TaskListService"/>.</summary>
public sealed class TaskListOptions
{
    /// <summary>Default file (<c>%APPDATA%\\FluidBar\\tasks.json</c>).</summary>
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluidBar", "tasks.json");

    /// <summary>Task file path.</summary>
    public string FilePath { get; init; } = DefaultFilePath;

    /// <summary>Reload cadence (external edits appear within this window).</summary>
    public TimeSpan ReloadInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Safety cap on parsed tasks (the card paints three).</summary>
    public int MaxTasks { get; init; } = 100;

    /// <summary>Maximum title length accepted for a new title; existing titles remain untouched.</summary>
    public int MaxTitleLength { get; init; } = 500;

    /// <summary>Reads the <c>FLUIDBAR_TASKS_PATH</c> diagnostics override (used by the evidence run).</summary>
    public static TaskListOptions FromEnvironment() => new()
    {
        FilePath = Environment.GetEnvironmentVariable("FLUIDBAR_TASKS_PATH") is { Length: > 0 } path
            ? path
            : DefaultFilePath,
    };
}

/// <summary>
/// Local task list: reads <c>%APPDATA%\\FluidBar\\tasks.json</c> (<c>[{"title":"...","done":false}]</c>)
/// and writes toggles back atomically (temp file + replace). Missing/invalid files stay explicit empty
/// states; nothing is ever fabricated in memory.
/// </summary>
public sealed class TaskListService : PlatformMonitor<TaskListSnapshot>
{
    private readonly TaskListOptions _options;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private BackgroundPoller? _poller;
    private TaskListSnapshot? _lastPublished;

    public TaskListService(IPlatformLogger logger, PlatformCounters counters,
        TaskListOptions? options = null, TimeProvider? timeProvider = null)
        : base("tasks", logger, counters, timeProvider ?? TimeProvider.System)
    {
        _options = options ?? new TaskListOptions();
    }

    /// <summary>Configured task file path.</summary>
    public string FilePath => _options.FilePath;

    protected override ValueTask OnStartAsync(CancellationToken cancellationToken)
    {
        PublishIfChanged(Load());
        _poller ??= new BackgroundPoller("tasks", _options.ReloadInterval, PollTick, Logger);
        _poller.Start();
        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnStopAsync(CancellationToken cancellationToken)
    {
        _poller?.Dispose();
        _poller = null;
        return ValueTask.CompletedTask;
    }

    private void PollTick()
    {
        if (!_writeGate.Wait(0)) return;
        try { PublishIfChanged(Load()); }
        finally { _writeGate.Release(); }
    }

    /// <summary>Reads the file right now (no publishing).</summary>
    public TaskListSnapshot Load()
    {
        try
        {
            if (!File.Exists(_options.FilePath))
            {
                return new TaskListSnapshot([], TaskListStatus.Missing, _options.FilePath, "task file not found");
            }

            var json = File.ReadAllText(_options.FilePath, Encoding.UTF8);
            var snapshot = Parse(json, fileExists: true, _options.FilePath, _options.MaxTasks);
            if (!snapshot.IsAvailable)
            {
                Logger.LogWarning(PlatformLogCategory.Platform, "tasks.invalid",
                    "tasks.json is not a valid task array; the card shows the explicit empty state.",
                    data: new Dictionary<string, object?>
                    {
                        ["path"] = _options.FilePath,
                        ["reason"] = snapshot.FailureReason,
                    });
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(PlatformLogCategory.Platform, "tasks.read-failed",
                "tasks.json could not be read.", ex);
            return new TaskListSnapshot([], TaskListStatus.Invalid, _options.FilePath,
                $"read-failed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Flips one checkbox and rewrites the file atomically (temp file + replace). Returns an explicit
    /// failure when the file is missing/invalid or the index is out of range; the original file is left
    /// untouched in every failure path.
    /// </summary>
    /// <param name="index">Zero-based task index (file order).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public Task<ToggleTaskResult> ToggleAsync(int index, CancellationToken cancellationToken = default)
        => MutateAsync("toggle", index, null, null, cancellationToken);

    /// <summary>Toggles a row only while its displayed snapshot is current.</summary>
    public Task<ToggleTaskResult> ToggleAsync(int index, TaskListSnapshot expectedSnapshot, CancellationToken cancellationToken = default)
        => MutateAsync("toggle", index, null, expectedSnapshot, cancellationToken);

    /// <summary>Adds a task, creating a missing file. Existing invalid files are never overwritten.</summary>
    public Task<ToggleTaskResult> AddAsync(string title, CancellationToken cancellationToken = default)
        => MutateAsync("add", -1, title, null, cancellationToken);

    public Task<ToggleTaskResult> AddScheduledAsync(string title, DateOnly date, TimeOnly time,
        TimeOnly? endTime = null, TaskPriority priority = TaskPriority.Normal, string? category = null,
        DateTimeOffset? reminderAt = null,
        CancellationToken cancellationToken = default)
        => MutateAsync("add", -1, title, null, cancellationToken, date, time, endTime, priority, category, reminderAt);

    public Task<ToggleTaskResult> UpdateAsync(int index, string title, DateOnly? date, TimeOnly? time,
        TimeOnly? endTime, TaskPriority priority, string? category,
        TaskListSnapshot? expectedSnapshot, DateTimeOffset? reminderAt = null, CancellationToken cancellationToken = default)
        => MutateAsync("update", index, title, expectedSnapshot, cancellationToken, date, time, endTime, priority, category, reminderAt);

    /// <summary>Changes one title without modifying the remaining task data.</summary>
    public Task<ToggleTaskResult> RenameAsync(int index, string title, CancellationToken cancellationToken = default,
        TaskListSnapshot? expectedSnapshot = null)
        => MutateAsync("rename", index, title, expectedSnapshot, cancellationToken);

    /// <summary>Removes one task, rejecting stale displayed indices when a snapshot is supplied.</summary>
    public Task<ToggleTaskResult> RemoveAsync(int index, CancellationToken cancellationToken = default,
        TaskListSnapshot? expectedSnapshot = null)
        => MutateAsync("remove", index, null, expectedSnapshot, cancellationToken);

    private Task<ToggleTaskResult> MutateAsync(string operation, int index, string? title,
        TaskListSnapshot? expectedSnapshot, CancellationToken cancellationToken, DateOnly? date = null,
        TimeOnly? startTime = null, TimeOnly? endTime = null, TaskPriority priority = TaskPriority.Normal,
        string? category = null, DateTimeOffset? reminderAt = null)
        => Task.Run(async () =>
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = Load();
            if (!snapshot.IsAvailable && !(operation == "add" && snapshot.Status == TaskListStatus.Missing))
            {
                return new ToggleTaskResult(false,
                    snapshot.Status == TaskListStatus.Missing ? "tasks.json does not exist" : "tasks.json is not valid",
                    snapshot);
            }

            if (expectedSnapshot is not null && (expectedSnapshot.Status != snapshot.Status
                || !SequenceEqual(expectedSnapshot.Items, snapshot.Items)))
                return new ToggleTaskResult(false, "tasks changed; refresh and retry", snapshot);

            if (operation != "add" && (index < 0 || index >= snapshot.Items.Length))
            {
                return new ToggleTaskResult(false, "task index out of range", snapshot);
            }

            if (operation is "add" or "rename" or "update"
                && (string.IsNullOrWhiteSpace(title) || title.Length > Math.Max(1, _options.MaxTitleLength)))
                return new ToggleTaskResult(false, "invalid task title", snapshot);

            string json;
            try
            {
                // Preserve unknown fields and skipped legacy rows. Re-serializing the rendered snapshot
                // would silently drop content past the display cap and trim unedited titles.
                var original = snapshot.Status == TaskListStatus.Missing ? null : File.ReadAllText(_options.FilePath, Encoding.UTF8);
                var document = string.IsNullOrWhiteSpace(original) ? new JsonArray() : JsonNode.Parse(original) as JsonArray;
                if (document is null) return new ToggleTaskResult(false, "tasks.json is not valid", snapshot);
                var rows = document.OfType<JsonObject>()
                    .Where(row => row["title"] is JsonValue value && value.TryGetValue<string>(out var text)
                        && !string.IsNullOrWhiteSpace(text)).ToArray();
                // Detect external edits between the initial snapshot read and the mutable document read.
                var currentDocument = Parse(original, original is not null, _options.FilePath, _options.MaxTasks);
                if (snapshot.Status != currentDocument.Status || !SequenceEqual(snapshot.Items, currentDocument.Items))
                    return new ToggleTaskResult(false, "tasks changed; refresh and retry", currentDocument);
                if (operation == "add")
                {
                    if (rows.Length >= Math.Max(1, _options.MaxTasks))
                        return new ToggleTaskResult(false, "task limit reached", snapshot);
                    document.Add(new JsonObject { ["title"] = title, ["done"] = false,
                        ["date"] = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["startTime"] = startTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                        ["endTime"] = endTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
                        ["priority"] = priority.ToString().ToLowerInvariant(),
                        ["category"] = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                        ["reminderAt"] = reminderAt?.ToString("O", CultureInfo.InvariantCulture),
                        ["reminderDismissedAt"] = null });
                }
                else if (operation == "remove") document.Remove(rows[index]);
                else if (operation == "rename") rows[index]["title"] = title;
                else if (operation == "update")
                {
                    rows[index]["title"] = title;
                    rows[index]["date"] = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    rows[index]["startTime"] = startTime?.ToString("HH:mm", CultureInfo.InvariantCulture);
                    rows[index]["endTime"] = endTime?.ToString("HH:mm", CultureInfo.InvariantCulture);
                    rows[index]["priority"] = priority.ToString().ToLowerInvariant();
                    rows[index]["category"] = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
                    rows[index]["reminderAt"] = reminderAt?.ToString("O", CultureInfo.InvariantCulture);
                    rows[index]["reminderDismissedAt"] = null;
                }
                else rows[index]["done"] = !snapshot.Items[index].Done;
                json = document.ToJsonString(JsonSerializerOptions) + Environment.NewLine;

                cancellationToken.ThrowIfCancellationRequested();
                var latest = File.Exists(_options.FilePath) ? File.ReadAllText(_options.FilePath, Encoding.UTF8) : null;
                if (!string.Equals(original, latest, StringComparison.Ordinal))
                    return new ToggleTaskResult(false, "tasks changed; refresh and retry", Load());
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Platform, "tasks.serialize-failed",
                    "The task list could not be serialized; the file is left unchanged.", ex);
                return new ToggleTaskResult(false, $"serialize-failed: {ex.GetType().Name}", snapshot);
            }

            try
            {
                WriteAtomic(json);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(PlatformLogCategory.Platform, "tasks.write-failed",
                    "tasks.json could not be rewritten; the file is left unchanged.", ex);
                return new ToggleTaskResult(false, $"write-failed: {ex.GetType().Name}", snapshot);
            }

            var updated = Parse(json, fileExists: true, _options.FilePath, _options.MaxTasks);
            PublishIfChanged(updated);
            Logger.LogInfo(PlatformLogCategory.Platform, "tasks.updated",
                "Task list written back to tasks.json.", new Dictionary<string, object?>
                {
                    ["index"] = index,
                    ["operation"] = operation,
                });
            return new ToggleTaskResult(true, null, updated);
        }
        finally
        {
            _writeGate.Release();
        }
    }, cancellationToken);

    /// <summary>Parses the task file content; public so the tolerance rules are testable.</summary>
    /// <param name="json">File content.</param>
    /// <param name="fileExists">Whether the file exists (missing files are an explicit empty state).</param>
    /// <param name="path">Path used for the hint/diagnostics.</param>
    /// <param name="maxTasks">Safety cap.</param>
    public static TaskListSnapshot Parse(string? json, bool fileExists, string? path, int maxTasks = 100)
    {
        if (!fileExists)
        {
            return new TaskListSnapshot([], TaskListStatus.Missing, path, "task file not found");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            // An existing empty file simply has no tasks: that is data, not an error.
            return new TaskListSnapshot([], TaskListStatus.Ok, path, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new TaskListSnapshot([], TaskListStatus.Invalid, path, "root is not an array");
            }

            var builder = ImmutableArray.CreateBuilder<TaskItemData>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (builder.Count >= Math.Max(1, maxTasks))
                {
                    break;
                }

                if (element.ValueKind != JsonValueKind.Object
                    || !element.TryGetProperty("title", out var titleElement)
                    || titleElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(titleElement.GetString()))
                {
                    // Rows without a usable title are skipped rather than shown as fabricated placeholders.
                    continue;
                }

                var done = element.TryGetProperty("done", out var doneElement)
                           && doneElement.ValueKind == JsonValueKind.True;
                DateOnly? date = element.TryGetProperty("date", out var dateElement) && dateElement.ValueKind == JsonValueKind.String
                    && DateOnly.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, out var parsedDate) ? parsedDate : null;
                TimeOnly? startTime = element.TryGetProperty("startTime", out var timeElement) && timeElement.ValueKind == JsonValueKind.String
                    && TimeOnly.TryParse(timeElement.GetString(), CultureInfo.InvariantCulture, out var parsedTime) ? parsedTime : null;
                TimeOnly? endTime = element.TryGetProperty("endTime", out var endElement) && endElement.ValueKind == JsonValueKind.String
                    && TimeOnly.TryParse(endElement.GetString(), CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : null;
                var priority = element.TryGetProperty("priority", out var priorityElement)
                    && priorityElement.ValueKind == JsonValueKind.String
                    && Enum.TryParse<TaskPriority>(priorityElement.GetString(), true, out var parsedPriority)
                        ? parsedPriority : TaskPriority.Normal;
                var category = element.TryGetProperty("category", out var categoryElement)
                    && categoryElement.ValueKind == JsonValueKind.String ? categoryElement.GetString() : null;
                DateTimeOffset? reminderAt = element.TryGetProperty("reminderAt", out var reminderElement)
                    && reminderElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(reminderElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedReminder)
                    ? parsedReminder : null;
                DateTimeOffset? dismissedAt = element.TryGetProperty("reminderDismissedAt", out var dismissedElement)
                    && dismissedElement.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(dismissedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDismissed)
                    ? parsedDismissed : null;
                builder.Add(new TaskItemData(titleElement.GetString()!, done, date, startTime, endTime, priority, category, reminderAt, dismissedAt));
            }

            return new TaskListSnapshot(builder.ToImmutable(), TaskListStatus.Ok, path, null);
        }
        catch (JsonException ex)
        {
            return new TaskListSnapshot([], TaskListStatus.Invalid, path, $"invalid json: {ex.Message}");
        }
    }

    /// <summary>Serializes tasks in the documented shape (indented, diffable).</summary>
    /// <param name="items">Tasks to write.</param>
    public static string Serialize(IEnumerable<TaskItemData> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var payload = items.Select(item => new
        {
            title = item.Title,
            done = item.Done,
            date = item.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            startTime = item.StartTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            endTime = item.EndTime?.ToString("HH:mm", CultureInfo.InvariantCulture),
            priority = item.Priority.ToString().ToLowerInvariant(),
            category = item.Category,
            reminderAt = item.ReminderAt?.ToString("O", CultureInfo.InvariantCulture),
            reminderDismissedAt = item.ReminderDismissedAt?.ToString("O", CultureInfo.InvariantCulture),
        }).ToArray();
        return JsonSerializer.Serialize(payload, JsonSerializerOptions) + Environment.NewLine;
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

    private void WriteAtomic(string json)
    {
        var directory = Path.GetDirectoryName(_options.FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = _options.FilePath + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, _options.FilePath, overwrite: true);
    }

    private void PublishIfChanged(TaskListSnapshot snapshot)
    {
        var last = _lastPublished;
        if (last is not null
            && last.Status == snapshot.Status
            && last.FilePath == snapshot.FilePath
            && SequenceEqual(last.Items, snapshot.Items))
        {
            return;
        }

        _lastPublished = snapshot;
        Publish(snapshot);
    }

    private static bool SequenceEqual(ImmutableArray<TaskItemData> left, ImmutableArray<TaskItemData> right)
    {
        if (left.IsDefault || right.IsDefault)
        {
            return left.IsDefault && right.IsDefault;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
