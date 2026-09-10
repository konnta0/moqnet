# MoqNet.Native

Low-level FFI bindings for [MoQ](https://github.com/moq-dev/moq) (`netstandard2.1`). Transport (QUIC / WebTransport) stays inside the native library.

TLS certificate verification is **on by default**. `Client.SetTlsDisableVerify(true)` is for development only.

Native binaries are produced from `native/moqnet-ffi` and link moq (MIT OR Apache-2.0). See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
