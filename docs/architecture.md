# Architecture

```
moq-net / moq-native / hang / moq-mux / moq-json / moq-audio / moq-video
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

Transport (QUIC / WebTransport / HTTP/3) stays inside `moq-native`. C# never speaks HTTP, so YetAnotherHttpHandler is not used.

Pinned upstream: `moq` `0f7cbd765f183dd13dd3b953a47f8c6b61811da0` (`moq-ffi` 0.3.17).
