using System.Text.Json;
using Reminders.Core;
using Reminders.Persistence;
using Reminders.Windows.Bridge;
using Xunit;

namespace Reminders.Windows.Tests;

public sealed class ProtocolTests
{
    private readonly MemoryStore store = new();
    private readonly Clock clock = new();
    private readonly StringWriter output = new();
    private BackendSession Session() => new(store, () => store.Broken = false, () => true, clock, () => TimeZoneInfo.Utc);
    private JsonElement Last => JsonDocument.Parse(output.ToString().Trim().Split('\n')[^1]).RootElement;
    private int Lines => output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

    [Fact]
    public void HeadlessIsExplicitAndPreservesDataDirectoryAndEditorPid()
    {
        var options = BackendOptions.Parse(new[] { "--headless", "--data-dir", ".", "--editor-pid", "123" });
        Assert.True(options.Headless);
        Assert.Equal(Path.GetFullPath("."), options.DataDirectory);
        Assert.Equal(123, options.EditorProcessId);
        Assert.False(BackendOptions.Parse(Array.Empty<string>()).Headless);
        Assert.Throws<ArgumentException>(() => BackendOptions.Parse(new[] { "--editor-pid", "bad" }));
        Assert.Throws<ArgumentException>(() => BackendOptions.Parse(new[] { "--data-dir" }));
    }

    [Fact]
    public void ErrorNotificationUsesMessageAndDoesNotReplaceSnapshotError()
    {
        using var protocol = new JsonLineProtocol(Session(), output);
        protocol.Notify("error", "保存失败");
        Assert.Equal("error", Last.GetProperty("event").GetString());
        Assert.Equal("保存失败", Last.GetProperty("message").GetString());
    }

    [Fact]
    public void ReadyAndSnapshotUseCamelCaseAndStringEnums()
    {
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        session.Service!.SaveTask(new() { Title = "中文", Schedule = new() { Kind = ScheduleKind.Weekly } });
        protocol.SendReady();
        Assert.Equal("ready", Last.GetProperty("event").GetString());
        var snapshot = Last.GetProperty("snapshot");
        Assert.True(snapshot.GetProperty("ready").GetBoolean());
        Assert.Equal("Weekly", snapshot.GetProperty("definitions")[0].GetProperty("schedule").GetProperty("kind").GetString());
        Assert.Equal("UTC", snapshot.GetProperty("timeZoneId").GetString());
        protocol.ProcessLine("{\"id\":1,\"method\":\"snapshot\",\"payload\":{}}");
        Assert.Equal(1, Last.GetProperty("id").GetInt32());
        Assert.Equal("中文", Last.GetProperty("result").GetProperty("tasks")[0].GetProperty("task").GetProperty("title").GetString());
    }

    [Fact]
    public void SaveCompleteUndoAndSettingsReturnPersistedSnapshots()
    {
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        var id = Guid.NewGuid();
        protocol.ProcessLine(Request(1, "saveTask", new { task = new { id, title = "  清单  ", schedule = new { kind = "Daily", resetTime = "05:00:00" } } }));
        Assert.Equal("清单", Last.GetProperty("result").GetProperty("definitions")[0].GetProperty("title").GetString());
        protocol.ProcessLine(Request(2, "complete", new { id }));
        Assert.True(Last.GetProperty("result").GetProperty("tasks")[0].GetProperty("isCompleted").GetBoolean());
        Assert.Single(store.State.Events);
        protocol.ProcessLine(Request(3, "undo", new { id }));
        Assert.False(Last.GetProperty("result").GetProperty("tasks")[0].GetProperty("isCompleted").GetBoolean());
        protocol.ProcessLine("{\"id\":4,\"method\":\"updateSettings\",\"payload\":{\"settings\":{\"fontSize\":20}}}");
        Assert.Equal(20, Last.GetProperty("result").GetProperty("overlay").GetProperty("fontSize").GetDouble());
    }

    [Fact]
    public void FailureReturnsErrorWithoutChangedSnapshot()
    {
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        var task = new TaskDefinition { Title = "失败保护" };
        session.Service!.SaveTask(task);
        output.GetStringBuilder().Clear();
        store.Fail = true;
        protocol.ProcessLine(Request(9, "complete", new { id = task.Id }));
        Assert.Equal(9, Last.GetProperty("id").GetInt32());
        Assert.Contains("失败", Last.GetProperty("error").GetString());
        Assert.Equal(1, Lines);
        Assert.False(session.Service.GetTasks()[0].IsCompleted);
    }

    [Fact]
    public void CorruptStateStaysAliveAndExplicitRestoreRebuildsService()
    {
        store.Broken = true;
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        protocol.SendReady();
        Assert.False(Last.GetProperty("snapshot").GetProperty("ready").GetBoolean());
        Assert.True(Last.GetProperty("snapshot").GetProperty("canRestoreBackup").GetBoolean());
        protocol.ProcessLine("{\"id\":1,\"method\":\"complete\",\"payload\":{\"id\":\"00000000-0000-0000-0000-000000000001\"}}");
        Assert.True(Last.TryGetProperty("error", out _));
        var replaced = 0;
        session.ServiceReplaced += (_, _) => replaced++;
        protocol.ProcessLine("{\"id\":2,\"method\":\"restoreBackup\",\"payload\":{}}");
        Assert.True(Last.GetProperty("result").GetProperty("ready").GetBoolean());
        Assert.Equal(1, replaced);
    }

    [Fact]
    public void FutureSchemaDisablesRecovery()
    {
        store.Broken = true; store.CanRestore = false;
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        protocol.SendReady();
        Assert.False(Last.GetProperty("snapshot").GetProperty("canRestoreBackup").GetBoolean());
        protocol.ProcessLine("{\"id\":1,\"method\":\"restoreBackup\",\"payload\":{}}");
        Assert.True(Last.TryGetProperty("error", out _));
        Assert.True(store.Broken);
    }

    [Fact]
    public void TimerOnlyPublishesWhenTimeProjectionChanges()
    {
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        var task = new TaskDefinition { Title = "日常", Schedule = new() { ResetTime = new(5, 0) } };
        session.Service!.SaveTask(task);
        output.GetStringBuilder().Clear();
        session.RefreshTime();
        clock.Now = clock.Now.AddMinutes(1);
        session.RefreshTime();
        Assert.Equal(0, Lines);
        clock.Now = clock.Now.AddDays(1);
        session.RefreshTime();
        Assert.Equal(1, Lines);
        Assert.Equal("changed", Last.GetProperty("event").GetString());
    }

    [Fact]
    public void VisibilityAndExitAreExplicitSignals()
    {
        var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        var visibility = 0; var exit = 0;
        session.EditorVisibilityChanged += (_, _) => visibility++;
        protocol.ExitRequested += (_, _) => exit++;
        protocol.ProcessLine("{\"id\":1,\"method\":\"editorVisible\",\"payload\":{\"visible\":true}}");
        Assert.True(session.IsVisible);
        Assert.Equal(1, visibility);
        protocol.ProcessLine("{\"id\":2,\"method\":\"exit\",\"payload\":{}}");
        Assert.Equal("exit", Last.GetProperty("event").GetString());
        Assert.Contains("\"id\":2", output.ToString());
        Assert.Equal(1, exit);
    }

    [Theory]
    [InlineData("{\"id\":42,\"method\":\"unknown\",\"payload\":{}}")]
    [InlineData("{\"id\":42,\"method\":\"complete\",\"payload\":{}}")]
    [InlineData("{\"id\":42,\"method\":\"saveTask\",\"payload\":null}")]
    [InlineData("not json")]
    public void InvalidRequestsProduceJsonErrorsAndKeepProtocolAlive(string line)
    {
        using var protocol = new JsonLineProtocol(Session(), output);
        protocol.ProcessLine(line);
        Assert.True(Last.TryGetProperty("error", out _));
        protocol.ProcessLine("{\"id\":99,\"method\":\"snapshot\",\"payload\":{}}");
        Assert.Equal(99, Last.GetProperty("id").GetInt32());
        Assert.True(Last.TryGetProperty("result", out _));
    }

    private static string Request(int id, string method, object payload) => JsonSerializer.Serialize(new { id, method, payload });

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class MemoryStore : IStateStore
    {
        public AppState State { get; private set; } = new();
        public bool Broken { get; set; }
        public bool CanRestore { get; set; } = true;
        public bool Fail { get; set; }
        public AppState Load() => Broken ? throw new StateLoadException("数据损坏", canRestoreBackup: CanRestore) : State;
        public void Save(AppState state) { if (Fail) throw new IOException("模拟写入失败"); State = state; }
    }
}
