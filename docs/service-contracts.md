# Contracts between the Cart service and its neighbours

**PROPOSED - NOT IMPLEMENTED.** Nothing on this page exists in the code. This repository
implements the Cart service's public API only ([api.md](api.md)); these are the three places
where it would connect to the rest of the platform described in
[architecture.md](architecture.md). The event rules they follow (outbox, at-least-once
delivery, full state plus a version) are in section 2.2 of that document.

| # | From | To | Kind | Purpose |
|---|---|---|---|---|
| A | Catalog | Cart | Event | Keep the cart's own copy of products and prices current |
| B | Orders | Cart | Internal HTTP call | Read the cart that is being checked out |
| C | Orders | Cart | Event | Clear the cart that was bought |

## A. Catalog to Cart: a product or its price changed

**PROPOSED - NOT IMPLEMENTED.** Today the `products` table is keyed by SKU alone, has no
version, and is filled with demo data by the migrate step.

Topic `catalog.product-changed`, keyed by `sku` and `market`, so one product's events arrive
in order. Each event carries the full new state, never a difference.

```json
{
  "eventId": "0193b1c2-7a40-7c11-9a55-0f6f5d3c2a10",
  "occurredAt": "2026-11-14T10:15:00Z",
  "sku": "TSHIRT-BLK-M",
  "market": "HR",
  "name": "T-shirt black M",
  "unitPrice": 19.99,
  "currency": "EUR",
  "isActive": true,
  "version": 42
}
```

- **What the Cart service does:** one upsert into `products` on (`sku`, `market`), applied
  only when the event's `version` is higher than the stored one.
- **Duplicates and older events:** a version that is equal or lower changes nothing, so a
  redelivered or late event is harmless and needs no bookkeeping.
- **Existing carts are not rewritten.** A line keeps the name and price from when it was last
  set, as it does today. It is a display value; the price that binds is the quote at checkout.
- **A product that is withdrawn** arrives as `isActive: false`. It can then no longer be added
  or changed, and can still be removed, which is the rule the code already has.

## B. Orders to Cart: read the cart at checkout

**PROPOSED - NOT IMPLEMENTED.**

```http
GET /internal/v1/carts/by-user/alice
Authorization: Bearer <service token>
```

```json
{"id":"01a1d613-fa2f-7beb-95d1-ce4c388b9fad","version":2,"currency":"EUR","items":[{"sku":"TSHIRT-BLK-M","name":"T-shirt black M","unitPrice":19.99,"quantity":2,"lineTotal":39.98}],"total":39.98,"updatedAt":"2026-11-13T18:03:16.012529+00:00"}
```

The body is the same cart document the public API returns. A user without a cart gets the
empty cart (`"id": null`, `"version": 0`); the Order service refuses to quote that.

- **Authentication:** a short-lived token from the identity provider's client-credentials
  flow, with the Cart API as audience and a `cart.read` scope. A shopper's token is refused.
- **Timeout and failure:** the Order service waits at most 500 ms and retries once (a read is
  safe to repeat). If the cart still cannot be read, the quote fails with a `503` and nothing
  has been reserved or charged. Checkout never continues with lines sent by the client.
- **What the Order service keeps:** `id` and `version`. They identify exactly what was
  quoted, make "one live order per cart version" enforceable, and come back in contract C.
- **Why it is not in this repository, and never public:** the address names a user, which the
  public API never allows (there the cart always comes from the caller's own token). It is
  safe only on a private network, not routed by the gateway, with a service identity, and this
  repository ships neither an identity provider nor a second service to call it.

## C. Orders to Cart: the order was confirmed

**PROPOSED - NOT IMPLEMENTED.**

Topic `orders.order-confirmed`, keyed by `orderId`. It is the same event the Order worker
uses to send the invoice.

```json
{
  "eventId": "0193b1d0-2f3e-7d42-8c1b-6a9e4b7f1c22",
  "occurredAt": "2026-11-14T10:21:07Z",
  "orderId": "0193b1cf-9d10-7a03-b6f4-2c8d5e1a7b90",
  "userId": "alice",
  "cartId": "01a1d613-fa2f-7beb-95d1-ce4c388b9fad",
  "cartVersion": 2
}
```

`userId` is the token subject, an opaque id; the event carries no name, address or contact.

- **What the Cart service does:** takes the user's cart lock (the one every change to a cart
  already takes), and clears the cart only if its `id` is `cartId` and its `version` is still
  `cartVersion`.
- **A cart changed after checkout is left alone.** If the shopper added or changed something
  while paying, the version is higher and the event clears nothing. They may still see
  what they bought next to what they added; that is preferred to deleting something they
  never bought.
- **Duplicates:** clearing raises the cart's version, so a second delivery of the same event
  no longer matches and does nothing. No table of processed events is needed.
- **A failed or abandoned payment** produces no event, so the cart stays as it was.
