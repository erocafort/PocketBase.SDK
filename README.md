# PocketBase.SDK

[![Publish NuGet](https://github.com/erocafort/PocketBase.SDK/actions/workflows/publish-nuget.yml/badge.svg)](https://github.com/erocafort/PocketBase.SDK/actions/workflows/publish-nuget.yml)
[![NuGet PocketBase.SDK](https://img.shields.io/nuget/v/PocketBase.SDK.svg)](https://www.nuget.org/packages/PocketBase.SDK)

PocketBase.SDK is a .NET SDK for [PocketBase](https://pocketbase.io/) focused on API parity with the official JS and Dart SDKs.

## Packages

- `PocketBase.SDK` - core client and services
- `PocketBase.SDK.Abstractions` - interfaces, DTOs and option types
- `PocketBase.SDK.DependencyInjection` - DI registration helpers

## Targets

- .NET 8
- .NET 9
- .NET 10
- .NET 11

## Installation

```bash
dotnet add package PocketBase.SDK
# Optional: dependency injection helpers
dotnet add package PocketBase.SDK.DependencyInjection
```

## Usage

### Create a client

```csharp
using PocketBase.SDK;
using PocketBase.SDK.Abstractions;

var pb = PocketBaseClient.Create(new PocketBaseOptions
{
    BaseUrl = "http://127.0.0.1:8090"
});
```

### Dependency injection (ASP.NET Core / Generic Host)

```csharp
using PocketBase.SDK.Abstractions;
using PocketBase.SDK.DependencyInjection;

builder.Services.AddPocketBase(options =>
{
    options.BaseUrl = "http://127.0.0.1:8090";
});

// Inject IPocketBaseClient and use it to reach the services (Collection, Files, Sql, ...).
public class PostsController(IPocketBaseClient pb)
{
    public Task<ResultList<RecordModel>> ListAsync() => pb.Collection("posts").GetListAsync();
}
```

`AddPocketBase` returns the `IHttpClientBuilder` of the underlying client, so you can add resilience handlers or other delegating handlers. The auth store is scoped by default; pass `authStoreLifetime` to change it (transient is not allowed).

### Authentication

```csharp
// Superusers (admin) live in the _superusers collection.
var admin = await pb.Collection("_superusers")
    .AuthWithPasswordAsync("admin@example.com", "password");

// Regular auth collection.
var user = await pb.Collection("users")
    .AuthWithPasswordAsync("jane", "secret");

// The token is stored in pb.AuthStore and sent automatically on later requests.
Console.WriteLine(pb.AuthStore.IsValid);
Console.WriteLine(pb.AuthStore.Token);

// Refresh the session (and optionally the auth record).
await pb.Collection("users").AuthRefreshAsync();

pb.AuthStore.Clear();
```

To persist the session across app restarts, use `AsyncAuthStore` with your own save/clear callbacks, or `ExportToCookie` / `LoadFromCookie` for cookie-based sessions.

### Records

```csharp
// List with filter, sort and expand (page is 1-based).
var page = await pb.Collection("posts").GetListAsync(1, 20, new RecordListOptions
{
    Filter = "published = true && created >= '2026-01-01'",
    Sort = "-created",
    Expand = "author"
});

// Typed results: map records to your own class.
public sealed class Post
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}

var typed = await pb.Collection("posts").GetListAsync<Post>(1, 20);

var one = await pb.Collection("posts").GetOneAsync("RECORD_ID");
var first = await pb.Collection("posts").GetFirstListItemAsync("slug = 'hello'");

var created = await pb.Collection("posts").CreateAsync(new { title = "Hello", published = true });
await pb.Collection("posts").UpdateAsync(created.Id, new { title = "Hello again" });
await pb.Collection("posts").DeleteAsync(created.Id);
```

### Realtime

```csharp
// Subscribe to one record ("RECORD_ID") or to all records ("*") of a collection.
var unsubscribe = await pb.Collection("posts").SubscribeAsync("*", e =>
{
    Console.WriteLine($"{e.Action}: {e.Record.Id}");
});

// ... later
await unsubscribe();
```

Subscriptions use Server-Sent Events. The SDK reconnects automatically and re-subscribes the active topics.

### Files

```csharp
var record = await pb.Collection("posts").GetOneAsync("RECORD_ID");
string url = pb.Files.GetURL(record, record.Get<string>("cover"), new FileOptions { Thumb = "100x100" });
```

### Batch requests

Batch requests must be enabled in the PocketBase settings.

```csharp
var batch = pb.CreateBatch();
batch.Collection("posts").Create(new { title = "a" });
batch.Collection("posts").Update("RECORD_ID", new { title = "b" });
batch.Collection("posts").Delete("OTHER_RECORD_ID");

var results = await batch.SendAsync();
```

### Errors

API failures throw `PocketBaseException`, which exposes the HTTP `Status` and the PocketBase `Response` payload. Network errors surface as the usual .NET exceptions.

```csharp
try
{
    await pb.Collection("posts").CreateAsync(new { title = "" });
}
catch (PocketBaseException ex) when (ex.Status == 400)
{
    Console.WriteLine(ex.Message);
}
```

### Admin APIs

Collections, settings, logs, backups, crons, SQL and health are available from the client:

```csharp
var collections = await pb.Collections.GetFullListAsync();
var health = await pb.Health.CheckAsync();
```

## Contributing

Contributions are welcome. The goal of this SDK is to stay close to the official PocketBase JS and Dart SDKs, so please keep parity in mind when proposing changes.

### Requirements

- A .NET SDK that can build the solution (targets net8.0 to net11.0). Running the tests for a target requires the matching runtime (for example, `-f net10.0` needs .NET 10).

### Build and test

```bash
dotnet build PocketBase.SDK.slnx
dotnet test PocketBase.SDK.Tests -f net10.0
```

The unit tests do not need a running PocketBase instance.

### Live integration tests (optional)

`LivePocketBaseIntegrationTests` runs against a real PocketBase server. It does nothing unless these variables are set:

| Variable | Example |
|---|---|
| `PB_URL` | `http://localhost:8090/` |
| `PB_EMAIL` | superuser email |
| `PB_PASSWORD` | superuser password |

```bash
dotnet test PocketBase.SDK.Tests -f net10.0 --filter "FullyQualifiedName~LivePocketBaseIntegrationTests"
```

Use a disposable local instance: the test creates and deletes collections.

### Guidelines

- Follow the official JS/Dart SDK behaviour (routes, HTTP methods, body keys, query parameters). If you deviate, explain why in the pull request and in `docs/ANALISIS.md`.
- Keep the public API in `PocketBase.SDK.Abstractions` and the implementation in `PocketBase.SDK`.
- Add or update unit tests for every behaviour change.
- Keep the three packable projects at the same version and bump it before tagging a release (NuGet does not allow reusing a version).
- Project decisions, coverage and status are tracked in `docs/ANALISIS.md` (in Spanish). Update it when you finish a phase or change a decision.

### Pull requests

1. Fork the repository and create a branch from `main`.
2. Make a focused change with tests.
3. Run the build and the unit tests.
4. Open a pull request describing the change and, if relevant, the PocketBase version you tested against.

## Publishing to NuGet.org (Trusted Publishing)

This repository uses **NuGet Trusted Publishing (OIDC)** from GitHub Actions (no long-lived API key).

1. In NuGet.org, create a Trusted Publishing policy:
   - Repository Owner: `erocafort`
   - Repository: `PocketBase.SDK`
   - Workflow File: `publish-nuget.yml`
2. In GitHub repo settings, add repository variable:
   - `NUGET_PUBLISH_USER`: your NuGet.org profile name (not email)
3. Create/push a tag `v*` (for example `v1.0.3`) to trigger publish.

For detailed analysis/status of implemented phases, see `docs/ANALISIS.md`.

## License

MIT. See [LICENSE](LICENSE).
