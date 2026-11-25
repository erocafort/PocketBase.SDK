using System.Text;
using PocketBase.SDK.Abstractions;

namespace PocketBase.SDK;

/// <summary>
/// Line based "text/event-stream" parser following the WHATWG EventSource interpretation rules
/// (the JS SDK relies on the browser <c>EventSource</c>; the Dart SDK has a simplified regex parser).
/// Lines are expected without the line terminator (as returned by <see cref="TextReader.ReadLine"/>).
/// </summary>
internal sealed class SseParser
{
    private readonly StringBuilder _data = new();
    private bool _hasData;
    private string _event = "";
    private string _lastEventId = "";
    private int _retry;

    /// <summary>Processes a single line and returns the dispatched message when <paramref name="line"/> is the empty message delimiter.</summary>
    public SseMessage? ProcessLine(string line)
    {
        if (line.Length == 0) return Dispatch();

        if (line[0] == ':') return null; // comment (e.g. keep-alive ping)

        string field, value;
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            field = line;
            value = "";
        }
        else
        {
            field = line[..colon];
            value = line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
        }

        switch (field)
        {
            case "event":
                _event = value;
                break;
            case "data":
                if (_hasData) _data.Append('\n');
                _data.Append(value);
                _hasData = true;
                break;
            case "id":
                if (!value.Contains('\0')) _lastEventId = value;
                break;
            case "retry":
                if (value.Length > 0 && value.All(char.IsAsciiDigit) && int.TryParse(value, out var retry)) _retry = retry;
                break;
        }

        return null;
    }

    private SseMessage? Dispatch()
    {
        try
        {
            // per spec, events without data are not dispatched
            if (!_hasData) return null;

            return new SseMessage
            {
                Id = _lastEventId,
                Event = _event.Length > 0 ? _event : "message",
                Data = _data.ToString(),
                Retry = _retry,
            };
        }
        finally
        {
            _data.Clear();
            _hasData = false;
            _event = "";
        }
    }
}
