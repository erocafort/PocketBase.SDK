# PocketBaseNet

PocketBaseNet is a .NET SDK for [PocketBase](https://pocketbase.io/) focused on API parity with the official JS and Dart SDKs.

## Packages

- `PocketBaseNet` - core client and services
- `PocketBaseNet.Abstractions` - interfaces, DTOs and option types
- `PocketBaseNet.DependencyInjection` - DI registration helpers

## Targets

- .NET 8
- .NET 9
- .NET 10
- .NET 11

## Quick start

```csharp
using PocketBaseNet;

var pb = PocketBaseClient.Create(new PocketBaseNet.Abstractions.PocketBaseOptions
{
    BaseUrl = "http://127.0.0.1:8090"
});

var auth = await pb.Collection("_superusers")
    .AuthWithPasswordAsync("admin@example.com", "password");

var posts = await pb.Collection("posts").GetListAsync();
```

For detailed analysis/status of implemented phases, see `docs/ANALISIS.md`.
