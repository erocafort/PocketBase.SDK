using System.Text.Json;
using PocketBaseNet.Abstractions;

namespace PocketBaseNet;

/// <summary>
/// Async auth store port of the JS SDK <c>AsyncAuthStore</c>.
/// It serializes persistence operations through an internal queue.
/// </summary>
public sealed class AsyncAuthStore : AuthStore
{
    private readonly Func<string, Task> _save;
    private readonly Func<Task>? _clear;

    private readonly object _queueSync = new();
    private readonly Queue<Func<Task>> _queue = new();
    private bool _isProcessing;

    public AsyncAuthStore(Func<string, Task> save, Func<Task>? clear = null, object? initial = null)
    {
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _clear = clear;

        Enqueue(() => LoadInitialAsync(initial));
    }

    public override void Save(string? token, RecordModel? record = null)
    {
        base.Save(token, record);

        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(new { token = token ?? "", record });
        }
        catch
        {
            serialized = "";
        }

        Enqueue(() => _save(serialized));
    }

    public override void Clear()
    {
        base.Clear();

        if (_clear is not null)
            Enqueue(_clear);
        else
            Enqueue(() => _save(""));
    }

    private async Task LoadInitialAsync(object? initial)
    {
        try
        {
            initial = initial switch
            {
                Task<string?> t => await t.ConfigureAwait(false),
                Task<object?> t => await t.ConfigureAwait(false),
                _ => initial,
            };

            if (initial is null) return;

            Dictionary<string, JsonElement>? parsed = initial switch
            {
                string s when !string.IsNullOrWhiteSpace(s) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(s),
                JsonElement e when e.ValueKind == JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.GetRawText()),
                _ => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(initial)),
            };

            if (parsed is null) return;

            var token = parsed.TryGetValue("token", out var tokenJson) && tokenJson.ValueKind == JsonValueKind.String
                ? tokenJson.GetString() ?? ""
                : "";

            RecordModel? record = null;
            if ((parsed.TryGetValue("record", out var recordJson) || parsed.TryGetValue("model", out recordJson)) &&
                recordJson.ValueKind == JsonValueKind.Object)
            {
                record = RecordModel.FromJson(recordJson);
            }

            Save(token, record);
        }
        catch
        {
            // ignore initial payload errors (same as official SDK)
        }
    }

    private void Enqueue(Func<Task> callback)
    {
        lock (_queueSync)
        {
            _queue.Enqueue(callback);
            if (_isProcessing) return;
            _isProcessing = true;
        }

        _ = ProcessQueueAsync();
    }

    private async Task ProcessQueueAsync()
    {
        while (true)
        {
            Func<Task>? callback;
            lock (_queueSync)
            {
                if (_queue.Count == 0)
                {
                    _isProcessing = false;
                    return;
                }

                callback = _queue.Dequeue();
            }

            try
            {
                await callback().ConfigureAwait(false);
            }
            catch
            {
                // swallow persistence errors to keep the queue alive
            }
        }
    }
}
