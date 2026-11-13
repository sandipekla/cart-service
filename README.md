# Cart service

A minimal Cart Web API for a retail platform, built with ASP.NET Core on .NET 10. The
platform design it belongs to is in [docs/architecture.md](docs/architecture.md); this
repository implements one service of it.

> **Status: work in progress.** The repository is built in small steps and this README only
> describes what exists. So far: the architecture document, the solution skeleton with a
> liveness endpoint, the cart's business rules with their unit tests, and the CI workflow.
> The cart endpoints and the PostgreSQL database are not here yet; section 11 of the
> architecture document tracks what is implemented.

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

## Test

```bash
dotnet test
```

The unit tests cover the cart's rules. They need no database and no Docker.

## Cart rules

The rules live in `src/Carts.Domain`, a project with no dependencies, so they can be read and
tested without a database or a web server.

- Setting an item sets its quantity to the given value; it never adds to the current one.
  Setting the same values again changes nothing.
- Quantity per line: 1 to 99. Distinct lines per cart: at most 50.
- One currency per cart, fixed by the first line and released when the last line is removed.
- An inactive product cannot be added or changed; it can always be removed.
- A line keeps the product's name and price as they were when the line was last set.
- Product data is validated before it can reach a cart: a well-formed SKU (1 to 64 letters,
  digits, dots, underscores or hyphens, starting with a letter or digit), a name of at most
  200 characters, a price that is not negative and has at most four decimal places, and a
  three-letter uppercase currency code.
- Every change that alters the cart increases its version by one. A refused or repeated
  command leaves the cart exactly as it was.

## Build checks

CI (`.github/workflows/ci.yml`) runs these on every push to `main` and on every pull request:

```bash
dotnet restore
dotnet build --no-restore --configuration Release
dotnet format --no-restore --verify-no-changes      # formatting and style follow .editorconfig
dotnet test --no-build --configuration Release
```

In CI warnings are errors, including NuGet's warning about a package with a known
vulnerability. The one exception is being unable to reach the advisory database, which only
warns. On any other machine warnings stay warnings, so a newer SDK or a newly published
advisory cannot break a fresh clone. To apply the CI rule locally, set `CI=true`:

```bash
CI=true dotnet build --no-incremental                  # bash, zsh
$env:CI = 'true'; dotnet build --no-incremental        # PowerShell
```

## Layout

```text
docs/architecture.md       platform design
src/Carts.Domain/          cart rules; no package references
src/Carts.Api/             ASP.NET Core minimal API
tests/Carts.UnitTests/     unit tests for the cart rules
.github/workflows/         CI
Directory.Build.props      compiler settings shared by every project
Directory.Packages.props   package versions, in one place
nuget.config               packages come from nuget.org only
global.json                requires a .NET 10 SDK; selects the test runner
```
