# Mobile Platform Build Guide

Desktop host builds use `./scripts/build-native.sh`. Android and iOS need a cross toolchain and are **not** part of the default desktop CI.

Mobile builds use `--no-default-features --features audio`. Hardware video encoders (NVENC / VAAPI / VideoToolbox / Media Foundation) are desktop-oriented; do not enable `video` on mobile unless you have verified the target.

## Prerequisites

### Android

1. **Android NDK r21+** (Android Studio or standalone)

   ```bash
   export ANDROID_NDK_HOME=/path/to/android-ndk
   # or
   export NDK_HOME=/path/to/android-ndk
   ```

2. **Rust Android targets**

   ```bash
   rustup target add aarch64-linux-android
   rustup target add armv7-linux-androideabi
   rustup target add x86_64-linux-android
   ```

3. Optional: [`cargo-ndk`](https://github.com/bbqsrc/cargo-ndk) (`cargo install cargo-ndk`). `build-android.sh` uses it when present; otherwise it drives the NDK clang linkers directly.

### iOS

1. **Xcode** from the Mac App Store (macOS only)
2. Xcode command line tools: `xcode-select --install`
3. **Rust iOS targets**

   ```bash
   rustup target add aarch64-apple-ios          # Device
   rustup target add aarch64-apple-ios-sim      # Simulator (Apple Silicon)
   rustup target add x86_64-apple-ios           # Simulator (Intel, optional)
   ```

## Building

```bash
# Android (any host with NDK)
./scripts/build-android.sh

# iOS (macOS + Xcode)
./scripts/build-ios.sh
```

Override features with `MOQNET_FEATURES` if needed (default is `audio`):

```bash
MOQNET_FEATURES=audio ANDROID_API_LEVEL=21 ./scripts/build-android.sh
```

Then copy bindings and assemble the UPM tree (binaries + `.meta` files):

```bash
./scripts/copy-bindings.sh
./scripts/pack-upm.sh
```

## Output

Libraries land under `native/output/`:

```
native/output/
├── android-arm64/      # Android ARM64 (arm64-v8a)
│   └── libmoqnet_ffi.so
├── android-arm/        # Android ARMv7 (armeabi-v7a)
│   └── libmoqnet_ffi.so
├── android-x86_64/     # Android x86_64
│   └── libmoqnet_ffi.so
├── ios-arm64/          # iOS device
│   └── libmoqnet_ffi.a
├── ios-sim-arm64/      # iOS Simulator (Apple Silicon)
│   └── libmoqnet_ffi.a
├── ios-sim-x64/        # iOS Simulator (Intel)
│   └── libmoqnet_ffi.a
├── ios-sim-universal/  # lipo of both simulator slices (when both exist)
│   └── libmoqnet_ffi.a
└── ios-xcframework/
    └── moqnet_ffi.xcframework  # Device + Simulator variants
```

`generate-unity-meta.sh` copies them into the UPM package:

```
src/MoqNet.Unity/Assets/Plugins/com.moqnet.unity/Plugins/
├── Android/
│   ├── arm64-v8a/libmoqnet_ffi.so
│   ├── armeabi-v7a/libmoqnet_ffi.so
│   └── x86_64/libmoqnet_ffi.so
└── iOS/
    └── moqnet_ffi.xcframework
```

Android keeps the `lib` prefix (`DllImport("moqnet_ffi")` → `libmoqnet_ffi.so`). Desktop Linux/macOS strip it so Unity can resolve `moqnet_ffi`. iOS player builds use `__Internal`; Xcode selects the device or Simulator static archive from the XCFramework.

Published `unity/v*` packages include desktop binaries plus an iOS XCFramework for arm64 devices and arm64/x64 Simulators. Android libraries are produced locally with `build-android.sh` until a mobile CI job is added.

## Unity configuration

### Android

`.meta` files set the CPU per ABI (`ARM64`, `ARMv7`, `X86_64`). Unity picks the matching library for the device.

Minimum: **API 21** (Android 5.0). IL2CPP is recommended.

### iOS

The XCFramework contains separate static archives for iOS devices and Simulators. Do not combine device and Simulator slices with `lipo`; only the two Simulator architectures are combined before `xcodebuild -create-xcframework`. iOS rustc cannot emit `cdylib`, so the scripts pass `--config 'lib.crate-type=["staticlib"]'`.

- IL2CPP backend is required
- Enable **Allow unsafe code**
- Minimum: **iOS 15.0**
- Unity **2021.3.37f1** or newer (`.xcframework` plug-in support)

## Troubleshooting

### Android: "cannot find -lmoqnet_ffi"

Confirm `Plugins/Android/<abi>/libmoqnet_ffi.so` exists and the `.meta` CPU matches that ABI.

### Android: linker / clang missing

`ANDROID_NDK_HOME` must point at a full NDK. `build-android.sh` looks for `toolchains/llvm/prebuilt/<host>/bin/<triple>21-clang`. Override the API with `ANDROID_API_LEVEL`.

### iOS: undefined symbols

1. Confirm `Plugins/iOS/moqnet_ffi.xcframework` is in the Xcode project
2. Confirm the XCFramework contains device and Simulator variants
3. Rebuild with `./scripts/build-ios.sh` on the same Xcode version you ship with

### iOS: not on macOS

The script exits immediately on Linux/Windows. Use a Mac runner or `publish-unity.yml`, which builds and packages all three iOS targets.
