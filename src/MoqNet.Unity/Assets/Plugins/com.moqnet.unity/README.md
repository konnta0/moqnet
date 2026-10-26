# MoqNet for Unity

UPM package wrapping MoqNet.Native with UniTask.

This package depends on [UniTask](https://github.com/Cysharp/UniTask) but does **not** declare it in `package.json` (projects that vendor UniTask as source would otherwise conflict). Install UniTask separately.

TLS verification is on by default. `SetTlsDisableVerify(true)` is for development only.

Native C# sources live in `Runtime/Native/` so Unity applies platform-specific `DllImport` names from the generated bindings.

The published package includes an iOS XCFramework for arm64 devices and arm64/x64 Simulators.
It requires Unity 2021.3.37f1 or newer. Android libraries still need a manual cross-build
(see the repository `docs/mobile-build-guide.md`).
