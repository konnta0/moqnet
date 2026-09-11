# MoqNet

![Development Status](https://img.shields.io/badge/status-alpha-red)
![Version](https://img.shields.io/badge/version-0.3.17.x-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)

C# / Unity bindings for [Media over QUIC](https://github.com/moq-dev/moq) — embedded native runtime via Cysharp/csbindgen.

## Project Status

**Unofficial, community-maintained bindings.** This project is not affiliated with moq-dev.

| | |
|--|--|
| **Maturity** | Alpha — APIs may change; package versions track `moq-ffi` |
| **Package version** | `0.3.17.x` = `moq-ffi` `0.3.17` + dotnet patch |
| **Upstream** | [moq](https://github.com/moq-dev/moq) `0f7cbd765f183dd13dd3b953a47f8c6b61811da0` |
| **Production use** | Evaluate for your workload; no stability guarantees yet |

**Available:** Client / Session / Origin / Broadcast / raw tracks / media catalog / JSON tracks / Server / audio / video encode helpers. .NET 8+ and Unity 2021.3+ on desktop.

**Caveats:** Mobile libraries need a [manual cross-build](docs/mobile-build-guide.md). Performance is not fully tuned.

Terms: [LICENSE](LICENSE) (MIT for this repo) · [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) (MIT OR Apache-2.0 for the bundled moq runtime)

## Overview

- **MoqNet.Native** — Low-level FFI bindings (.NET Standard 2.1) with embedded MoQ runtime
- **MoqNet.Client** — High-level async API for .NET 8.0+
- **MoqNet.Unity** — Unity-optimized wrapper with UniTask

Transport is QUIC / WebTransport inside the native library. Unity HTTP/2 (`YetAnotherHttpHandler`) is not required.

## Architecture

See [docs/architecture.md](docs/architecture.md).

```
┌─────────────────┐
│  moq (Rust)     │  ← moq-net / moq-native (MIT OR Apache-2.0)
└────────┬────────┘
         │
┌────────▼────────┐
│  moqnet-ffi     │  ← csbindgen generates C#
└────────┬────────┘
         │
┌────────▼────────┐
│ MoqNet.Native   │  ← Layer 1
└────────┬────────┘
    ┌────┴──────┐
┌───▼──────┐ ┌──▼─────────┐
│ Unity    │ │ Client     │
│ (UniTask)│ │ (.NET 8)   │
└──────────┘ └────────────┘
```

## Quick Start

### .NET 8.0+ (MoqNet.Client)

```csharp
using MoqNet.Client;

using var client = new Client();
// Development only — TLS verification is on by default.
// client.SetTlsDisableVerify(true);

await using var session = await client.ConnectAsync("https://relay.example/");
var broadcast = session.Publisher.CreateBroadcast("demos/dotnet");
using var track = broadcast.PublishTrack("status");
track.WriteFrame(System.Text.Encoding.UTF8.GetBytes("hello"), timestampUs: 0);
```

### Unity (MoqNet.Unity)

This package depends on [UniTask](https://github.com/Cysharp/UniTask) but does not declare it in `package.json` (to support projects that vendor UniTask as source). Install UniTask separately before use.

```csharp
using UnityEngine;
using MoqNet.Unity;
using Cysharp.Threading.Tasks;

public class MoqExample : MonoBehaviour
{
    async void Start()
    {
        using var client = new Client();
        var session = await client.ConnectAsync(
            "https://relay.example/",
            this.GetCancellationTokenOnDestroy());
        var broadcast = session.Publisher.CreateBroadcast("demos/unity");
        using var track = broadcast.PublishTrack("status");
        track.WriteFrame(System.Text.Encoding.UTF8.GetBytes("hello"), 0);
    }
}
```

## Supported Platforms

| Platform | Architecture | Status |
|----------|-------------|--------|
| Windows | x64, ARM64 | Supported (host build) |
| Linux | x64, ARM64 | Supported (host build) |
| macOS | x64, ARM64 | Supported (host build) |
| Android / iOS | | Via [cross-build](docs/mobile-build-guide.md) |

## Building from Source

Prerequisites: Rust 1.91+ (1.95 recommended), .NET SDK 8.0+, a C/C++ linker.

```bash
./scripts/build-native.sh
./scripts/copy-bindings.sh
dotnet test MoqNet.sln
# SDK 9+: dotnet test MoqNet.slnx
```

Default native features are `audio,video`. Override with `MOQNET_FEATURES=audio ./scripts/build-native.sh`. Linux GPU encoders (NVENC / VAAPI) are behind the optional `linux-gpu` feature.

## Versioning

`{MoqFfiVersion}.{dotnet-patch}` in [build/Versions.props](build/Versions.props). UPM uses `{MoqFfiVersion}-native.{dotnet-patch}`.
