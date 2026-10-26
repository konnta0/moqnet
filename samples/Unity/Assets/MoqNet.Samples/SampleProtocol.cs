namespace MoqNet.Samples.Unity
{
    /// <summary>Matches the .NET sample protocol so Unity and Blazor share one broadcast.</summary>
    public static class SampleProtocol
    {
        public const string DefaultRelayUrl = "https://127.0.0.1:4433/";
        public const string BroadcastPath = "samples/live";
        public const string AudioTrack = "audio";
        public const string PreviewTrack = "preview";
        public const string AudioCodec = "opus";
        public const uint AudioSampleRate = 48000;
        public const uint AudioChannels = 1;
        public const uint AudioFrameDurationMs = 20;
        public const int AudioFrameSamples = 960;
        public const int VideoWidth = 320;
        public const int VideoHeight = 180;
        public const int VideoFps = 10;
        public const int MaxPreviewBytes = 256 * 1024;
        public const int MaxPcmBytes = 16 * 1024;
        public const int RgbaBytes = 230400;

        public static ulong TimestampUs()
        {
            return (ulong)(UnityEngine.Time.realtimeSinceStartupAsDouble * 1000000.0);
        }
    }
}
