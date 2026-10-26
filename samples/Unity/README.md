# Unity live sample

One project, two roles. Use **Publisher** on one instance and **Subscriber** on as many others as you want.

## Open

1. Build the native library from the repo root (`./scripts/build-native.sh` then `dotnet build src/MoqNet.Native/MoqNet.Native.csproj`) so `moqnet_ffi` is copied into the UPM package.
2. Open this folder in Unity 2021.3.37f1 or newer.
3. Press Play. A host object is created automatically if the scene is empty.

UniTask is pulled from GitHub via `Packages/manifest.json`. `com.moqnet.unity` is referenced from this repository.

Desktop Editor / standalone only. Native FFI does not run in WebGL.

## Run

1. Start a relay (`dotnet run --project ../Web` or `dotnet run --project ../Relay`).
2. Play as **Publisher** (camera + microphone, or a generated tone and color bar).
3. Play additional Editor instances or standalone builds as **Subscriber**.

The default URL is `https://127.0.0.1:4433/`. TLS verification is disabled for the localhost self-signed certificate.

Receivers display the JPEG preview track. H.264 is published when the software encoder is available, but MoqNet does not decode video back to pixels yet.
