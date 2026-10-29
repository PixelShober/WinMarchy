using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace WinMarchy.Core.GlazeWm;

/// <summary>A GlazeWM event (window_managed, focus_changed, …) with its raw JSON data.</summary>
public sealed record GlazeEvent(string Type, JsonElement Data);

/// <summary>
/// Talks to GlazeWM over its local WebSocket (ws://localhost:6123): queries, commands and an event
/// subscription. One request at a time; events arrive in between and are handed to <see cref="Event"/>.
/// </summary>
public sealed class GlazeIpc : IAsyncDisposable
{
    public const int Port = 6123;
    readonly SemaphoreSlim _send = new(1, 1);
    readonly object _pendingLock = new();
    ClientWebSocket? _ws;
    CancellationTokenSource? _readCts;
    Task? _reader;
    TaskCompletionSource<JsonElement>? _pending;
    string? _pendingMessage;
    int _unanswered;

    public event Action<GlazeEvent>? Event;
    public event Action? Disconnected;

    public bool Connected => _ws?.State == WebSocketState.Open;

    /// <summary>Why the last <see cref="ConnectAsync"/> failed, for the log.</summary>
    public string? LastError { get; private set; }

    /// <summary>Requests in a row that GlazeWM left without an answer (5 seconds each). 0 after any answer.</summary>
    public int Unanswered => Volatile.Read(ref _unanswered);

    public async Task<bool> ConnectAsync(CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        // Straight to 127.0.0.1. With the system proxy (usual in companies) the connection would go
        // to the proxy, which refuses or answers too late.
        ws.Options.Proxy = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}"), cts.Token);
            _ws = ws;
            _readCts = new CancellationTokenSource();
            _reader = Task.Run(() => ReadLoopAsync(ws, _readCts.Token));
            LastError = null;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LastError = ex is OperationCanceledException ? "Timeout" : Net.Describe(ex);
            ws.Dispose();
            return false;
        }
    }

    /// <summary>"query workspaces", "command --id … set-floating", "sub -e window_managed".</summary>
    public async Task<JsonElement> SendAsync(string message, CancellationToken ct)
    {
        var ws = _ws ?? throw new WinMarchyException("glazeNotConnected");
        await _send.WaitAsync(ct);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool queries = message.StartsWith("query", StringComparison.Ordinal);
        try
        {
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock)
            {
                _pending = tcs;
                _pendingMessage = message;
            }
            await ws.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using (timeout.Token.Register(() => tcs.TrySetCanceled()))
            {
                var result = await tcs.Task;
                Volatile.Write(ref _unanswered, 0);
                // Queries are many and say little; commands are what a fault report needs.
                if (!queries) Log.Trace($"{message} → ok ({clock.ElapsedMilliseconds} ms)", "glazewm");
                return result;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _unanswered);
            Log.Trace($"{message} → timeout ({clock.ElapsedMilliseconds} ms)", "glazewm");
            throw new WinMarchyException("glazeNoAnswer", ("command", message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Trace($"{message} → {Net.Describe(ex)} ({clock.ElapsedMilliseconds} ms)", "glazewm");
            throw;
        }
        finally
        {
            lock (_pendingLock)
            {
                _pending = null;
                _pendingMessage = null;
            }
            _send.Release();
        }
    }

    public Task<JsonElement> QueryAsync(string what, CancellationToken ct) => SendAsync("query " + what, ct);

    /// <summary>
    /// Whether GlazeWM answers on a fresh connection. Tells a stuck connection (connect again) from a
    /// GlazeWM that hangs (only a restart helps).
    /// </summary>
    public static async Task<bool> ProbeAsync(CancellationToken ct)
    {
        await using var probe = new GlazeIpc();
        if (!await probe.ConnectAsync(ct)) return false;
        try
        {
            await probe.QueryAsync("focused", ct);
            return true;
        }
        catch (WinMarchyException)
        {
            return false;
        }
    }

    public Task<JsonElement> CommandAsync(string command, CancellationToken ct, string? containerId = null) =>
        SendAsync(containerId == null ? "command " + command : $"command --id {containerId} {command}", ct);

    public Task SubscribeAsync(IEnumerable<string> events, CancellationToken ct) =>
        SendAsync("sub -e " + string.Join(' ', events), ct);

    async Task ReadLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[1 << 16];
        var message = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                Dispatch(text);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // GlazeWM exited or reloaded; the agent reconnects.
        }
        lock (_pendingLock) _pending?.TrySetException(new WinMarchyException("glazeNotConnected"));
        Disconnected?.Invoke();
    }

    void Dispatch(string text)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(text); }
        catch { return; }
        var root = doc.RootElement.Clone();
        doc.Dispose();

        var type = root.TryGetProperty("messageType", out var mt) ? mt.GetString() : null;
        if (type == "client_response")
        {
            lock (_pendingLock)
            {
                if (_pending == null) return;
                var echo = root.TryGetProperty("clientMessage", out var cm) ? cm.GetString() : null;
                if (echo != null && _pendingMessage != null && echo != _pendingMessage) return;
                if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                    _pending.TrySetException(new WinMarchyException("glazeCommandFailed",
                        ("detail", root.TryGetProperty("error", out var err) ? err.GetString() : "?")));
                else
                    _pending.TrySetResult(root.TryGetProperty("data", out var data) ? data : default);
            }
        }
        else if (type == "event_subscription" && root.TryGetProperty("data", out var ev) && ev.ValueKind == JsonValueKind.Object)
        {
            var eventType = ev.TryGetProperty("eventType", out var et) ? et.GetString() ?? "" : "";
            try { Event?.Invoke(new GlazeEvent(eventType, ev)); }
            catch (Exception ex) { Log.Debug("GlazeWM event handler: " + ex.Message); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _readCts?.Cancel(); } catch { }
        if (_ws is { State: WebSocketState.Open } ws)
        {
            // A GlazeWM that hangs never answers the close either.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", timeout.Token); } catch { }
        }
        _ws?.Dispose();
        if (_reader != null) { try { await _reader; } catch { } }
    }
}
