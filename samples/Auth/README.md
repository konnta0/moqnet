# Authenticated relay sample

This sample issues short-lived, path-scoped JWTs for the upstream `moq-relay` 0.17 series.
The relay asks `moq auth serve` to admit each session; the auth server verifies the token
and independently enforces publish and subscribe permissions.

The committed `dev-key.jwk` and `X-Sample-User` header are deliberately insecure
development fixtures. In a real API, authenticate the HTTP request normally,
keep the signing key in a secret store, and derive the user ID from
`HttpContext.User`.

## Run

Install a 0.17-series relay and the `moq` CLI (auth server). These crate versions
match [moq-relay 0.17.9](https://github.com/moq-dev/moq/releases/tag/moq-relay-v0.17.9):

```bash
cargo install moq-relay --version 0.17.9
cargo install moq-cli --version 0.14.5
```

From the repository root, start the auth server, relay, and ticket API in separate terminals:

```bash
moq auth serve --listen 127.0.0.1:4440 --key samples/Auth/dev-key.jwk
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

`put` / `get` claims are the 0.14 form; 0.17 still reads them as subtree patterns
(`alice` → `alice/**`, `""` → `**`). After connecting at `/rooms/demo`, that
publisher creates broadcast `alice`. A subscriber to the same room receives
`get: [""]` and can request `alice`, but cannot publish. A token for another
room is rejected.

The relay uses a self-signed certificate. Development clients must disable TLS
verification or explicitly trust the generated certificate. Never disable TLS
verification in production.

For anonymous development without JWTs, replace `[auth] url` with
`public = "**"` (opens every path; development only).
