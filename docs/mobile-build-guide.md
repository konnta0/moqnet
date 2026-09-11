# Mobile cross-build (follow-up)

Desktop host builds are covered by `./scripts/build-native.sh`.

Android (`aarch64-linux-android`, `armv7-linux-androideabi`, `x86_64-linux-android`) and iOS (`aarch64-apple-ios`) need an NDK / Xcode cross toolchain, matching ZenohDotNet's later mobile guide.

Suggested starting points once the desktop crate is stable:

```bash
# Android
cargo ndk -t arm64-v8a -t armeabi-v7a -t x86_64 \
  build --release --package moqnet_ffi --no-default-features --features audio

# iOS
cargo build --release --target aarch64-apple-ios --package moqnet_ffi
```

Video hardware backends (NVENC / VAAPI / VideoToolbox / Media Foundation) are platform-specific. Prefer `--features audio` or software `openh264` on mobile.
