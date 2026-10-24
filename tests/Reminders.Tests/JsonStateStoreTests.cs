using System.Text.Json;
using Reminders.Core;
using Reminders.Persistence;
using Xunit;

namespace Reminders.Tests;

public sealed class JsonStateStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "RemindersTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "state.json");
    private JsonStateStore Store => new(FilePath);
    private static AppState WithTitle(string title) => new() { Tasks = new[] { new TaskDefinition { Title = title } } };

    [Fact]
    public void FreshStoreAndChineseRoundTrip()
    {
        Assert.Empty(Store.Load().Tasks);
        var service = new TaskService(Store);
        var task = new TaskDefinition { Title = "中文每日任务" };
        service.SaveTask(task);
        service.Complete(task.Id);
        var restored = new TaskService(Store);
        Assert.Equal("中文每日任务", restored.State.Tasks[0].Title);
        Assert.True(restored.GetTasks()[0].IsCompleted);
        Assert.Single(restored.State.Events);
    }

    [Fact]
    public void SaveKeepsPreviousValidBackupAndExplicitRestorePreservesIt()
    {
        Store.Save(WithTitle("第一版"));
        Store.Save(WithTitle("第二版"));
        Assert.True(Store.HasBackup);
        var backupBytes = File.ReadAllBytes(FilePath + ".bak");
        File.WriteAllText(FilePath, "corrupt");
        Assert.Throws<StateLoadException>(() => Store.Load());
        Assert.Equal("corrupt", File.ReadAllText(FilePath));
        Assert.Equal("第一版", Store.RestoreBackup().Tasks[0].Title);
        Assert.Equal(backupBytes, File.ReadAllBytes(FilePath + ".bak"));
        Assert.Equal("第一版", Store.Load().Tasks[0].Title);
    }

    [Fact]
    public void MissingPrimaryWithBackupRequiresExplicitRecovery()
    {
        Store.Save(WithTitle("一"));
        Store.Save(WithTitle("二"));
        File.Delete(FilePath);
        Assert.Throws<StateLoadException>(() => Store.Load());
        Assert.Equal("一", Store.RestoreBackup().Tasks[0].Title);
    }

    [Fact]
    public void InvalidBackupDoesNotReplacePrimary()
    {
        Store.Save(WithTitle("有效"));
        File.WriteAllText(FilePath + ".bak", "bad");
        Assert.Throws<StateLoadException>(() => Store.RestoreBackup());
        Assert.Equal("有效", Store.Load().Tasks[0].Title);
    }

    [Fact]
    public void NewerSchemaCannotBeOverwrittenOrRecovered()
    {
        Store.Save(WithTitle("旧版本"));
        Store.Save(WithTitle("备份"));
        File.WriteAllText(FilePath, "{\"SchemaVersion\":2,\"Tasks\":[],\"Events\":[],\"Overlay\":{}}");
        Assert.False(Assert.Throws<StateLoadException>(() => Store.Load()).CanRestoreBackup);
        Assert.Throws<StateLoadException>(() => Store.Save(new()));
        Assert.Throws<StateLoadException>(() => Store.RestoreBackup());
        Assert.Contains("2", File.ReadAllText(FilePath));
    }

    [Fact]
    public void CorruptPrimaryCannotBeOverwrittenBySave()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "broken");
        Assert.Throws<StateLoadException>(() => Store.Save(new()));
        Assert.Equal("broken", File.ReadAllText(FilePath));
    }

    [Fact]
    public void ValidationRejectsDuplicateIdsUnknownEnumsAndDanglingEvents()
    {
        var task = new TaskDefinition { Title = "重复" };
        Assert.Throws<ArgumentException>(() => Store.Save(new() { Tasks = new[] { task, task } }));
        Assert.Throws<ArgumentException>(() => Store.Save(new() { Tasks = new[] { task with { Schedule = new() { Kind = (ScheduleKind)99 } } } }));
        Assert.Throws<ArgumentException>(() => Store.Save(new() { Events = new[] { new CompletionEvent { TaskId = Guid.NewGuid() } } }));
        Assert.Throws<ArgumentException>(() => Store.Save(new() { Overlay = new() { Opacity = double.NaN } }));
        Assert.Throws<ArgumentException>(() => Store.Save(new() { Overlay = new() { Height = 0 } }));
    }

    [Fact]
    public void LoadValidatesDataRatherThanOnlyJsonSyntax()
    {
        Directory.CreateDirectory(directory);
        var task = new TaskDefinition { Title = "重复" };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new AppState { Tasks = new[] { task, task } }));
        Assert.Throws<StateLoadException>(() => Store.Load());
        File.WriteAllText(FilePath, "{\"SchemaVersion\":1,\"Tasks\":null}");
        Assert.Throws<StateLoadException>(() => Store.Load());
    }

    [Fact]
    public void MissingTaskIdentityCannotSilentlyGenerateANewTask()
    {
        Store.Save(WithTitle("有效"));
        var text = File.ReadAllText(FilePath);
        var id = Store.Load().Tasks[0].Id.ToString();
        text = text.Replace($"\"Id\": \"{id}\",", "");
        File.WriteAllText(FilePath, text);
        Assert.Throws<StateLoadException>(() => Store.Load());
    }

    [Fact]
    public void FailedReplacementCleansTemporaryFile()
    {
        Directory.CreateDirectory(FilePath);
        Assert.ThrowsAny<IOException>(() => Store.Save(new()));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
