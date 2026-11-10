using FluidBar.Platform.Windows.Diagnostics;
using FluidBar.Platform.Windows.Tasks;

namespace FluidBar.Platform.Tests;

public sealed class TaskManagementTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fluidbar-task-management-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "nested", "tasks.json");
    private TaskListService Create(int maxTasks = 100) => new(NullPlatformLogger.Instance, new PlatformCounters(),
        new TaskListOptions { FilePath = FilePath, MaxTasks = maxTasks });

    [Fact]
    public async Task Add_CreatesMissingFile_ThenRenameToggleRemovePersist()
    {
        await using var service = Create();
        var added = await service.AddAsync("  原样保留  ");
        Assert.True(added.Succeeded);
        Assert.Equal("  原样保留  ", added.Snapshot.Items[0].Title);
        Assert.True((await service.RenameAsync(0, "new title")).Succeeded);
        Assert.True((await service.ToggleAsync(0)).Snapshot.Items[0].Done);
        Assert.True((await service.RemoveAsync(0)).Succeeded);
        Assert.Empty(service.Load().Items);
    }

    [Theory]
    [InlineData("{ broken")]
    [InlineData("{\"title\":\"wrong root\"}")]
    public async Task Mutations_RefuseInvalidDocumentWithoutChangingBytes(string json)
    {
        Write(json);
        await using var service = Create();
        Assert.False((await service.AddAsync("new")).Succeeded);
        Assert.False((await service.RenameAsync(0, "new")).Succeeded);
        Assert.False((await service.RemoveAsync(0)).Succeeded);
        Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task Editing_PreservesOtherRowsUnknownFieldsAndWhitespace()
    {
        Write("""[{"title":"  one  ","done":false,"extra":"keep"},{"legacy":42},{"title":"two","done":true}]""");
        await using var service = Create();
        Assert.True((await service.RenameAsync(1, "renamed")).Succeeded);
        var json = File.ReadAllText(FilePath);
        Assert.Contains("  one  ", json);
        Assert.Contains("\"extra\": \"keep\"", json);
        Assert.Contains("\"legacy\": 42", json);
        Assert.Equal("  one  ", service.Load().Items[0].Title);
    }

    [Fact]
    public async Task ValidationAndStaleIndices_DoNotWrite()
    {
        await using var service = Create(maxTasks: 1);
        Assert.False((await service.AddAsync(" ")).Succeeded);
        Assert.False((await service.AddAsync(new string('x', 501))).Succeeded);
        var first = await service.AddAsync("one");
        Assert.False((await service.AddAsync("two")).Succeeded);
        Assert.False((await service.RenameAsync(-1, "other")).Succeeded);
        Assert.False((await service.RemoveAsync(9)).Succeeded);
        Assert.True((await service.RenameAsync(0, "changed")).Succeeded);
        Assert.False((await service.RemoveAsync(0, expectedSnapshot: first.Snapshot)).Succeeded);
        Assert.Equal("changed", service.Load().Items[0].Title);
    }

    [Fact]
    public async Task ConcurrentAdds_AreSerializedWithoutLosingRows()
    {
        await using var service = Create();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => service.AddAsync("task " + i)));
        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Equal(20, service.Load().Count);
        Assert.Equal(20, service.Load().Items.Select(item => item.Title).Distinct().Count());
    }

    [Fact]
    public async Task EditingACappedList_PreservesTasksBeyondTheDisplayLimit()
    {
        Write("""[{"title":"one","done":false},{"title":"two","done":true},{"title":"  third  ","done":false}]""");
        await using var service = Create(maxTasks: 1);
        Assert.True((await service.ToggleAsync(0)).Succeeded);
        var complete = TaskListService.Parse(File.ReadAllText(FilePath), true, FilePath);
        Assert.Equal(3, complete.Count);
        Assert.Equal("two", complete.Items[1].Title);
        Assert.True(complete.Items[1].Done);
        Assert.Equal("  third  ", complete.Items[2].Title);
        Assert.False((await service.AddAsync("fourth")).Succeeded);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, json);
    }

    [Fact]
    public async Task ScheduledTasks_RoundTripEditAndRejectStaleSnapshot()
    {
        await using var service = Create();
        var date = new DateOnly(2026, 11, 7);
        var added = await service.AddScheduledAsync("meeting", date, new TimeOnly(9, 30));
        Assert.True(added.Succeeded);
        Assert.Equal(date, service.Load().Items[0].Date);
        var edited = await service.UpdateAsync(0, "new", date.AddDays(1), new TimeOnly(10, 0),
            new TimeOnly(11, 0), TaskPriority.High, "Work", added.Snapshot);
        Assert.True(edited.Succeeded);
        Assert.Equal(new TimeOnly(10, 0), service.Load().Items[0].StartTime);
        Assert.False((await service.UpdateAsync(0, "stale", date, null, null,
            TaskPriority.Normal, null, added.Snapshot)).Succeeded);
    }

    [Fact]
    public void OptionalSchedule_InvalidTypesDoNotBreakLegacyTasks()
    {
        var parsed = TaskListService.Parse("""[{"title":"old"},{"title":"bad","date":12,"startTime":false}]""", true, "tasks.json");
        Assert.Equal(2, parsed.Count);
        Assert.All(parsed.Items, item => Assert.Null(item.Date));
    }

    [Fact]
    public async Task Reminder_RoundTripsAndEditResetsDismissedState()
    {
        await using var service = Create();
        var date = new DateOnly(2026, 11, 7);
        var reminder = new DateTimeOffset(2026, 11, 7, 9, 25, 0, TimeSpan.FromHours(8));
        var added = await service.AddScheduledAsync("review", date, new TimeOnly(9, 30), reminderAt: reminder);
        Assert.True(added.Succeeded);
        Assert.Equal(reminder, service.Load().Items[0].ReminderAt);

        var json = File.ReadAllText(FilePath).Replace("\"reminderDismissedAt\": null", "\"reminderDismissedAt\": \"2026-11-07T09:26:00+08:00\"");
        File.WriteAllText(FilePath, json);
        var current = service.Load();
        Assert.NotNull(current.Items[0].ReminderDismissedAt);
        var changed = await service.UpdateAsync(0, "review", date, new TimeOnly(9, 30), null,
            TaskPriority.Normal, null, current, reminder);
        Assert.True(changed.Succeeded);
        Assert.Null(service.Load().Items[0].ReminderDismissedAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
