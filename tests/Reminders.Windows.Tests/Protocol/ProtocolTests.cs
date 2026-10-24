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
    public void SnapshotReloadsExternallyRepairedPrimaryWithoutWritingOrRestoring()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RemindersProtocolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "state.json");
            var jsonStore = new JsonStateStore(path);
            jsonStore.Save(new());
            jsonStore.Save(new() { Tasks = new[] { new TaskDefinition { Title = "外部修复的任务" } } });
            var repairedBytes = File.ReadAllBytes(path);
            var backupBytes = File.ReadAllBytes(path + ".bak");
            File.WriteAllText(path, "损坏内容");
            var tracked = new TrackingStore(jsonStore);
            var restored = 0;
            using var session = new BackendSession(tracked, () => { restored++; jsonStore.RestoreBackup(); }, () => jsonStore.HasBackup, clock, () => TimeZoneInfo.Utc);
            using var protocol = new JsonLineProtocol(session, output);
            var replaced = 0;
            var changed = 0;
            session.ServiceReplaced += (_, _) => replaced++;
            session.Changed += (_, _) => changed++;
            Assert.False(session.Snapshot().Ready);
            Assert.True(session.Snapshot().CanRestoreBackup);
            protocol.ProcessLine(Request(1, "snapshot", new { }));
            Assert.False(Last.GetProperty("result").GetProperty("ready").GetBoolean());
            Assert.Equal(0, replaced);

            // 模拟用户用外部编辑器修复主文件，“重新读取”只尝试读取该文件。
            File.WriteAllBytes(path, repairedBytes);
            protocol.ProcessLine(Request(2, "snapshot", new { }));
            Assert.True(Last.GetProperty("result").GetProperty("ready").GetBoolean());
            Assert.Equal("外部修复的任务", Last.GetProperty("result").GetProperty("definitions")[0].GetProperty("title").GetString());
            Assert.Null(session.Snapshot().Error);
            Assert.Equal(1, replaced);
            Assert.Equal(1, changed);
            Assert.Contains("\"event\":\"changed\"", output.ToString());
            Assert.Equal(3, tracked.LoadCalls);
            protocol.ProcessLine(Request(3, "snapshot", new { }));
            Assert.Equal(3, tracked.LoadCalls);
            Assert.Equal(1, replaced);
            Assert.Equal(1, changed);
            Assert.Equal(0, restored);
            Assert.Equal(0, tracked.SaveCalls);
            Assert.Equal(repairedBytes, File.ReadAllBytes(path));
            Assert.Equal(backupBytes, File.ReadAllBytes(path + ".bak"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FutureSchemaSnapshotDoesNotRestoreOrOverwritePrimary()
    {
        store.Broken = true;
        store.CanRestore = false;
        var restored = 0;
        using var session = new BackendSession(store, () => restored++, () => true, clock, () => TimeZoneInfo.Utc);
        using var protocol = new JsonLineProtocol(session, output);
        var replaced = 0;
        session.ServiceReplaced += (_, _) => replaced++;
        protocol.ProcessLine(Request(1, "snapshot", new { }));
        protocol.ProcessLine(Request(2, "snapshot", new { }));
        Assert.False(Last.GetProperty("result").GetProperty("ready").GetBoolean());
        Assert.False(Last.GetProperty("result").GetProperty("canRestoreBackup").GetBoolean());
        Assert.True(store.Broken);
        Assert.Equal(0, restored);
        Assert.Equal(0, replaced);
        Assert.Equal(3, store.LoadCalls);
        Assert.Equal(0, store.SaveCalls);
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
    public void SequenceTracksStateAndMidnightWhileMatchingEventAndResponse()
    {
        using var session = Session();
        using var protocol = new JsonLineProtocol(session, output);
        Assert.Equal(0, session.Snapshot().Sequence);
        var task = new TaskDefinition { Title = "五点重置", Schedule = new() { ResetTime = new(5, 0) } };
        session.Service!.SaveTask(task);
        Assert.Equal(1, session.Snapshot().Sequence);
        protocol.ProcessLine(Request(1, "snapshot", new { }));
        protocol.ProcessLine(Request(2, "snapshot", new { }));
        Assert.Equal(1, Last.GetProperty("result").GetProperty("sequence").GetInt64());

        output.GetStringBuilder().Clear();
        protocol.ProcessLine(Request(3, "complete", new { id = task.Id }));
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var changed = JsonDocument.Parse(lines[0]);
        Assert.Equal("changed", changed.RootElement.GetProperty("event").GetString());
        Assert.Equal(2, changed.RootElement.GetProperty("snapshot").GetProperty("sequence").GetInt64());
        Assert.Equal(2, Last.GetProperty("result").GetProperty("sequence").GetInt64());
        protocol.ProcessLine(Request(4, "undo", new { id = task.Id }));
        Assert.Equal(3, Last.GetProperty("result").GetProperty("sequence").GetInt64());
        session.Service.SaveTask(task with { Schedule = task.Schedule with { ResetTime = new(4, 0) } });
        Assert.Equal(4, session.Snapshot().Sequence);

        clock.Now = DateTimeOffset.Parse("2026-10-24T23:59:00Z");
        session.RefreshTime();
        Assert.Equal(4, session.Snapshot().Sequence);
        var key = session.Snapshot().Tasks[0].Period!.Key;
        clock.Now = clock.Now.AddMinutes(1);
        session.RefreshTime();
        Assert.Equal(key, session.Snapshot().Tasks[0].Period!.Key);
        Assert.Equal(5, session.Snapshot().Sequence);
        session.RefreshTime();
        protocol.ProcessLine(Request(5, "snapshot", new { }));
        Assert.Equal(5, Last.GetProperty("result").GetProperty("sequence").GetInt64());
    }

    [Fact]
    public void DateProjectionUsesLocalMidnightRatherThanUtcMidnight()
    {
        clock.Now = DateTimeOffset.Parse("2026-10-24T15:59:00Z");
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/Plus8", TimeSpan.FromHours(8), "Test +08", "Test +08");
        using var session = new BackendSession(store, () => { }, () => false, clock, () => zone);
        session.Service!.SaveTask(new() { Title = "本地日常", Schedule = new() { ResetTime = new(5, 0) } });
        var sequence = session.Snapshot().Sequence;
        var key = session.Snapshot().Tasks[0].Period!.Key;
        clock.Now = clock.Now.AddMinutes(1);
        session.RefreshTime();
        Assert.Equal(key, session.Snapshot().Tasks[0].Period!.Key);
        Assert.Equal(sequence + 1, session.Snapshot().Sequence);
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
    private sealed class TrackingStore(IStateStore inner) : IStateStore
    {
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public AppState Load() { LoadCalls++; return inner.Load(); }
        public void Save(AppState state) { SaveCalls++; inner.Save(state); }
    }

    private sealed class MemoryStore : IStateStore
    {
        public AppState State { get; private set; } = new();
        public bool Broken { get; set; }
        public bool CanRestore { get; set; } = true;
        public bool Fail { get; set; }
        public int LoadCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public AppState Load() { LoadCalls++; return Broken ? throw new StateLoadException("数据损坏", canRestoreBackup: CanRestore) : State; }
        public void Save(AppState state) { SaveCalls++; if (Fail) throw new IOException("模拟写入失败"); State = state; }
    }
}
