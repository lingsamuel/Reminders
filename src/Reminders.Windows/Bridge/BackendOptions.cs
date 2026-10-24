using System;
using System.IO;

namespace Reminders.Windows.Bridge;

internal sealed record BackendOptions(string DataDirectory, int? EditorProcessId, bool Headless)
{
    public static BackendOptions Parse(string[] args)
    {
        var dataPath = Argument(args, "--data-dir");
        var directory = dataPath is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reminders")
            : Path.GetFullPath(dataPath);
        var process = Argument(args, "--editor-pid");
        int? editorProcessId = null;
        if (process is not null)
        {
            if (!int.TryParse(process, out var id) || id <= 0) throw new ArgumentException("--editor-pid 后需要提供有效进程标识。");
            editorProcessId = id;
        }
        return new(directory, editorProcessId, Array.IndexOf(args, "--headless") >= 0);
    }

    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} 后需要提供参数值。");
        return args[index + 1];
    }
}
