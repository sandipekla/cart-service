# Cart API

The contract of the API. Every example below is the output of a real call against the Docker
Compose stack, with its demo catalog.

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

Four endpoints. Every one answers `200` with the cart as it now is, so a client never needs a
second request to see the result of a change.

| Method | Address | Body | Does |
|---|---|---|---|
| `GET` | `/api/v1/cart` | none | Returns the caller's cart |
| `PUT` | `/api/v1/cart/items/{sku}` | `{"quantity": 2}` | Sets the quantity of a product |
| `DELETE` | `/api/v1/cart/items/{sku}` | none | Removes a product |
| `DELETE` | `/api/v1/cart` | none | Removes every product |

All four are safe to repeat: sending the same request again leaves the cart exactly as the
first one left it.

### Parallel requests

Changes to one cart are applied one after the other, in the order they reach the database,
even when they are sent at the same moment and handled by different instances of the API.
Each starts from what the one before it stored, so parallel changes do not fail and do not
overwrite each other: twenty parallel `PUT`s for twenty products all answer `200` and leave
twenty lines. When two parallel `PUT`s set the same product to different quantities, both
answer `200` and the one applied last stands.

A change waits for the one before it for at most two seconds (the `Cart__LockTimeoutMs`
setting). After that it answers `409 cart_busy` with a `Retry-After` header. Nothing was
changed, and because every request is safe to repeat, the client can simply send it again.
Reading a cart never waits for a change. Two seconds is the limit of that wait, not of the
whole request: on a heavily loaded server the answer can take longer.

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json
Cache-Control: no-store
Retry-After: 1

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Cart busy","status":409,"detail":"Another change to this cart is still in progress. Nothing was changed; try again.","code":"cart_busy","traceId":"00-2e05f82c20e0c46f46115001f416c591-3ccae227515d1226-00"}
```

Conditional request headers (`If-Match`, `If-None-Match`, `If-Unmodified-Since`) are not
supported: the API ignores them and applies the request. The `version` in each answer is
there for a client to notice that a cart changed, not to guard a write.

### The cart

```json
{"id":"01a1d613-fa2f-7beb-95d1-ce4c388b9fad","version":2,"currency":"EUR","items":[{"sku":"TSHIRT-BLK-M","name":"T-shirt black M","unitPrice":19.99,"quantity":2,"lineTotal":39.98},{"sku":"MUG-WHT","name":"Mug white 300 ml","unitPrice":7.5,"quantity":1,"lineTotal":7.5}],"total":47.48,"updatedAt":"2026-11-13T18:03:16.012529+00:00"}
```

| Field | Meaning |
|---|---|
| `id` | The cart's id, or `null` for a user who has no cart |
| `version` | Increases by one with every change to the cart; `0` when there is no cart |
| `currency` | Three-letter currency code shared by every line; `null` while the cart has no lines |
| `items` | The lines, in the order they were added: `sku`, `name`, `unitPrice`, `quantity`, `lineTotal` |
| `total` | The sum of the line totals |
| `updatedAt` | When the cart last changed, in UTC; `null` when there is no cart |

A user who never had a cart gets the same shape, empty. No cart is stored for them until they
set their first item:

```json
{"id":null,"version":0,"currency":null,"items":[],"total":0,"updatedAt":null}
```

Amounts are JSON numbers without padding zeros (`19.99`, never `19.9900`; `7.5`, not `7.50`).
Read them into a decimal type and format them for the currency on the client. A line's `name`
and `unitPrice` are the values from when the line was last set, not live catalog values.

Every answer from a cart address, errors included, carries `Cache-Control: no-store`: a cart
is one person's data.

### GET /api/v1/cart

```bash
curl http://localhost:8080/api/v1/cart -H "Authorization: Bearer $TOKEN"
```

Reading never creates a cart.

### PUT /api/v1/cart/items/{sku}

**Sets** the quantity of a product to the value given. It replaces the quantity the cart
holds; it never adds to it. Setting 2 and then 5 leaves 5, and setting 2 twice leaves 2. There
is no "add one more" operation.

```bash
curl -X PUT http://localhost:8080/api/v1/cart/items/TSHIRT-BLK-M \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"quantity":2}'
```

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Cache-Control: no-store

{"id":"01a1d613-fa2f-7beb-95d1-ce4c388b9fad","version":1,"currency":"EUR","items":[{"sku":"TSHIRT-BLK-M","name":"T-shirt black M","unitPrice":19.99,"quantity":2,"lineTotal":39.98}],"total":39.98,"updatedAt":"2026-11-13T18:03:15.887482+00:00"}
```

- The first item a user sets creates their cart and fixes its currency.
- Setting an item also refreshes the line's name and price from the product.
- The body carries a quantity and nothing else. Any other field in it, a price for example, is
  ignored: names and prices always come from the service's own product data.
- A body that names the same field twice is refused with a `400`.
- Repeating a request that changes nothing answers the same cart with the same `version`.

| Rule | Value | When broken |
|---|---|---|
| `sku` | 1 to 64 letters, digits, dots, underscores or hyphens, starting with a letter or a digit | `400 validation_failed`, `errors.sku` |
| `quantity` | Required, a whole number from 1 to 99 | `400 validation_failed`, `errors.quantity` |
| The product exists | | `404 product_not_found` |
| The product is still sold | | `422 product_unavailable` |
| One currency per cart | The product's currency is the cart's | `422 currency_mismatch` |
| Different products per cart | At most 50 | `422 item_limit_exceeded` |

Both validation rules are checked before anything else, and both mistakes are reported at
once. A request that is refused changes nothing.

Every route that changes a cart (`PUT` and both `DELETE`s) can also answer `409`; see
[Parallel requests](#parallel-requests).

### DELETE /api/v1/cart/items/{sku}

Removes a product from the cart.

```bash
curl -X DELETE http://localhost:8080/api/v1/cart/items/MUG-WHT -H "Authorization: Bearer $TOKEN"
```

- Removing a product that is not in the cart answers `200` with the cart unchanged. The same
  goes for a SKU that is not a product at all: removal does not look the product up.
- A product that is no longer sold can still be removed.
- Removing the last product leaves the cart in place, empty, with `currency` back to `null`.
- A malformed `sku` is `400 validation_failed`, as for `PUT`.
- For a user without a cart the answer is the empty cart, and none is created.

### DELETE /api/v1/cart

Removes every product in one change.

```bash
curl -X DELETE http://localhost:8080/api/v1/cart -H "Authorization: Bearer $TOKEN"
```

```json
{"id":"01a1d613-fa2f-7beb-95d1-ce4c388b9fad","version":4,"currency":null,"items":[],"total":0,"updatedAt":"2026-11-13T18:03:29.219526+00:00"}
```

The cart keeps its `id` and its `version` goes up by one. Clearing a cart that is already
empty changes nothing. For a user without a cart the answer is the empty cart, and none is
created.

## Errors

Every response that is not a success is a problem document
([RFC 9457](https://www.rfc-editor.org/rfc/rfc9457)) with the media type
`application/problem+json`. That includes errors raised by the framework itself. No error ever
carries a stack trace or an internal message.

The format assumes a client that accepts JSON, which is what an `Accept` header of `*/*`, or
none at all, means. A client that says it accepts only something else, `text/html` for
example, still gets the right status, but the errors raised by the framework arrive as a short
plain-text line, and the cart's own errors arrive without the `traceId`.

```http
HTTP/1.1 401 Unauthorized
Content-Type: application/problem+json
WWW-Authenticate: Bearer

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.2","title":"Unauthorized","status":401,"traceId":"00-15d44312655bec2d3987d55334a699df-288321a05710720e-00"}
```

| Status | `code` | When |
|---|---|---|
| `400` | `validation_failed` | A value breaks a rule. The broken rules are listed by field under `errors` |
| `400` | none | The body is missing, is not JSON, names a field twice, or has a value of the wrong type: a quantity in quotes, with a decimal point or an exponent (`"2"`, `2.0`, `1e1`), or too large to be a whole number |
| `401` | none | No valid token |
| `404` | `product_not_found` | The product to set does not exist |
| `404` | none | No such address |
| `405` | none | The address exists but not for this method; the `Allow` header lists the methods it has |
| `409` | `cart_busy` | Another change to the same cart did not finish in time. Nothing was changed; send the request again after the number of seconds in `Retry-After` |
| `409` | `concurrency_conflict` | The cart changed while the request was being applied. Nothing was changed; read the cart and send the request again. This is a safety net that a correct deployment never shows |
| `413` | none | The request body is larger than 16 KB |
| `415` | none | The request body is not sent as `application/json` |
| `422` | `product_unavailable` | The product is no longer sold |
| `422` | `currency_mismatch` | The product's currency is not the cart's |
| `422` | `item_limit_exceeded` | The cart already holds 50 different products |
| `500` | none | The API failed. The body says nothing about the cause; the log does, under the `traceId` |

`code` is the stable name of a cart error; it is what a client should switch on. `title` and
`detail` are for people and may be reworded.

A cart rule that is broken:

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json
Cache-Control: no-store

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Product not found","status":404,"detail":"No product has this SKU.","code":"product_not_found","traceId":"00-14fba447d33209a481c9510e56cc7b26-d3950b16d6d82c65-00"}
```

A request that fails validation:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
Cache-Control: no-store

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"sku":["1 to 64 letters, digits, dots, underscores or hyphens, starting with a letter or a digit."],"quantity":["Required: a whole number from 1 to 99."]},"code":"validation_failed","traceId":"00-6038fd1528ba24ba583d2c4df94daf32-9b876d41b072d10a-00"}
```

`traceId` identifies the request in the logs.

## Health

Not part of the cart contract, and open without a token. Both answer `GET` only, with a plain
text body of `Healthy` or `Unhealthy`.

| Endpoint | 200 means | Otherwise |
|---|---|---|
| `GET /health/live` | The process responds | The process is restarted by whatever runs it |
| `GET /health/ready` | The database answers and is migrated | `503`: traffic should go elsewhere until it recovers |
