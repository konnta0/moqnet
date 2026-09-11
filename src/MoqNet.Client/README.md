# MoqNet.Client

Async .NET 8 wrapper around [MoqNet.Native](../MoqNet.Native/README.md). Long waits (`ConnectAsync`, `ReadFrameAsync`, `NextAsync`, …) run on the thread pool and honor `CancellationToken` by calling the matching native `Cancel`.

TLS verification is on by default. `SetTlsDisableVerify(true)` is for development only.
