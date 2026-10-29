# Architecture

```
moq-net / moq-tokio / hang / moq-mux / moq-json / moq-audio / moq-video
        │
        ▼
native/moqnet-ffi   C ABI + csbindgen
        │
        ▼
MoqNet.Native       .NET Standard 2.1
   ┌────┴────┐
   ▼         ▼
Client     Unity UPM + UniTask
(.NET 8)
```

Upstream `moq-ffi` is UniFFI-only. This repo re-exposes the same object model as a C ABI so Cysharp/csbindgen can generate Unity-safe P/Invoke.

Transport (QUIC / WebTransport / HTTP/3) stays inside `moq-tokio`. C# never speaks HTTP, so YetAnotherHttpHandler is not used.

Pinned upstream: crates.io versions published with [moq-relay 0.17.9](https://github.com/moq-dev/moq/releases/tag/moq-relay-v0.17.9) (`30fd9d450bd380f8cac77d97d44b42976ba16878`). `moq-native` 0.20.0 is a compile-fail tombstone.

Desktop CI builds `audio,video` on Linux and runs `dotnet test MoqNet.sln`. Package tags (`native/v*`, `client/v*`, `unity/v*`) publish NuGet / UPM artifacts. Android / iOS simulator binaries are produced with `scripts/build-android.sh` and `scripts/build-ios.sh`.
