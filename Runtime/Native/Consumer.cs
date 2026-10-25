using System;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    /// <summary>
    /// Consumes tracks, media, JSON, and audio from a broadcast.
    /// </summary>
    public sealed class BroadcastConsumer : NativeObject
    {
        internal BroadcastConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Current route as JSON.</summary>
        public unsafe string RouteJson
        {
            get
            {
                byte* json = null;
                Ffi.Check(NativeMethods.moq_broadcast_consumer_route_json((void*)Handle, &json), "RouteJson");
                return Ffi.ClaimString(json) ?? "{}";
            }
        }

        /// <summary>Watches route updates.</summary>
        public unsafe RouteWatch RouteUpdates()
        {
            void* watch = null;
            Ffi.Check(NativeMethods.moq_broadcast_consumer_route_updates((void*)Handle, &watch), "RouteUpdates");
            return new RouteWatch((IntPtr)watch);
        }

        /// <summary>Subscribes to the hang catalog. Blocks until the catalog track is ready.</summary>
        public unsafe CatalogConsumer SubscribeCatalog()
        {
            void* catalog = null;
            Ffi.Check(NativeMethods.moq_broadcast_consumer_subscribe_catalog((void*)Handle, &catalog), "SubscribeCatalog");
            return new CatalogConsumer((IntPtr)catalog);
        }

        /// <summary>Subscribes to a raw track. Blocks until the subscription is established.</summary>
        public unsafe TrackConsumer SubscribeTrack(string name, SubscriptionOptions? options = null)
        {
            Require(name, nameof(name));
            var opts = options ?? new SubscriptionOptions();
            using var utf = Utf8String.Create(name);
            void* track = null;
            Ffi.Check(
                NativeMethods.moq_broadcast_consumer_subscribe_track(
                    (void*)Handle,
                    utf.Ptr,
                    opts.Priority,
                    opts.Ordered ? (byte)1 : (byte)0,
                    opts.LatencyMaxMs,
                    opts.GroupStart,
                    opts.GroupEnd,
                    &track),
                "SubscribeTrack");
            return new TrackConsumer((IntPtr)track);
        }

        /// <summary>Subscribes to encoded media. Treat init bytes as an untrusted bitstream.</summary>
        public unsafe MediaConsumer SubscribeMedia(string name, MediaContainerKind kind, byte[]? init = null, SubscriptionOptions? options = null)
        {
            Require(name, nameof(name));
            var opts = options ?? new SubscriptionOptions();
            using var utf = Utf8String.Create(name);
            void* media = null;
            if (init == null || init.Length == 0)
            {
                Ffi.Check(
                    NativeMethods.moq_broadcast_consumer_subscribe_media(
                        (void*)Handle,
                        utf.Ptr,
                        (byte)kind,
                        null,
                        UIntPtr.Zero,
                        opts.Priority,
                        opts.Ordered ? (byte)1 : (byte)0,
                        opts.LatencyMaxMs,
                        &media),
                    "SubscribeMedia");
            }
            else
            {
                fixed (byte* p = init)
                {
                    Ffi.Check(
                        NativeMethods.moq_broadcast_consumer_subscribe_media(
                            (void*)Handle,
                            utf.Ptr,
                            (byte)kind,
                            p,
                            (UIntPtr)(ulong)init.Length,
                            opts.Priority,
                            opts.Ordered ? (byte)1 : (byte)0,
                            opts.LatencyMaxMs,
                            &media),
                        "SubscribeMedia");
                }
            }

            return new MediaConsumer((IntPtr)media);
        }

        /// <summary>Fetches a historical group by sequence.</summary>
        public unsafe GroupConsumer FetchGroup(string name, ulong sequence, byte priority = 128)
        {
            Require(name, nameof(name));
            using var utf = Utf8String.Create(name);
            void* group = null;
            Ffi.Check(NativeMethods.moq_broadcast_consumer_fetch_group((void*)Handle, utf.Ptr, sequence, priority, &group), "FetchGroup");
            return new GroupConsumer((IntPtr)group);
        }

        /// <summary>Subscribes to a JSON snapshot track.</summary>
        public unsafe JsonSnapshotConsumer SubscribeJsonSnapshot(string name, bool compression = false)
        {
            Require(name, nameof(name));
            using var utf = Utf8String.Create(name);
            void* consumer = null;
            Ffi.Check(NativeMethods.moq_broadcast_consumer_subscribe_json_snapshot((void*)Handle, utf.Ptr, compression ? (byte)1 : (byte)0, &consumer), "SubscribeJsonSnapshot");
            return new JsonSnapshotConsumer((IntPtr)consumer);
        }

        /// <summary>Subscribes to a JSON stream track.</summary>
        public unsafe JsonStreamConsumer SubscribeJsonStream(string name, bool compression = false)
        {
            Require(name, nameof(name));
            using var utf = Utf8String.Create(name);
            void* consumer = null;
            Ffi.Check(NativeMethods.moq_broadcast_consumer_subscribe_json_stream((void*)Handle, utf.Ptr, compression ? (byte)1 : (byte)0, &consumer), "SubscribeJsonStream");
            return new JsonStreamConsumer((IntPtr)consumer);
        }

        /// <summary>Subscribes to an audio track and decodes PCM. Treat codec input as untrusted.</summary>
        public unsafe AudioConsumer SubscribeAudio(string name, string codec, uint sampleRate, uint channelCount, AudioFormat format, long latencyMaxMs = -1)
        {
            Require(name, nameof(name));
            Require(codec, nameof(codec));
            using var n = Utf8String.Create(name);
            using var c = Utf8String.Create(codec);
            void* consumer = null;
            Ffi.Check(
                NativeMethods.moq_broadcast_consumer_subscribe_audio(
                    (void*)Handle,
                    n.Ptr,
                    c.Ptr,
                    sampleRate,
                    channelCount,
                    (byte)format,
                    latencyMaxMs,
                    &consumer),
                "SubscribeAudio");
            return new AudioConsumer((IntPtr)consumer);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_broadcast_consumer_free(handle);
        }
    }

    /// <summary>Watches broadcast route changes.</summary>
    public sealed class RouteWatch : NativeObject
    {
        internal RouteWatch(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Next"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_route_watch_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next route JSON, or returns null when dropped.</summary>
        public unsafe string? Next()
        {
            byte* json = null;
            var status = NativeMethods.moq_route_watch_next((void*)Handle, &json);
            if (Ffi.IsNone(status, "RouteWatch.Next"))
            {
                return null;
            }

            return Ffi.ClaimString(json);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_route_watch_free(handle);
        }
    }

    /// <summary>Consumes hang catalog updates as JSON.</summary>
    public sealed class CatalogConsumer : NativeObject
    {
        internal CatalogConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Next"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_catalog_consumer_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next catalog JSON, or returns null at end of stream.</summary>
        public unsafe string? Next()
        {
            byte* json = null;
            var status = NativeMethods.moq_catalog_consumer_next((void*)Handle, &json);
            if (Ffi.IsNone(status, "CatalogConsumer.Next"))
            {
                return null;
            }

            return Ffi.ClaimString(json);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_catalog_consumer_free(handle);
        }
    }

    /// <summary>Consumes frames from a raw track.</summary>
    public sealed class TrackConsumer : NativeObject
    {
        internal TrackConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking read.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_track_consumer_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next frame, or returns null when the track ends.</summary>
        public unsafe MoqFrame? ReadFrame()
        {
            byte* payload = null;
            UIntPtr len = UIntPtr.Zero;
            ulong timestamp = 0;
            var status = NativeMethods.moq_track_consumer_read_frame((void*)Handle, &payload, &len, &timestamp);
            if (Ffi.IsNone(status, "ReadFrame"))
            {
                return null;
            }

            return new MoqFrame(Ffi.ClaimBuffer(payload, len), timestamp);
        }

        /// <summary>Blocks until the next group, or returns null when the track ends.</summary>
        public unsafe GroupConsumer? NextGroup()
        {
            void* group = null;
            var status = NativeMethods.moq_track_consumer_next_group((void*)Handle, &group);
            if (Ffi.IsNone(status, "NextGroup"))
            {
                return null;
            }

            return new GroupConsumer((IntPtr)group);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_track_consumer_free(handle);
        }
    }

    /// <summary>Consumes frames from a single group.</summary>
    public sealed class GroupConsumer : NativeObject
    {
        internal GroupConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Group sequence number.</summary>
        public unsafe ulong Sequence => NativeMethods.moq_group_consumer_sequence((void*)Handle);

        /// <summary>Blocks until the next frame, or returns null when the group ends.</summary>
        public unsafe MoqFrame? ReadFrame()
        {
            byte* payload = null;
            UIntPtr len = UIntPtr.Zero;
            ulong timestamp = 0;
            var status = NativeMethods.moq_group_consumer_read_frame((void*)Handle, &payload, &len, &timestamp);
            if (Ffi.IsNone(status, "ReadFrame"))
            {
                return null;
            }

            return new MoqFrame(Ffi.ClaimBuffer(payload, len), timestamp);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_group_consumer_free(handle);
        }
    }

    /// <summary>Consumes decoded media frames from a container.</summary>
    public sealed class MediaConsumer : NativeObject
    {
        internal MediaConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Next"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_media_consumer_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next media frame, or returns null at end of stream.</summary>
        public unsafe MoqFrame? Next()
        {
            byte* payload = null;
            UIntPtr len = UIntPtr.Zero;
            ulong timestamp = 0;
            byte keyframe = 0;
            var status = NativeMethods.moq_media_consumer_next((void*)Handle, &payload, &len, &timestamp, &keyframe);
            if (Ffi.IsNone(status, "MediaConsumer.Next"))
            {
                return null;
            }

            return new MoqFrame(Ffi.ClaimBuffer(payload, len), timestamp, keyframe != 0);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_media_consumer_free(handle);
        }
    }

    /// <summary>Consumes JSON snapshot documents.</summary>
    public sealed class JsonSnapshotConsumer : NativeObject
    {
        internal JsonSnapshotConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Next"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_json_snapshot_consumer_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next JSON document, or returns null at end of stream.</summary>
        public unsafe string? Next()
        {
            byte* json = null;
            var status = NativeMethods.moq_json_snapshot_consumer_next((void*)Handle, &json);
            if (Ffi.IsNone(status, "JsonSnapshotConsumer.Next"))
            {
                return null;
            }

            return Ffi.ClaimString(json);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_json_snapshot_consumer_free(handle);
        }
    }

    /// <summary>Consumes JSON stream events.</summary>
    public sealed class JsonStreamConsumer : NativeObject
    {
        internal JsonStreamConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Next"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_json_stream_consumer_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next JSON value, or returns null at end of stream.</summary>
        public unsafe string? Next()
        {
            byte* json = null;
            var status = NativeMethods.moq_json_stream_consumer_next((void*)Handle, &json);
            if (Ffi.IsNone(status, "JsonStreamConsumer.Next"))
            {
                return null;
            }

            return Ffi.ClaimString(json);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_json_stream_consumer_free(handle);
        }
    }

    /// <summary>Consumes decoded PCM audio.</summary>
    public sealed class AudioConsumer : NativeObject
    {
        internal AudioConsumer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="Next"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_audio_consumer_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until the next PCM frame, or returns null at end of stream.</summary>
        public unsafe MoqFrame? Next()
        {
            byte* payload = null;
            UIntPtr len = UIntPtr.Zero;
            ulong timestamp = 0;
            var status = NativeMethods.moq_audio_consumer_next((void*)Handle, &payload, &len, &timestamp);
            if (Ffi.IsNone(status, "AudioConsumer.Next"))
            {
                return null;
            }

            return new MoqFrame(Ffi.ClaimBuffer(payload, len), timestamp);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_audio_consumer_free(handle);
        }
    }
}
