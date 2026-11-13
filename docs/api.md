# Cart API

The contract of the API as it stands. It grows with the implementation; the README says what
exists. Every example below is the output of a real call against the Docker Compose stack.

- Base address when run with Docker Compose: `http://localhost:8080`
- Interactive version of this document, in the `Development` environment only:
  <http://localhost:8080/scalar>, backed by the OpenAPI description at `/openapi/v1.json`
- All request and response bodies are JSON

## Authentication

Every cart endpoint needs a bearer token:

```http
Authorization: Bearer <token>
```

The token's `sub` claim is the user id, and the cart belongs to that user. There is no cart id
and no user id in any address, query or body, so a caller cannot ask for someone else's cart.

| Environment | Where tokens come from | Accepted signatures |
|---|---|---|
| `Development` | `POST /dev/token` of this API | HS256, with a key that exists only in the memory of the running process |
| Any other | An OpenID Connect provider, set with `Auth__Authority` and `Auth__Audience` | RS256, ES256 |

A token is refused with `401` when it is missing, malformed, expired (30 seconds of clock
difference are tolerated), without an expiry, unsigned, signed with another key, issued by
another issuer, meant for another audience, or when its `sub` claim is missing, blank, longer
than 255 characters or contains a control character. The `401` never says which of these it
was.

Outside `Development` the API does not start unless `Auth__Authority` is an `https` address
and `Auth__Audience` is set, and `POST /dev/token` does not exist (it answers `404`).

### POST /dev/token

`Development` only. Issues a token for any user id you choose, valid for one hour. Tokens stop
working when the API restarts, because the signing key is made up at startup.

```bash
curl -X POST http://localhost:8080/dev/token \
  -H 'Content-Type: application/json' \
  -d '{"userId":"alice"}'
```

```json
{"accessToken":"<the token>","tokenType":"Bearer","expiresIn":3600}
```

`userId` is required: 1 to 64 letters, digits, dots, underscores or hyphens. Anything else is
a `400` with an `errors.userId` entry. The answer carries `Cache-Control: no-store`, because it
is a credential.

## Cart

### GET /api/v1/cart

The caller's cart. Reading never creates a cart: a user who has none gets the empty cart.

```bash
curl http://localhost:8080/api/v1/cart -H "Authorization: Bearer $TOKEN"
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store

{"id":null,"version":0,"currency":null,"items":[],"total":0,"updatedAt":null}
```

| Field | Meaning |
|---|---|
| `id` | The cart's id, or `null` for a user who has no cart |
| `version` | Increases by one with every change to the cart; `0` when there is no cart |
| `currency` | Three-letter currency code shared by every line; `null` while the cart has no lines |
| `items` | The lines, in the order they were added: `sku`, `name`, `unitPrice`, `quantity`, `lineTotal` |
| `total` | The sum of the line totals |
| `updatedAt` | When the cart last changed, in UTC; `null` when there is no cart |

Amounts are JSON numbers without padding zeros (`19.99`, never `19.9900`). Read them into a
decimal type and format them for the currency on the client. A line's `name` and `unitPrice`
are the values from when the line was last set, not live catalog values.

Cart responses carry `Cache-Control: no-store`: a cart is one person's data.

## Errors

Every response that is not a success is a problem document
([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)) with the media type
`application/problem+json`. That includes errors raised by the framework itself. No error ever
carries a stack trace or an internal message. The one exception to the format: a client that
says it accepts only something other than JSON (for example `Accept: text/html`) gets the
same status with a short plain-text body.

```http
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json
WWW-Authenticate: Bearer

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Unauthorized","status":401,"traceId":"00-15d44312655bec2d3987d55334a699df-288321a05710720e-00"}
```

| Status | When |
|---|---|
| `400` | The request body is missing, is not JSON, or is not valid. Validation failures list the fields under `errors` |
| `401` | No valid token |
| `404` | No such address |
| `405` | The address exists but not for this method; the `Allow` header lists the methods it has |
| `413` | The request body is larger than 16 KB |
| `415` | The request body is not sent as `application/json` |
| `500` | The API failed. The body says nothing about the cause; the log does, under the `traceId` |

`traceId` identifies the request in the logs.

## Health

Not part of the cart contract, and open without a token. Both answer `GET` only, with a plain
text body of `Healthy` or `Unhealthy`.

| Endpoint | 200 means | Otherwise |
|---|---|---|
| `GET /health/live` | The process responds | The process is restarted by whatever runs it |
| `GET /health/ready` | The database answers and is migrated | `503`: traffic should go elsewhere until it recovers |
