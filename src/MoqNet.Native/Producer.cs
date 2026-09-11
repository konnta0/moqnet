using System;
using MoqNet.Native.FFI;

namespace MoqNet.Native
{
    /// <summary>
    /// Produces tracks, media, JSON, audio, and video on a broadcast.
    /// </summary>
    public sealed class BroadcastProducer : NativeObject
    {
        /// <summary>Creates a standalone broadcast that is not attached to an origin.</summary>
        public static unsafe BroadcastProducer Create()
        {
            void* broadcast = null;
            Ffi.Check(NativeMethods.moq_broadcast_producer_new(&broadcast), "BroadcastProducer.Create");
            return new BroadcastProducer((IntPtr)broadcast);
        }

        internal BroadcastProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Returns a consumer of this broadcast.</summary>
        public unsafe BroadcastConsumer Consume()
        {
            void* consumer = null;
            Ffi.Check(NativeMethods.moq_broadcast_producer_consume((void*)Handle, &consumer), "Consume");
            return new BroadcastConsumer((IntPtr)consumer);
        }

        /// <summary>Accepts on-demand track requests.</summary>
        public unsafe BroadcastDynamic Dynamic()
        {
            void* dynamic = null;
            Ffi.Check(NativeMethods.moq_broadcast_producer_dynamic((void*)Handle, &dynamic), "Dynamic");
            return new BroadcastDynamic((IntPtr)dynamic);
        }

        /// <summary>Controls whether this broadcast is announced.</summary>
        public unsafe void SetAnnounce(bool announce)
        {
            Ffi.Check(NativeMethods.moq_broadcast_producer_set_announce((void*)Handle, announce ? (byte)1 : (byte)0), "SetAnnounce");
        }

        /// <summary>Sets a JSON catalog section.</summary>
        public unsafe void SetCatalogSection(string name, string json)
        {
            Require(name, nameof(name));
            Require(json, nameof(json));
            using var n = Utf8String.Create(name);
            using var j = Utf8String.Create(json);
            Ffi.Check(NativeMethods.moq_broadcast_producer_set_catalog_section((void*)Handle, n.Ptr, j.Ptr), "SetCatalogSection");
        }

        /// <summary>Removes a catalog section.</summary>
        public unsafe void RemoveCatalogSection(string name)
        {
            Require(name, nameof(name));
            using var n = Utf8String.Create(name);
            Ffi.Check(NativeMethods.moq_broadcast_producer_remove_catalog_section((void*)Handle, n.Ptr), "RemoveCatalogSection");
        }

        /// <summary>Publishes a raw byte track.</summary>
        public unsafe TrackProducer PublishTrack(string name, TrackOptions? options = null)
        {
            Require(name, nameof(name));
            var opts = options ?? new TrackOptions();
            using var utf = Utf8String.Create(name);
            void* track = null;
            Ffi.Check(
                NativeMethods.moq_broadcast_producer_publish_track(
                    (void*)Handle,
                    utf.Ptr,
                    opts.Priority,
                    opts.Ordered ? (byte)1 : (byte)0,
                    opts.LatencyMaxMs,
                    opts.Timescale,
                    &track),
                "PublishTrack");
            return new TrackProducer((IntPtr)track);
        }

        /// <summary>Publishes an encoded media container or elementary track.</summary>
        public unsafe MediaProducer PublishMedia(string format, byte[]? init = null)
        {
            Require(format, nameof(format));
            using var utf = Utf8String.Create(format);
            void* media = null;
            if (init == null || init.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_broadcast_producer_publish_media((void*)Handle, utf.Ptr, null, UIntPtr.Zero, &media), "PublishMedia");
            }
            else
            {
                fixed (byte* p = init)
                {
                    Ffi.Check(NativeMethods.moq_broadcast_producer_publish_media((void*)Handle, utf.Ptr, p, (UIntPtr)(ulong)init.Length, &media), "PublishMedia");
                }
            }

            return new MediaProducer((IntPtr)media);
        }

        /// <summary>Publishes a streaming media container (no init segment).</summary>
        public unsafe MediaStreamProducer PublishMediaStream(string format)
        {
            Require(format, nameof(format));
            using var utf = Utf8String.Create(format);
            void* media = null;
            Ffi.Check(NativeMethods.moq_broadcast_producer_publish_media_stream((void*)Handle, utf.Ptr, &media), "PublishMediaStream");
            return new MediaStreamProducer((IntPtr)media);
        }

        /// <summary>Publishes a JSON snapshot track.</summary>
        public unsafe JsonSnapshotProducer PublishJsonSnapshot(string name, uint deltaRatio = 0, bool compression = false)
        {
            Require(name, nameof(name));
            using var utf = Utf8String.Create(name);
            void* producer = null;
            Ffi.Check(NativeMethods.moq_broadcast_producer_publish_json_snapshot((void*)Handle, utf.Ptr, deltaRatio, compression ? (byte)1 : (byte)0, &producer), "PublishJsonSnapshot");
            return new JsonSnapshotProducer((IntPtr)producer);
        }

        /// <summary>Publishes a JSON stream track.</summary>
        public unsafe JsonStreamProducer PublishJsonStream(string name, bool compression = false)
        {
            Require(name, nameof(name));
            using var utf = Utf8String.Create(name);
            void* producer = null;
            Ffi.Check(NativeMethods.moq_broadcast_producer_publish_json_stream((void*)Handle, utf.Ptr, compression ? (byte)1 : (byte)0, &producer), "PublishJsonStream");
            return new JsonStreamProducer((IntPtr)producer);
        }

        /// <summary>Publishes a PCM audio track encoded to Opus inside the native library.</summary>
        public unsafe AudioProducer PublishAudio(string name, AudioFormat format, uint sampleRate, uint channels, long bitrate = -1, uint frameDurationMs = 20)
        {
            Require(name, nameof(name));
            using var utf = Utf8String.Create(name);
            void* producer = null;
            Ffi.Check(
                NativeMethods.moq_broadcast_producer_publish_audio(
                    (void*)Handle,
                    utf.Ptr,
                    (byte)format,
                    sampleRate,
                    channels,
                    bitrate,
                    frameDurationMs,
                    &producer),
                "PublishAudio");
            return new AudioProducer((IntPtr)producer);
        }

        /// <summary>Publishes a raw-video track encoded inside the native library.</summary>
        public unsafe VideoProducer PublishVideo(
            VideoPixelFormat format,
            uint width,
            uint height,
            uint framerate,
            VideoCodec codec = VideoCodec.H264,
            string? track = null,
            long bitrate = -1,
            int gop = -1,
            VideoEncoderKind kind = VideoEncoderKind.Auto,
            string? kindName = null)
        {
            using var trackUtf = Utf8String.Create(track);
            using var kindUtf = Utf8String.Create(kindName);
            void* producer = null;
            Ffi.Check(
                NativeMethods.moq_broadcast_producer_publish_video(
                    (void*)Handle,
                    (byte)format,
                    width,
                    height,
                    framerate,
                    (byte)codec,
                    trackUtf.Ptr,
                    bitrate,
                    gop,
                    (byte)kind,
                    kindUtf.Ptr,
                    &producer),
                "PublishVideo");
            return new VideoProducer((IntPtr)producer);
        }

        /// <summary>Finishes the broadcast.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_broadcast_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_broadcast_producer_free(handle);
        }
    }

    /// <summary>On-demand track production for a broadcast.</summary>
    public sealed class BroadcastDynamic : NativeObject
    {
        internal BroadcastDynamic(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Cancels a blocking <see cref="RequestedTrack"/> call.</summary>
        public unsafe void Cancel()
        {
            Ffi.Check(NativeMethods.moq_broadcast_dynamic_cancel((void*)Handle), "Cancel");
        }

        /// <summary>Blocks until a subscriber requests a track.</summary>
        public unsafe TrackRequest RequestedTrack()
        {
            void* request = null;
            Ffi.Check(NativeMethods.moq_broadcast_dynamic_requested_track((void*)Handle, &request), "RequestedTrack");
            return new TrackRequest((IntPtr)request);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_broadcast_dynamic_free(handle);
        }
    }

    /// <summary>A subscriber request for a track.</summary>
    public sealed class TrackRequest : NativeObject
    {
        internal TrackRequest(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Requested track name.</summary>
        public unsafe string Name
        {
            get
            {
                byte* name = null;
                Ffi.Check(NativeMethods.moq_track_request_name((void*)Handle, &name), "Name");
                return Ffi.ClaimString(name) ?? string.Empty;
            }
        }

        /// <summary>Accepts the request and returns a producer.</summary>
        public unsafe TrackProducer Accept(TrackOptions? options = null)
        {
            var opts = options ?? new TrackOptions();
            void* track = null;
            Ffi.Check(
                NativeMethods.moq_track_request_accept(
                    (void*)Handle,
                    opts.Priority,
                    opts.Ordered ? (byte)1 : (byte)0,
                    opts.LatencyMaxMs,
                    opts.Timescale,
                    &track),
                "Accept");
            return new TrackProducer((IntPtr)track);
        }

        /// <summary>Rejects the request.</summary>
        public unsafe void Abort(ushort errorCode)
        {
            Ffi.Check(NativeMethods.moq_track_request_abort((void*)Handle, errorCode), "Abort");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_track_request_free(handle);
        }
    }

    /// <summary>Publishes frames on a raw track.</summary>
    public sealed class TrackProducer : NativeObject
    {
        internal TrackProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Track name.</summary>
        public unsafe string Name
        {
            get
            {
                byte* name = null;
                Ffi.Check(NativeMethods.moq_track_producer_name((void*)Handle, &name), "Name");
                return Ffi.ClaimString(name) ?? string.Empty;
            }
        }

        /// <summary>Appends a new group.</summary>
        public unsafe GroupProducer AppendGroup()
        {
            void* group = null;
            Ffi.Check(NativeMethods.moq_track_producer_append_group((void*)Handle, &group), "AppendGroup");
            return new GroupProducer((IntPtr)group);
        }

        /// <summary>Writes a timestamped frame, copying the payload across the FFI boundary.</summary>
        public unsafe void WriteFrame(byte[] payload, ulong timestampUs)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_track_producer_write_frame((void*)Handle, null, UIntPtr.Zero, timestampUs), "WriteFrame");
                return;
            }

            fixed (byte* p = payload)
            {
                Ffi.Check(NativeMethods.moq_track_producer_write_frame((void*)Handle, p, (UIntPtr)(ulong)payload.Length, timestampUs), "WriteFrame");
            }
        }

        /// <summary>Appends a datagram and returns its sequence number.</summary>
        public unsafe ulong AppendDatagram(byte[] payload, ulong timestampUs)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            ulong sequence = 0;
            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_track_producer_append_datagram((void*)Handle, null, UIntPtr.Zero, timestampUs, &sequence), "AppendDatagram");
            }
            else
            {
                fixed (byte* p = payload)
                {
                    Ffi.Check(NativeMethods.moq_track_producer_append_datagram((void*)Handle, p, (UIntPtr)(ulong)payload.Length, timestampUs, &sequence), "AppendDatagram");
                }
            }

            return sequence;
        }

        /// <summary>Blocks until at least one subscriber is attached.</summary>
        public unsafe void Used()
        {
            Ffi.Check(NativeMethods.moq_track_producer_used((void*)Handle), "Used");
        }

        /// <summary>Blocks until no subscribers remain.</summary>
        public unsafe void Unused()
        {
            Ffi.Check(NativeMethods.moq_track_producer_unused((void*)Handle), "Unused");
        }

        /// <summary>Returns a local consumer of this track.</summary>
        public unsafe TrackConsumer Consume()
        {
            void* consumer = null;
            Ffi.Check(NativeMethods.moq_track_producer_consume((void*)Handle, &consumer), "Consume");
            return new TrackConsumer((IntPtr)consumer);
        }

        /// <summary>Finishes the track.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_track_producer_finish((void*)Handle), "Finish");
        }

        /// <summary>Aborts the track with an application error code.</summary>
        public unsafe void Abort(ushort errorCode)
        {
            Ffi.Check(NativeMethods.moq_track_producer_abort((void*)Handle, errorCode), "Abort");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_track_producer_free(handle);
        }
    }

    /// <summary>Publishes frames inside a single group.</summary>
    public sealed class GroupProducer : NativeObject
    {
        internal GroupProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Group sequence number.</summary>
        public unsafe ulong Sequence => NativeMethods.moq_group_producer_sequence((void*)Handle);

        /// <summary>Writes a timestamped frame into this group.</summary>
        public unsafe void WriteFrame(byte[] payload, ulong timestampUs)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_group_producer_write_frame((void*)Handle, null, UIntPtr.Zero, timestampUs), "WriteFrame");
                return;
            }

            fixed (byte* p = payload)
            {
                Ffi.Check(NativeMethods.moq_group_producer_write_frame((void*)Handle, p, (UIntPtr)(ulong)payload.Length, timestampUs), "WriteFrame");
            }
        }

        /// <summary>Finishes the group.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_group_producer_finish((void*)Handle), "Finish");
        }

        /// <summary>Returns a local consumer of this group.</summary>
        public unsafe GroupConsumer Consume()
        {
            void* consumer = null;
            Ffi.Check(NativeMethods.moq_group_producer_consume((void*)Handle, &consumer), "Consume");
            return new GroupConsumer((IntPtr)consumer);
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_group_producer_free(handle);
        }
    }

    /// <summary>Publishes already-encoded media frames.</summary>
    public sealed class MediaProducer : NativeObject
    {
        internal MediaProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Track name, when the media is a single elementary track.</summary>
        public unsafe string Name
        {
            get
            {
                byte* name = null;
                Ffi.Check(NativeMethods.moq_media_producer_name((void*)Handle, &name), "Name");
                return Ffi.ClaimString(name) ?? string.Empty;
            }
        }

        /// <summary>Decodes and publishes one encoded frame.</summary>
        public unsafe void WriteFrame(byte[] payload, ulong timestampUs)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_media_producer_write_frame((void*)Handle, null, UIntPtr.Zero, timestampUs), "WriteFrame");
                return;
            }

            fixed (byte* p = payload)
            {
                Ffi.Check(NativeMethods.moq_media_producer_write_frame((void*)Handle, p, (UIntPtr)(ulong)payload.Length, timestampUs), "WriteFrame");
            }
        }

        /// <summary>Finishes the media track or container.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_media_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_media_producer_free(handle);
        }
    }

    /// <summary>Publishes a streaming media bitstream.</summary>
    public sealed class MediaStreamProducer : NativeObject
    {
        internal MediaStreamProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Pushes bitstream bytes.</summary>
        public unsafe void Write(byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_media_stream_producer_write((void*)Handle, null, UIntPtr.Zero), "Write");
                return;
            }

            fixed (byte* p = payload)
            {
                Ffi.Check(NativeMethods.moq_media_stream_producer_write((void*)Handle, p, (UIntPtr)(ulong)payload.Length), "Write");
            }
        }

        /// <summary>Finishes the stream.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_media_stream_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_media_stream_producer_free(handle);
        }
    }

    /// <summary>Publishes JSON document snapshots.</summary>
    public sealed class JsonSnapshotProducer : NativeObject
    {
        internal JsonSnapshotProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Replaces the current snapshot with JSON text.</summary>
        public unsafe void Update(string json)
        {
            Require(json, nameof(json));
            using var utf = Utf8String.Create(json);
            Ffi.Check(NativeMethods.moq_json_snapshot_producer_update((void*)Handle, utf.Ptr), "Update");
        }

        /// <summary>Finishes the snapshot track.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_json_snapshot_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_json_snapshot_producer_free(handle);
        }
    }

    /// <summary>Publishes a JSON event stream.</summary>
    public sealed class JsonStreamProducer : NativeObject
    {
        internal JsonStreamProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Appends a JSON value.</summary>
        public unsafe void Append(string json)
        {
            Require(json, nameof(json));
            using var utf = Utf8String.Create(json);
            Ffi.Check(NativeMethods.moq_json_stream_producer_append((void*)Handle, utf.Ptr), "Append");
        }

        /// <summary>Finishes the stream track.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_json_stream_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_json_stream_producer_free(handle);
        }
    }

    /// <summary>Publishes PCM audio that the native library encodes.</summary>
    public sealed class AudioProducer : NativeObject
    {
        internal AudioProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Audio track name.</summary>
        public unsafe string Name
        {
            get
            {
                byte* name = null;
                Ffi.Check(NativeMethods.moq_audio_producer_name((void*)Handle, &name), "Name");
                return Ffi.ClaimString(name) ?? string.Empty;
            }
        }

        /// <summary>Writes one PCM frame. Treat the buffer as an untrusted bitstream.</summary>
        public unsafe void Write(byte[] payload, ulong timestampUs)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_audio_producer_write((void*)Handle, null, UIntPtr.Zero, timestampUs), "Write");
                return;
            }

            fixed (byte* p = payload)
            {
                Ffi.Check(NativeMethods.moq_audio_producer_write((void*)Handle, p, (UIntPtr)(ulong)payload.Length, timestampUs), "Write");
            }
        }

        /// <summary>Finishes the audio track.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_audio_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_audio_producer_free(handle);
        }
    }

    /// <summary>Publishes raw video that the native library encodes.</summary>
    public sealed class VideoProducer : NativeObject
    {
        internal VideoProducer(IntPtr handle)
            : base(handle)
        {
        }

        /// <summary>Video track name.</summary>
        public unsafe string Name
        {
            get
            {
                byte* name = null;
                Ffi.Check(NativeMethods.moq_video_producer_name((void*)Handle, &name), "Name");
                return Ffi.ClaimString(name) ?? string.Empty;
            }
        }

        /// <summary>Encodes and publishes one raw picture. Treat the buffer as untrusted pixel data.</summary>
        public unsafe void Write(byte[] payload, ulong timestampUs)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (payload.Length == 0)
            {
                Ffi.Check(NativeMethods.moq_video_producer_write((void*)Handle, null, UIntPtr.Zero, timestampUs), "Write");
                return;
            }

            fixed (byte* p = payload)
            {
                Ffi.Check(NativeMethods.moq_video_producer_write((void*)Handle, p, (UIntPtr)(ulong)payload.Length, timestampUs), "Write");
            }
        }

        /// <summary>Requests a keyframe (IDR) on the next encoded picture.</summary>
        public unsafe void Cut()
        {
            Ffi.Check(NativeMethods.moq_video_producer_cut((void*)Handle), "Cut");
        }

        /// <summary>Updates the target bitrate in bits per second.</summary>
        public unsafe void SetBitrate(ulong bitrate)
        {
            Ffi.Check(NativeMethods.moq_video_producer_set_bitrate((void*)Handle, bitrate), "SetBitrate");
        }

        /// <summary>Drains the encoder and finishes the track.</summary>
        public unsafe void Finish()
        {
            Ffi.Check(NativeMethods.moq_video_producer_finish((void*)Handle), "Finish");
        }

        /// <inheritdoc />
        protected override unsafe void Release(void* handle)
        {
            NativeMethods.moq_video_producer_free(handle);
        }
    }
}
