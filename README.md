# Cart service

A minimal Cart Web API for a retail platform, built with ASP.NET Core on .NET 10. The
platform design it belongs to is in [docs/architecture.md](docs/architecture.md); this
repository implements one service of it.

> **Status: work in progress.** The repository is built in small steps and this README only
> describes what exists. So far: the architecture document, the cart's business rules, the
> PostgreSQL schema with its migration, health endpoints, tests and the CI workflow. The cart
> endpoints, authentication and a one-command Docker setup are not here yet; section 11 of
> the architecture document tracks what is implemented.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.100 or later
- Docker, for the database and for the integration tests

## Run

Start a local PostgreSQL. The password below is a throwaway for local use and matches
`appsettings.Development.json`:

```bash
docker run -d --name carts-db \
  -e POSTGRES_DB=carts -e POSTGRES_USER=carts -e POSTGRES_PASSWORD=carts-local-only \
  -p 127.0.0.1:5433:5432 postgres:17-alpine
```

Create the schema, then start the API:

```bash
dotnet run --project src/Carts.Api -- --migrate
dotnet run --project src/Carts.Api
```

The API listens on <http://localhost:5080>.

```bash
curl -i http://localhost:5080/health/live     # 200 Healthy: the process responds
curl -i http://localhost:5080/health/ready    # 200 Healthy: the database answers and is migrated; 503 otherwise
```

Remove the database when done: `docker rm -f carts-db`.

Logs are human-readable in the `Development` environment and structured JSON, one object per
line, in every other environment.

## Database

- Schema changes are EF Core migrations in `src/Carts.Api/Persistence/Migrations`.
- `--migrate` applies them and exits, with code 0 on success and 1 on any failure, bad
  configuration included. It is a separate step on purpose: the API never changes the schema
  while starting up or serving requests. In production the same command runs as a controlled
  step before a rollout, ideally with a database role of its own; the API's role then needs
  no right to change the schema.
- In the `Development` environment `--migrate` also inserts a small demo catalog when the
  `products` table is empty. Any other environment gets the schema and nothing else.
- The database repeats the cart's rules as constraints: one cart per user, quantity 1 to 99,
  no negative price, a three-letter currency code, and lines deleted with their cart. The
  `products` table also refuses a malformed SKU or an empty name.
- Ready means the database answers and the newest migration of this build is applied. The
  check allows two seconds to connect and two to answer, and uses at most two database
  connections of its own, however often the endpoint is called. Its answer is only `Healthy`
  or `Unhealthy`; the reason goes to the log. Both health endpoints answer `GET` only.
- The connection string is `ConnectionStrings__Carts`. The API refuses to start without it.
  Only the `Development` environment has a default, pointing at the container above. Outside
  a local machine it should demand an encrypted, verified connection (`SSL Mode=VerifyFull`).
- A failed SQL statement is not logged a second time by the database layer: the error that
  results from it is logged where it is handled.

To add a migration after changing the model:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/Carts.Api --output-dir Persistence/Migrations
```

A test fails when the model and the migrations disagree.

## Test

```bash
dotnet test
```

- `tests/Carts.UnitTests` covers the cart's rules. It needs no database and no Docker.
- `tests/Carts.IntegrationTests` runs against a real PostgreSQL that the tests start in Docker
  themselves ([Testcontainers](https://dotnet.testcontainers.org/)). Docker must be running.

With Docker Desktop nothing else is needed. With Colima, point Testcontainers at its socket:

```bash
export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock"
export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock
```

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
docs/architecture.md           platform design
src/Carts.Domain/              cart rules; no package references
src/Carts.Api/                 ASP.NET Core minimal API
  Persistence/                 database context, table mappings, migrations, setup step
  Products/                    the service's own copy of product data
tests/Carts.UnitTests/         unit tests for the cart rules
tests/Carts.IntegrationTests/  tests against a real PostgreSQL
.github/workflows/             CI
Directory.Build.props          compiler settings shared by every project
Directory.Packages.props       package versions, in one place
dotnet-tools.json              the EF Core command-line tool, pinned
nuget.config                   packages come from nuget.org only
global.json                    requires a .NET 10 SDK; selects the test runner
```
