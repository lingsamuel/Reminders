namespace Reminders.Persistence;

/// <summary>读取失败不会覆盖原文件；不支持的版本也禁止使用旧备份降级。</summary>
public sealed class StateLoadException : Exception
{
    public StateLoadException(string message, Exception? innerException = null, bool canRestoreBackup = true)
        : base(message, innerException) => CanRestoreBackup = canRestoreBackup;

    public bool CanRestoreBackup { get; }
}
