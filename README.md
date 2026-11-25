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

## Quick start

```csharp
using PocketBase.SDK;

var pb = PocketBaseClient.Create(new PocketBase.SDK.Abstractions.PocketBaseOptions
{
    BaseUrl = "http://127.0.0.1:8090"
});

var auth = await pb.Collection("_superusers")
    .AuthWithPasswordAsync("admin@example.com", "password");

var posts = await pb.Collection("posts").GetListAsync();
```

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
