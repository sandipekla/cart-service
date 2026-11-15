# Cart service

A minimal Cart Web API for a retail platform, built with ASP.NET Core on .NET 10. The
platform design it belongs to is in [docs/architecture.md](docs/architecture.md); this
repository implements one service of it.

What is here: the four cart endpoints with validation, token authentication, safe concurrent
updates, PostgreSQL with migrations, tests against a real database, a one-command Docker
setup and CI. The rest of the platform is designed in the architecture document and not
built; see [Not built](#not-built).

The API contract, with examples, is in [docs/api.md](docs/api.md).

## Run

The only prerequisite is Docker with Compose v2 (Docker Desktop, or Docker Engine with the
Compose plugin).

```bash
docker compose up --build
```

The command stays in the foreground and shows the logs, so run the commands further down in
a second terminal. `docker compose up --build --wait` starts the same stack in the
background and returns once the API is healthy.

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
3. Under **Cart**, run `PUT /api/v1/cart/items/{sku}` with `sku` set to `TSHIRT-BLK-M` and
   the body `{"quantity":2}`, then `GET /api/v1/cart`.

The demo catalog has eight products. Two of them are there to try the rules:

| SKU | What happens |
|---|---|
| `TSHIRT-BLK-M`, `MUG-WHT`, `HOODIE-GRY-M`, ... | Ordinary products priced in EUR |
| `POSTER-2019` | No longer sold: `422 product_unavailable` |
| `GIFTCARD-USD-25` | Priced in USD: `422 currency_mismatch` once the cart holds a EUR product |
| any other SKU | `404 product_not_found` |

### Try it from a terminal

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/dev/token \
  -H 'Content-Type: application/json' -d '{"userId":"alice"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

CART=http://localhost:8080/api/v1/cart
AUTH="Authorization: Bearer $TOKEN"
JSON='Content-Type: application/json'

curl -i $CART -H "$AUTH"
curl -i -X PUT $CART/items/TSHIRT-BLK-M -H "$AUTH" -H "$JSON" -d '{"quantity":2}'
curl -i -X PUT $CART/items/MUG-WHT -H "$AUTH" -H "$JSON" -d '{"quantity":1}'
curl -i -X PUT $CART/items/POSTER-2019 -H "$AUTH" -H "$JSON" -d '{"quantity":1}'
curl -i -X PUT $CART/items/MUG-WHT -H "$AUTH" -H "$JSON" -d '{"quantity":0}'
curl -i -X DELETE $CART/items/MUG-WHT -H "$AUTH"
curl -i -X DELETE $CART -H "$AUTH"
curl -i $CART

curl -i http://localhost:8080/health/live
curl -i http://localhost:8080/health/ready
```

The answers, in order:

| Request | Answer |
|---|---|
| `GET` the cart | `200`, the empty cart |
| `PUT` `TSHIRT-BLK-M`, quantity 2 | `200`, one line |
| `PUT` `MUG-WHT`, quantity 1 | `200`, two lines |
| `PUT` `POSTER-2019`, quantity 1 | `422`, no longer sold |
| `PUT` `MUG-WHT`, quantity 0 | `400`, quantity out of range |
| `DELETE` `MUG-WHT` | `200`, one line again |
| `DELETE` the cart | `200`, empty |
| `GET` the cart without a token | `401` |
| `GET /health/live` | `200 Healthy`: the process responds |
| `GET /health/ready` | `200 Healthy`: the database answers and is migrated; `503` otherwise |

The block is written for bash and zsh. On Windows use Git Bash or WSL, or the browser page
above.

Stopping and resetting:

- `docker compose down` stops the stack; the database keeps its data.
- `docker compose down --volumes` also deletes the data, so the next start begins from
  nothing.

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
  up when the API starts and exists only in memory, so there is no signing key in the
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
- A failed SQL statement or a failed save is not logged a second time by the database layer:
  the error that results from it is logged where it is handled.

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

## API

Four endpoints, all under `/api/v1/cart`, all answering with the cart as it now is. The full
contract, with examples and every error, is in [docs/api.md](docs/api.md).

| Method | Address | Does |
|---|---|---|
| `GET` | `/api/v1/cart` | Returns the caller's cart |
| `PUT` | `/api/v1/cart/items/{sku}` | Sets the quantity of a product (it replaces, it never adds) |
| `DELETE` | `/api/v1/cart/items/{sku}` | Removes a product |
| `DELETE` | `/api/v1/cart` | Removes every product |

- Requests are validated before anything else happens: a malformed SKU or a quantity outside
  1 to 99 is a `400` that names the field, and the database is never asked.
- A broken cart rule is a `404` or `422` with a stable `code` a client can switch on.
- A request that is refused, or that changes nothing, writes nothing.
- Every change is logged with the cart id, its new version and the user id.
- Parallel changes to one cart are applied one after the other; see the next section.

## Concurrency

The problem: one user changes one cart from two places at the same moment. Two browser tabs,
a double click, a mobile client that sends a request again because the first answer was slow.
Without protection both requests read the same cart and both write, and the slower one fails
or overwrites the other.

### What the API does

Every change to a cart runs in one short database transaction, and the first thing that
transaction does is take a lock that belongs to the user:

```sql
BEGIN;
SELECT set_config('lock_timeout', '2000ms', true);             -- for this transaction only
SELECT pg_advisory_xact_lock(hashtextextended(@user_id, 0));   -- wait for this user's lock
-- read the cart, apply the change, store the result
COMMIT;                                                        -- releases the lock
```

- Changes to one cart run one after the other, and each starts from what the one before it
  stored. No client ever has to resolve a conflict: twenty parallel requests for twenty
  products all answer `200`, and the cart ends with twenty lines and version 20.
- The lock lives in PostgreSQL, not in the process, so it holds across any number of API
  instances.
- It is an advisory lock on a hash of the user id. It locks no row, so it also works for a
  user who has no cart yet, which is exactly when two first requests would otherwise both
  try to create one.
- It is scoped to the transaction: it is released when the transaction ends, however it
  ends, and it works behind a connection pooler in transaction mode.
- Reading takes no lock. `GET` is one statement, so it sees the cart before a change or
  after it, never half of it.
- Different users do not wait for each other. Two user ids with the same hash would, for a
  few milliseconds, which is harmless.
- A product that does not exist is answered before the lock is taken.

A change that waits longer than `Cart__LockTimeoutMs` (default `2000`, allowed 1 to 20000)
gives up with `409 cart_busy` and `Retry-After: 1`. Nothing was changed, and sending the
request again is safe. The timeout covers every lock the change waits for: the cart lock,
and a row or a table that something else holds, such as a schema change in progress.

### Set, not add

`PUT /api/v1/cart/items/{sku}` carries the quantity the line should have, not an amount to
add. "Set to 3" sent twice leaves 3; "add 3" sent twice would leave 6. That is what makes a
retry or a double click harmless. When two parallel requests set the same line to different
quantities, both succeed and the one applied last stands.

### The safety net

The lock is the mechanism; the database still checks. A cart's `version` is checked on every
update, and unique keys allow one cart per user and one line per product. While every write
takes the lock none of these can fire. If one does, some write path changed a cart without
the lock: the request is refused with `409 concurrency_conflict`, nothing is overwritten, and
the API logs it as an error, because it is a bug and not a busy moment.

### The alternative not taken

Optimistic concurrency alone: no lock, check the version when writing, and answer `409` (or
retry on the server) when it changed. It turns parallel changes to different lines of one
cart into errors every client has to handle, and a server-side retry loop ends up doing the
same one-after-the-other work with more round trips. It also needs separate handling for two
first requests creating the same cart. A cart change is small and the server always knows
how to apply it, so waiting a few milliseconds is the simpler contract.

Conditional requests (`ETag` and `If-Match`), for a client that wants to refuse to overwrite
a change it has not seen, solve a different problem and are not built.

### Known limits

- A request that waits for the lock holds a database connection while it waits, up to the
  timeout. While one user's lock is stuck, a burst of a hundred or more changes for that
  user can occupy all of an instance's connection pool, which delays or fails every other
  request on that instance, reads included. The fix is a rate limit per user, which is not
  built.
- The timeout bounds the wait for locks, not the whole request: under heavy load, waiting
  for a free database connection comes on top.
- Changes are ordered by when they reach the database, not by when the client sent them.
- `updatedAt` comes from the clock of the instance that applied the change. `version` is the
  reliable order of changes.

### How it is tested

`tests/Carts.IntegrationTests/CartConcurrencyTests.cs` runs against a real PostgreSQL. Its
requests are truly parallel: every one is started before any is awaited, and all are released
by one signal.

- Twenty parallel first requests of a new user, for twenty products: all `200`, exactly one
  cart, twenty lines, versions 1 to 20.
- The same request ten times at once: one change, version 1.
- Parallel sets of one line to 2 and to 5, for ten users at once: one line, the later value.
- Parallel sets, removes and a clear: all `200`, and the stored cart is the one the last
  answer showed.
- The lock held by another connection past the timeout: `409 cart_busy` with `Retry-After`
  on all three changing routes, and the cart unchanged. The same answer when it is the
  cart's row or the whole table that is locked.
- A request waits for the lock and succeeds once it is free. Reading, and another user's
  change, do not wait.
- A writer that ignores the lock, changing or creating the cart in the middle of a request:
  `409 concurrency_conflict`, nothing overwritten, one error in the log.

With the one call that takes the lock removed, all four parallel tests above fail.

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

## Not built

This repository is deliberately one small service. The following are not in the code. Most
are designed in [docs/architecture.md](docs/architecture.md) or named there as later work;
the rest are deliberate omissions:

- The other services of the platform (catalog, orders), the gateway, and what connects them:
  Kafka, the outbox and `cart.changed` events, Redis, the search engine. This service
  publishes no events. Its `products` table stands in for the copy of product data that the
  catalog's events would keep up to date; here only the demo catalog fills it.
- Checkout, payments and stock. A cart holds what the user wants; it reserves nothing.
- Guest carts, merging a guest cart at login, and cart expiry.
- Conditional requests (`ETag`, `If-Match`); see [Concurrency](#concurrency).
- Rate limiting, traces and metrics. Logs and the two health endpoints are what there is.
- Kubernetes manifests, or any deployment beyond the Compose stack.
- An identity provider. Outside `Development` the API validates tokens from the one it is
  configured with; it does not ship one.

Section 11 of the architecture document lists, line by line, what is implemented and what is
proposed.

## Build checks

CI (`.github/workflows/ci.yml`) runs these on every push to `main` and on every pull request:

```bash
dotnet restore
dotnet build --no-restore --configuration Release
dotnet format --no-restore --verify-no-changes
dotnet test --no-build --configuration Release
```

The format step checks formatting and style against `.editorconfig`.

A second CI job starts the Compose stack from nothing and uses it the way a person would: it
checks that the API becomes ready and the demo catalog is there, gets a token, puts items in a
cart and removes one, checks that an oversized body is refused, then stops the stack, starts
it again and checks that both the database and the cart are still there.

In CI warnings are errors, including NuGet's warning about a package with a known
vulnerability. The one exception is being unable to reach the advisory database, which only
warns. On any other machine warnings stay warnings, so a newer SDK or a newly published
advisory cannot break a fresh clone. To apply the CI rule locally, set `CI=true`:

```bash
CI=true dotnet build --no-incremental
```

In PowerShell: `$env:CI = 'true'; dotnet build --no-incremental`.

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
Carts.slnx                     the solution: all four projects
.editorconfig                  formatting and style rules
Directory.Build.props          compiler settings shared by every project
Directory.Packages.props       package versions, in one place
dotnet-tools.json              the EF Core command-line tool, pinned
nuget.config                   packages come from nuget.org only
global.json                    requires a .NET 10 SDK; selects the test runner
```
