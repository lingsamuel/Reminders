using System;
using System.IO;
using System.Text.Json;

namespace Reminders.Windows.Bridge;

/// <summary>每行独立请求；所有输出持锁写入一整行，保证事件与响应不会交错。</summary>
internal sealed class JsonLineProtocol : IDisposable
{
    private readonly BackendSession session;
    private readonly TextWriter output;
    private readonly object outputLock = new();

    public JsonLineProtocol(BackendSession session, TextWriter output)
    {
        this.session = session;
        this.output = output;
        session.Changed += StateChanged;
    }

    public event EventHandler? ExitRequested;

    public void ProcessLine(string line)
    {
        long? id = null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var value) || !value.TryGetInt64(out var number))
                throw new ArgumentException("请求需要整数 id。");
            id = number;
            var method = root.GetProperty("method").GetString() ?? throw new ArgumentException("请求需要 method。");
            var payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object) throw new ArgumentException("payload 必须为对象。");
            session.Execute(method, payload);
            Write(new { id, result = session.Snapshot() });
            if (method == "exit")
            {
                Notify("exit");
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception error)
        {
            Write(new { id, error = error.Message });
        }
    }

    public void SendReady() => Write(new { @event = "ready", snapshot = session.Snapshot() });
    public void Notify(string name, string? message = null) => Write(new { @event = name, message });
    private void StateChanged(object? sender, EventArgs args) => Write(new { @event = "changed", snapshot = session.Snapshot() });
    private void Write(object value)
    {
        var line = JsonSerializer.Serialize(value, BridgeJson.Options);
        lock (outputLock)
        {
            output.WriteLine(line);
            output.Flush();
        }
    }

    public void Dispose() => session.Changed -= StateChanged;
}
