# MoqNet samples

Live audio + video fan-out. One publisher, many subscribers. Unity and the Blazor app talk to the same relay.

```
Unity / Web publisher ──▶ Relay (QUIC) ──▶ Unity / Web subscribers × N
```

- Broadcast: `samples/live`
- `audio`: PCM → Opus → PCM (`PublishAudio` / `SubscribeAudio`)
- `video`: RGBA → H.264 when the software encoder is present
- `preview`: JPEG so every receiver can show a picture (MoqNet has no video decoder yet)

## Web

The browser captures and plays media. The MoQ session runs in the ASP.NET process with `MoqNet.Client`. Native QUIC cannot run inside Blazor WebAssembly; this hosted model is the supported way to use MoqNet from a web app.

```bash
dotnet run --project samples/Web
```

Open [http://127.0.0.1:5178](http://127.0.0.1:5178). `/publish` is the sender. Open `/subscribe` in several tabs for receivers.

The web project also starts the relay on `127.0.0.1:4433`. Set `MoqNet:HostRelay` to `false` if you already have `samples/Relay` running.

## Relay only

```bash
dotnet run --project samples/Relay
```

Optional bind argument: `dotnet run --project samples/Relay -- 127.0.0.1:4433`

## Authenticated relay

[`Auth/`](Auth/README.md) demonstrates the production-oriented split: an ASP.NET API
issues short-lived room/role-scoped JWTs, and the upstream `moq-relay` enforces
publish and subscribe paths. The sample identity header and committed key are for
local development only.

## Unity

See [Unity/README.md](Unity/README.md). Publisher and subscriber are a role switch in the same project.

## Prerequisites

Build the native library first so `MoqNet.Native` can load `moqnet_ffi`:

```bash
./scripts/build-native.sh
dotnet build src/MoqNet.Native/MoqNet.Native.csproj
```

Development TLS is self-signed. Sample clients call `SetTlsDisableVerify(true)` for localhost only.

## Smoke

After the native library is built:

```bash
dotnet run --project samples/Smoke
```

This starts an in-process relay, publishes Opus + JPEG (+ H.264 when the encoder is present), and checks that a subscriber receives the audio and preview.
