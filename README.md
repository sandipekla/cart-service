# Cart service

A minimal Cart Web API for a retail platform, built with ASP.NET Core on .NET 10.

> **Status: work in progress.** The repository is built in small steps and this README only
> describes what exists. So far: the solution skeleton with a liveness endpoint and the CI
> workflow. The cart endpoints and the PostgreSQL database are not here yet.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.100 or later

## Run

```bash
dotnet run --project src/Carts.Api
```

The API listens on <http://localhost:5080>.

```bash
curl -i http://localhost:5080/health/live    # 200 Healthy
```

Logs are human-readable in the `Development` environment and structured JSON, one object per
line, in every other environment.

## Build checks

CI (`.github/workflows/ci.yml`) runs these on every push to `main` and on every pull request:

```bash
dotnet restore
dotnet build --no-restore --configuration Release   # warnings are errors
dotnet format --no-restore --verify-no-changes      # formatting and style follow .editorconfig
```

## Layout

```text
src/Carts.Api/           ASP.NET Core minimal API
.github/workflows/       CI
Directory.Build.props    compiler settings shared by every project
global.json              requires a .NET 10 SDK
```
