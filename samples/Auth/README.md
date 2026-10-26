# Authenticated relay sample

This sample issues short-lived, path-scoped JWTs for the upstream `moq-relay`.
The relay verifies the token and independently enforces publish (`put`) and
subscribe (`get`) permissions.

The committed `dev-key.jwk` and `X-Sample-User` header are deliberately insecure
development fixtures. In a real API, authenticate the HTTP request normally,
keep the signing key in a secret store, and derive the user ID from
`HttpContext.User`.

## Run

Install a relay compatible with the MoQ revision pinned by this repository:

```bash
cargo install --git https://github.com/moq-dev/moq \
  --rev 0f7cbd765f183dd13dd3b953a47f8c6b61811da0 moq-relay
```

From the repository root, start the relay and ticket API in separate terminals:

```bash
moq-relay samples/Auth/relay.toml
dotnet run --project samples/Auth
```

Request a subscriber ticket:

```bash
curl -sS http://127.0.0.1:5188/api/moq/tickets \
  -H 'Content-Type: application/json' \
  -H 'X-Sample-User: bob' \
  -d '{"room":"demo","role":"subscriber"}'
```

Request a publisher ticket by changing the role to `publisher`. The response
contains a `relayUrl`; pass it directly to `Client.ConnectAsync`.

For room `demo`, publisher `alice` receives this grant:

```json
{
  "root": "rooms/demo",
  "put": ["alice"],
  "exp": 0,
  "iat": 0
}
```

After connecting at `/rooms/demo`, that publisher creates broadcast `alice`.
A subscriber to the same room receives `get: [""]` and can request `alice`, but
cannot publish. A token for another room is rejected by the relay.

The relay uses a self-signed certificate. Development clients must disable TLS
verification or explicitly trust the generated certificate. Never disable TLS
verification in production.
