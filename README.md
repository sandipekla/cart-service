# Cart service

A minimal Cart Web API for a retail platform, built with ASP.NET Core on .NET 10. The
platform design it belongs to is in [docs/architecture.md](docs/architecture.md); this
repository implements one service of it.

> **Status: work in progress.** The repository is built in small steps and this README only
> describes what exists. So far: the architecture document, the cart's business rules, the
> PostgreSQL schema with its migration, a one-command Docker setup, token authentication and
> reading a cart. Changing a cart (adding, updating and removing items) is not here yet;
> section 11 of the architecture document tracks what is implemented.

The API contract, with examples, is in [docs/api.md](docs/api.md).

## Run

The only prerequisite is Docker with Compose v2 (Docker Desktop, or Docker Engine with the
Compose plugin).

```bash
docker compose up --build
```

This starts three containers:

| Container | What it does |
|---|---|
| `db` | PostgreSQL 17. Its data lives in a named volume |
| `migrate` | Creates the schema and a small demo catalog, then exits |
| `api` | The API, started only after `migrate` has finished successfully |

When the `api` container reports healthy, the API is on <http://localhost:8080>.

### Try it in the browser

Open <http://localhost:8080/scalar>. It lists the API's endpoints and can call them.

1. Under **Development**, run `POST /dev/token` with a body such as `{"userId":"alice"}` and
   copy the `accessToken` from the answer.
2. Paste it into the **Authentication** box at the top of the page (bearer token).
3. Under **Cart**, run `GET /api/v1/cart`.

### Try it from a terminal

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/dev/token \
  -H 'Content-Type: application/json' -d '{"userId":"alice"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

curl -i http://localhost:8080/api/v1/cart -H "Authorization: Bearer $TOKEN"    # 200, the empty cart
curl -i http://localhost:8080/api/v1/cart                                      # 401, no token

curl -i http://localhost:8080/health/live     # 200 Healthy: the process responds
curl -i http://localhost:8080/health/ready    # 200 Healthy: the database answers and is migrated; 503 otherwise
```

In Windows PowerShell use `curl.exe`, because `curl` alone is an alias for another command.

Stopping and resetting:

```bash
docker compose down              # stop; the database keeps its data
docker compose down --volumes    # stop and delete the data, so the next start begins from nothing
```

Things to know:

- Nothing needs to be configured. To change a port or the database password, copy
  `.env.example` to `.env` and edit the copy.
- Both ports are published on `127.0.0.1` only: the API on 8080, PostgreSQL on 5433. If the
  start fails with "port is already allocated" or "address already in use", another program
  holds one of them: set `API_PORT` or `DB_PORT` in `.env`.
- If `migrate` fails with "password authentication failed", the database volume was created
  with a different password. `docker compose down --volumes` removes it.
- The stack runs in the `Development` environment. That is what adds the demo catalog, the
  token endpoint and the documentation page, and the API says so in a warning when it starts.
  The image itself names no environment, so anywhere else it runs as `Production`: no token
  endpoint, no documentation page, and no start without an identity provider configured.
- The database password in `compose.yaml` is a throwaway for a database only this machine can
  reach.
- On the very first start PostgreSQL logs one error about a missing `__EFMigrationsHistory`
  table. That is the migration tool finding out that the database is new.
- The API container runs as an unprivileged user.

Logs are human-readable in the `Development` environment and structured JSON, one object per
line, in every other environment.

## Develop without the API container

Prerequisite: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version
10.0.100 or later. Start only the database from Compose, then run the API from source:

```bash
docker compose up --detach --wait db
dotnet run --project src/Carts.Api -- --migrate
dotnet run --project src/Carts.Api
```

Run this way the API listens on <http://localhost:5080> and uses the connection string in
`appsettings.Development.json`, which points at the Compose database on its default port and
password. If you changed either in `.env`, set `ConnectionStrings__Carts` to match.

## Authentication

- Every cart endpoint needs a bearer token. The cart is found from the token's `sub` claim;
  no address, query or body can name a user or a cart, so one user cannot reach another's.
- In `Development`, `POST /dev/token` issues a token for any user id. Its signing key is made
  up when the API starts and exists only in memory, so there is no development secret in the
  repository, in configuration or in the image. A restart invalidates earlier tokens.
- In every other environment tokens come from an OpenID Connect provider: set
  `Auth__Authority` (an `https` address) and `Auth__Audience`. The API refuses to start
  without them, accepts only RS256 and ES256 signatures, and has no token endpoint.
- Each endpoint declares whether it needs a token. A test fails if one does not say, and
  another lists exactly which endpoints are open: the two health endpoints, plus the token
  endpoint and the documentation in `Development`.
- Every error has one format, problem details (RFC 9457), and never shows a stack trace.
  What is logged while a request is handled carries the user id, and a request that fails
  with an unhandled error gets one more line naming the user. Tokens are never logged.
- The answer to a bad token is a plain `401`: it does not say what was wrong with the token.
- Token validation is fixed in code. A setting in the framework's own
  `Authentication:Schemes:Bearer` section cannot add an issuer, an audience or a signing key,
  or point the API at a plain `http` key address.
- A request body larger than 16 KB is refused with `413`.
- One thing the access tests cannot see: code added as plain middleware, outside the mapped
  endpoints, is not covered by "every endpoint declares whether it needs a token".

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
  Only the `Development` environment has a default, pointing at the Compose database. Outside
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

Prerequisites: the .NET 10 SDK and a running Docker.

```bash
dotnet test
```

- `tests/Carts.UnitTests` covers the cart's rules. It needs no database and no Docker.
- `tests/Carts.IntegrationTests` runs against a real PostgreSQL that the tests start in Docker
  themselves ([Testcontainers](https://dotnet.testcontainers.org/)). Docker must be running.
  They do not use the Compose stack and do not need it.

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

A second CI job starts the Compose stack from nothing and checks that the API becomes ready
and the demo catalog is there.

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
Dockerfile                     the API image: build stage and runtime stage
compose.yaml                   the local stack: database, migrate step, API
.env.example                   the optional settings of the local stack
docs/architecture.md           platform design
docs/api.md                    the API contract, with examples
src/Carts.Domain/              cart rules; no package references
src/Carts.Api/                 ASP.NET Core minimal API
  Features/Carts/              cart endpoints, the shape of their answers, the service behind them
  Auth/                        token validation, the current user, the development token endpoint
  ApiDocs/                     OpenAPI description and the documentation page (Development only)
  Health/                      liveness and readiness endpoints
  Json/                        how amounts are written
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
