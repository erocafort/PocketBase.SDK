using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PocketBaseNet.Abstractions;

public sealed class CollectionField
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("system")] public bool System { get; set; }
    [JsonPropertyName("required")] public bool Required { get; set; }
    [JsonPropertyName("presentable")] public bool Presentable { get; set; }
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class CollectionModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "base";
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("system")] public bool System { get; set; }
    [JsonPropertyName("listRule")] public string? ListRule { get; set; }
    [JsonPropertyName("viewRule")] public string? ViewRule { get; set; }
    [JsonPropertyName("createRule")] public string? CreateRule { get; set; }
    [JsonPropertyName("updateRule")] public string? UpdateRule { get; set; }
    [JsonPropertyName("deleteRule")] public string? DeleteRule { get; set; }
    [JsonPropertyName("fields")] public List<CollectionField> Fields { get; set; } = [];
    [JsonPropertyName("indexes")] public List<string> Indexes { get; set; } = [];
    [JsonPropertyName("options")] public JsonObject? Options { get; set; }
}

public sealed class ConfigurableOAuth2Provider
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("logo")] public string Logo { get; set; } = "";
}

public sealed class LogModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public JsonObject Data { get; set; } = [];
}

public sealed class LogStat
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("date")] public string Date { get; set; } = "";
}

public sealed class BackupFileInfo
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("modified")] public string Modified { get; set; } = "";
}

public sealed class CronJob
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("expression")] public string Expression { get; set; } = "";
}

public sealed class SqlColumn
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("nullable")] public bool Nullable { get; set; }
}

public sealed class SqlResult
{
    [JsonPropertyName("execTime")] public double ExecTime { get; set; }
    [JsonPropertyName("affectedRows")] public long AffectedRows { get; set; }
    [JsonPropertyName("columns")] public List<SqlColumn> Columns { get; set; } = [];
    [JsonPropertyName("rows")] public List<List<string?>> Rows { get; set; } = [];
}

public sealed class HealthCheck
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public JsonObject Data { get; set; } = [];
}

public sealed class AppleClientSecret
{
    [JsonPropertyName("secret")] public string Secret { get; set; } = "";
}
