using System.Diagnostics;

namespace MoqNet.Samples;

/// <summary>
/// Shared broadcast path and media parameters for the live sample apps.
/// </summary>
public static class SampleProtocol
{
    /// <summary>Default QUIC / WebTransport bind address for the sample relay.</summary>
    public const string DefaultBind = "127.0.0.1:4433";

    /// <summary>Default client URL for the sample relay.</summary>
    public const string DefaultRelayUrl = "https://127.0.0.1:4433/";

    /// <summary>Hostnames used when the relay generates a development certificate.</summary>
    public const string TlsGenerateHosts = "localhost";

    /// <summary>Broadcast path announced by the publisher.</summary>
    public const string BroadcastPath = "samples/live";

    /// <summary>Opus audio track name.</summary>
    public const string AudioTrack = "audio";

    /// <summary>JPEG preview track used so receivers can display frames without a video decoder.</summary>
    public const string PreviewTrack = "preview";

    /// <summary>Codec name passed to <c>SubscribeAudio</c>.</summary>
    public const string AudioCodec = "opus";

    /// <summary>PCM sample rate used by every sample publisher.</summary>
    public const uint AudioSampleRate = 48000;

    /// <summary>PCM channel count used by every sample publisher.</summary>
    public const uint AudioChannels = 1;

    /// <summary>Native Opus frame duration.</summary>
    public const uint AudioFrameDurationMs = 20;

    /// <summary>Samples in one 20 ms frame at <see cref="AudioSampleRate"/>.</summary>
    public const int AudioFrameSamples = 960;

    /// <summary>Encoded video width.</summary>
    public const uint VideoWidth = 320;

    /// <summary>Encoded video height.</summary>
    public const uint VideoHeight = 180;

    /// <summary>Publisher capture rate for video and JPEG preview.</summary>
    public const uint VideoFps = 10;

    /// <summary>Maximum accepted JPEG preview payload.</summary>
    public const int MaxPreviewBytes = 256 * 1024;

    /// <summary>Maximum accepted PCM frame.</summary>
    public const int MaxPcmBytes = 16 * 1024;

    /// <summary>Packed RGBA size for <see cref="VideoWidth"/> × <see cref="VideoHeight"/>.</summary>
    public const int MaxRgbaBytes = 230400;

    /// <summary>Returns the current timestamp in microseconds.</summary>
    public static ulong TimestampUs()
    {
        var ticks = (ulong)Stopwatch.GetTimestamp();
        var frequency = (ulong)Stopwatch.Frequency;
        return (ticks / frequency * 1_000_000UL) +
               (ticks % frequency * 1_000_000UL / frequency);
    }

    /// <summary>Returns true when <paramref name="payload"/> is a plausible PCM frame.</summary>
    public static bool IsValidPcm(byte[]? payload)
    {
        return payload is { Length: > 0 and <= MaxPcmBytes } && (payload.Length % sizeof(float)) == 0;
    }

    /// <summary>Returns true when <paramref name="payload"/> is a packed RGBA frame of the sample size.</summary>
    public static bool IsValidRgba(byte[]? payload)
    {
        return payload is { Length: MaxRgbaBytes };
    }

    /// <summary>Returns true when <paramref name="payload"/> is a bounded JPEG payload.</summary>
    public static bool IsValidJpeg(byte[]? payload)
    {
        return payload is { Length: > 2 and <= MaxPreviewBytes } && payload[0] == 0xFF && payload[1] == 0xD8;
    }
}
