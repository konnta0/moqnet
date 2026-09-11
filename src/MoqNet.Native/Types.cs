using System;

namespace MoqNet.Native
{
    /// <summary>
    /// Options applied when publishing or accepting a raw track.
    /// </summary>
    public sealed class TrackOptions
    {
        /// <summary>
        /// Track priority. Higher values are preferred by the subscriber.
        /// </summary>
        public byte Priority { get; set; } = 128;

        /// <summary>
        /// When true, groups are delivered in order.
        /// </summary>
        public bool Ordered { get; set; } = true;

        /// <summary>
        /// Maximum latency in milliseconds, or -1 to use the native default.
        /// </summary>
        public long LatencyMaxMs { get; set; } = -1;

        /// <summary>
        /// Timestamp timescale, or -1 to use microseconds.
        /// </summary>
        public long Timescale { get; set; } = -1;
    }

    /// <summary>
    /// Options applied when subscribing to a track.
    /// </summary>
    public sealed class SubscriptionOptions
    {
        /// <summary>Subscriber priority.</summary>
        public byte Priority { get; set; } = 128;

        /// <summary>When true, groups are delivered in order.</summary>
        public bool Ordered { get; set; } = true;

        /// <summary>Maximum latency in milliseconds.</summary>
        public ulong LatencyMaxMs { get; set; }

        /// <summary>Inclusive group start, or -1 for the live edge.</summary>
        public long GroupStart { get; set; } = -1;

        /// <summary>Exclusive group end, or -1 for no end.</summary>
        public long GroupEnd { get; set; } = -1;
    }

    /// <summary>
    /// A media payload with a presentation timestamp in microseconds.
    /// </summary>
    public readonly struct MoqFrame
    {
        /// <summary>Presentation timestamp in microseconds.</summary>
        public ulong TimestampUs { get; }

        /// <summary>Copied payload bytes.</summary>
        public byte[] Payload { get; }

        /// <summary>True when this frame is a video keyframe. Null for raw tracks.</summary>
        public bool? Keyframe { get; }

        /// <summary>Creates a frame.</summary>
        public MoqFrame(byte[] payload, ulong timestampUs, bool? keyframe = null)
        {
            Payload = payload ?? Array.Empty<byte>();
            TimestampUs = timestampUs;
            Keyframe = keyframe;
        }
    }

    /// <summary>Hang catalog container kind for media subscribe.</summary>
    public enum MediaContainerKind : byte
    {
        /// <summary>Legacy hang container.</summary>
        Legacy = 0,

        /// <summary>CMAF / fMP4 with an init segment.</summary>
        Cmaf = 1,

        /// <summary>LOC container.</summary>
        Loc = 2,
    }

    /// <summary>PCM sample format, matching WebCodecs AudioData.format.</summary>
    public enum AudioFormat : byte
    {
        U8 = 0,
        S16 = 1,
        S32 = 2,
        F32 = 3,
        U8Planar = 4,
        S16Planar = 5,
        S32Planar = 6,
        F32Planar = 7,
    }

    /// <summary>Raw video pixel layout.</summary>
    public enum VideoPixelFormat : byte
    {
        /// <summary>Planar I420.</summary>
        I420 = 0,

        /// <summary>Packed RGBA.</summary>
        Rgba = 1,
    }

    /// <summary>Video codec for the native encoder.</summary>
    public enum VideoCodec : byte
    {
        H264 = 0,
        H265 = 1,
    }

    /// <summary>Which video encoder implementation to use.</summary>
    public enum VideoEncoderKind : byte
    {
        Auto = 0,
        Hardware = 1,
        Software = 2,
        Named = 3,
    }
}
