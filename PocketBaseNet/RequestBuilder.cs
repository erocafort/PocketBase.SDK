using PocketBaseNet.Abstractions;

namespace PocketBaseNet;

/// <summary>Converts the public option types into <see cref="SendOptions"/>.</summary>
internal static class RequestBuilder
{
    /// <summary>
    /// Builds the request options. Query precedence (low to high): <paramref name="defaultQuery"/>,
    /// <see cref="CommonOptions.Query"/>, typed option properties (same as the JS SDK, where the
    /// top-level options are moved into the query, overwriting it).
    /// Body precedence (low to high): <see cref="CommonOptions.Body"/>, <paramref name="body"/> (explicit method arguments, same as the Dart SDK).
    /// </summary>
    public static SendOptions Build(
        HttpMethod method,
        CommonOptions? options,
        IDictionary<string, object?>? body = null,
        IDictionary<string, object?>? defaultQuery = null)
    {
        var send = new SendOptions { Method = method };

        if (defaultQuery is not null)
        {
            foreach (var (k, v) in defaultQuery) send.Query[k] = v;
        }

        if (options is not null)
        {
            foreach (var (k, v) in options.Query) send.Query[k] = v;
            foreach (var (k, v) in options.Headers) send.Headers[k] = v;
            ApplyTypedQuery(send.Query, options);
        }

        if (body is not null)
        {
            var merged = new Dictionary<string, object?>();
            if (options is not null)
            {
                foreach (var (k, v) in options.Body) merged[k] = v;
            }
            foreach (var (k, v) in body) merged[k] = v;
            send.Body = merged;
        }

        return send;
    }

    public static void ApplyTypedQuery(IDictionary<string, object?> query, CommonOptions options)
    {
        Set(query, "fields", options.Fields);

        switch (options)
        {
            case RecordOptions r:
                Set(query, "expand", r.Expand);
                break;
            case ListOptions l:
                Set(query, "filter", l.Filter);
                Set(query, "sort", l.Sort);
                Set(query, "skipTotal", l.SkipTotal);
                if (l is RecordListOptions rl) Set(query, "expand", rl.Expand);
                break;
            case LogStatsOptions ls:
                Set(query, "filter", ls.Filter);
                break;
        }
    }

    private static void Set(IDictionary<string, object?> query, string key, object? value)
    {
        if (value is not null) query[key] = value;
    }

    /// <summary>Shallow copy of list options (the "batch" option is not part of the query).</summary>
    public static RecordListOptions ToListOptions(RecordFullListOptions? options)
    {
        var copy = new RecordListOptions
        {
            Fields = options?.Fields,
            Expand = options?.Expand,
            Filter = options?.Filter,
            Sort = options?.Sort,
            SkipTotal = options?.SkipTotal,
        };

        if (options is not null)
        {
            foreach (var (k, v) in options.Query) copy.Query[k] = v;
            foreach (var (k, v) in options.Headers) copy.Headers[k] = v;
        }

        return copy;
    }
}
