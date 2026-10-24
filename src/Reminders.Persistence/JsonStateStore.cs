using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Reminders.Core;

namespace Reminders.Persistence;

public sealed class JsonStateStore : IStateStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // 持久化记录必须完整：缺失 Id 等字段不能触发模型默认值生成新身份。
            if (info.Kind == JsonTypeInfoKind.Object)
                foreach (var property in info.Properties) property.IsRequired = true;
        });
        return new()
        {
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            TypeInfoResolver = resolver
        };
    }

    public JsonStateStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }
    public bool HasBackup => File.Exists(BackupPath);
    private string BackupPath => FilePath + ".bak";

    public AppState Load()
    {
        if (File.Exists(FilePath)) return ReadState(FilePath);
        if (HasBackup) throw new StateLoadException("主数据文件缺失，存在备份。请确认后恢复备份。");
        return new();
    }

    public void Save(AppState state)
    {
        StateValidator.Validate(state);
        // 每次替换前验证主文件，防止损坏文件或未来版本覆盖最后一个有效备份。
        if (File.Exists(FilePath) || HasBackup) Load();
        WriteAtomically(state, preserveBackup: false);
    }

    public AppState RestoreBackup()
    {
        if (File.Exists(FilePath))
        {
            try { ReadState(FilePath); }
            catch (StateLoadException exception) when (exception.CanRestoreBackup) { }
        }
        if (!HasBackup) throw new StateLoadException("未找到可恢复的备份。");
        var state = ReadState(BackupPath);
        // 恢复时保留原备份，不将损坏的主文件轮换成备份。
        WriteAtomically(state, preserveBackup: true);
        return state;
    }

    private static AppState ReadState(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("SchemaVersion", out var version) || !version.TryGetInt32(out var schema))
                throw new JsonException("数据版本字段缺失或无效。");
            if (schema != 1)
                throw new StateLoadException($"数据版本 {schema} 不受此应用支持，请使用匹配版本的应用。", canRestoreBackup: false);
            foreach (var name in new[] { "Tasks", "Events", "Overlay" })
                if (!root.TryGetProperty(name, out _)) throw new JsonException($"数据字段 {name} 缺失。");
            var state = JsonSerializer.Deserialize<AppState>(bytes, Options) ?? throw new JsonException("数据为空。");
            StateValidator.Validate(state);
            return state;
        }
        catch (StateLoadException) { throw; }
        catch (Exception exception) when (exception is JsonException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new StateLoadException("无法读取任务数据，原文件已保留。请检查文件或确认恢复有效备份。", exception);
        }
    }

    private void WriteAtomically(AppState state, bool preserveBackup)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, Path.GetFileName(FilePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, state, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Replace(temporaryPath, FilePath, preserveBackup ? null : BackupPath);
            else File.Move(temporaryPath, FilePath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
