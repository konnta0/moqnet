using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MoqNet.Native;
using MoqNet.Unity;
using UnityEngine;

namespace MoqNet.Samples.Unity
{
    public enum SampleRole
    {
        Publisher,
        Subscriber
    }

    /// <summary>
    /// Publisher or subscriber for the live sample. Add to a GameObject, or press Play and a host is created.
    /// </summary>
    public sealed class LiveSample : MonoBehaviour
    {
        [SerializeField] private SampleRole _role = SampleRole.Subscriber;
        [SerializeField] private string _relayUrl = SampleProtocol.DefaultRelayUrl;
        [SerializeField] private bool _disableTlsVerify = true;

        private readonly PcmRingBuffer _playback = new PcmRingBuffer(SampleProtocol.AudioFrameSamples * 32);
        private readonly float[] _captureScratch = new float[4096];
        private readonly float[] _resampledScratch = new float[4096];
        private readonly float[] _pending = new float[SampleProtocol.AudioFrameSamples * 2];
        private readonly byte[] _pcmBytes = new byte[SampleProtocol.AudioFrameSamples * 4];
        private readonly byte[] _rgba = new byte[SampleProtocol.RgbaBytes];
        private readonly Color32[] _scaledPixels = new Color32[SampleProtocol.VideoWidth * SampleProtocol.VideoHeight];
        private readonly float[] _filterScratch = new float[2048];

        private CancellationTokenSource _cts;
        private Client _client;
        private Session _session;
        private BroadcastProducer _broadcast;
        private AudioProducer _audio;
        private VideoProducer _video;
        private TrackProducer _preview;
        private BroadcastConsumer _consumeBroadcast;
        private AudioConsumer _audioConsumer;
        private TrackConsumer _previewConsumer;

        private WebCamTexture _webCam;
        private Color32[] _webCamPixels;
        private Texture2D _previewTexture;
        private AudioClip _micClip;
        private string _micDevice;
        private int _micPosition;
        private int _pendingCount;
        private float _videoAccumulator;
        private float _tonePhase;
        private string _status = "Idle. Press Start.";
        private bool _running;
        private bool _starting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<LiveSample>() != null)
            {
                return;
            }

            var host = new GameObject("MoqNet Live Sample");
            host.AddComponent<LiveSample>();
            host.AddComponent<AudioSource>();
        }

        private void Awake()
        {
            var source = GetComponent<AudioSource>();
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = true;
            source.loop = true;
            source.clip = AudioClip.Create("moq-playback", (int)SampleProtocol.AudioSampleRate, 1, (int)SampleProtocol.AudioSampleRate, true, static _ => { });
            if (!source.isPlaying)
            {
                source.Play();
            }

            _previewTexture = new Texture2D(SampleProtocol.VideoWidth, SampleProtocol.VideoHeight, TextureFormat.RGBA32, false);
        }

        private void Update()
        {
            if (!_running || _role != SampleRole.Publisher)
            {
                return;
            }

            PumpMicrophone();
            _videoAccumulator += Time.deltaTime;
            if (_videoAccumulator >= 1f / SampleProtocol.VideoFps)
            {
                _videoAccumulator = 0f;
                CaptureVideoFrame();
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_role != SampleRole.Subscriber)
            {
                return;
            }

            int frames = data.Length / channels;
            int got = _playback.Read(_filterScratch, 0, frames);
            for (int i = 0; i < frames; i++)
            {
                float sample = i < got ? _filterScratch[i] : 0f;
                for (int c = 0; c < channels; c++)
                {
                    data[(i * channels) + c] = sample;
                }
            }
        }

        private void OnGUI()
        {
            const int width = 420;
            GUILayout.BeginArea(new Rect(16, 16, width, 280), GUI.skin.window);
            GUILayout.Label("MoqNet live sample");
            GUILayout.Label(_role == SampleRole.Publisher ? "Role: Publisher" : "Role: Subscriber");
            if (GUILayout.Button(_role == SampleRole.Publisher ? "Switch to Subscriber" : "Switch to Publisher"))
            {
                if (!_running && !_starting)
                {
                    _role = _role == SampleRole.Publisher ? SampleRole.Subscriber : SampleRole.Publisher;
                }
            }

            GUILayout.Label("Relay URL");
            _relayUrl = GUILayout.TextField(_relayUrl ?? string.Empty);
            GUILayout.Label(_status);
            if (!_running)
            {
                if (GUILayout.Button(_starting ? "Starting..." : "Start") && !_starting)
                {
                    StartSession().Forget();
                }
            }
            else if (GUILayout.Button("Stop"))
            {
                StopSession();
            }

            GUILayout.EndArea();

            if (_previewTexture != null)
            {
                GUI.DrawTexture(new Rect(16, 310, 320, 180), _previewTexture, ScaleMode.StretchToFill, false);
            }
        }

        private void OnDestroy()
        {
            StopSession();
            if (_previewTexture != null)
            {
                Destroy(_previewTexture);
            }

        }

        private async UniTaskVoid StartSession()
        {
            _starting = true;
            _status = "Connecting...";
            try
            {
                _cts = new CancellationTokenSource();
                var ct = _cts.Token;
                _client = new Client();
                if (_disableTlsVerify)
                {
                    _client.SetTlsDisableVerify(true);
                }

                _session = await _client.ConnectAsync(_relayUrl, ct);
                if (_role == SampleRole.Publisher)
                {
                    StartPublisher();
                }
                else
                {
                    await StartSubscriberAsync(ct);
                }

                _running = true;
            }
            catch (Exception ex)
            {
                _status = "Failed: " + ex.Message;
                StopSession();
            }
            finally
            {
                _starting = false;
            }
        }

        private void StartPublisher()
        {
            _broadcast = _session.Publisher.CreateBroadcast(SampleProtocol.BroadcastPath);
            _audio = _broadcast.PublishAudio(
                SampleProtocol.AudioTrack,
                AudioFormat.F32,
                SampleProtocol.AudioSampleRate,
                SampleProtocol.AudioChannels,
                -1,
                SampleProtocol.AudioFrameDurationMs);
            _preview = _broadcast.PublishTrack(SampleProtocol.PreviewTrack);
            try
            {
                _video = _broadcast.PublishVideo(
                    VideoPixelFormat.Rgba,
                    (uint)SampleProtocol.VideoWidth,
                    (uint)SampleProtocol.VideoHeight,
                    (uint)SampleProtocol.VideoFps,
                    VideoCodec.H264,
                    "video",
                    250000,
                    15,
                    VideoEncoderKind.Software);
            }
            catch (MoqException)
            {
                _video = null;
            }

            StartMicrophone();
            StartCamera();
            _status = _video != null ? "Publishing (H.264 + Opus + JPEG)." : "Publishing (Opus + JPEG).";
        }

        private async UniTask StartSubscriberAsync(CancellationToken cancellationToken)
        {
            _consumeBroadcast = await _session.Consumer.RequestBroadcastAsync(SampleProtocol.BroadcastPath, cancellationToken);
            _audioConsumer = await _consumeBroadcast.SubscribeAudioAsync(
                SampleProtocol.AudioTrack,
                SampleProtocol.AudioCodec,
                SampleProtocol.AudioSampleRate,
                SampleProtocol.AudioChannels,
                AudioFormat.F32,
                200,
                cancellationToken);
            _previewConsumer = await _consumeBroadcast.SubscribeTrackAsync(SampleProtocol.PreviewTrack, null, cancellationToken);
            ReadAudioLoop(cancellationToken).Forget();
            ReadPreviewLoop(cancellationToken).Forget();
            _status = "Subscribed. Waiting for media.";
        }

        private async UniTaskVoid ReadAudioLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await _audioConsumer.NextAsync(cancellationToken);
                    if (frame == null)
                    {
                        return;
                    }

                    var payload = frame.Value.Payload;
                    if (payload == null || payload.Length < 4 || (payload.Length % 4) != 0)
                    {
                        continue;
                    }

                    int samples = payload.Length / 4;
                    if (samples > _captureScratch.Length)
                    {
                        continue;
                    }

                    Buffer.BlockCopy(payload, 0, _captureScratch, 0, payload.Length);
                    _playback.Write(_captureScratch, 0, samples);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (MoqException)
            {
            }
        }

        private async UniTaskVoid ReadPreviewLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var frame = await _previewConsumer.ReadFrameAsync(cancellationToken);
                    if (frame == null)
                    {
                        return;
                    }

                    var payload = frame.Value.Payload;
                    if (payload == null || payload.Length < 3 || payload.Length > SampleProtocol.MaxPreviewBytes)
                    {
                        continue;
                    }

                    await UniTask.SwitchToMainThread(cancellationToken);
                    _previewTexture.LoadImage(payload, false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (MoqException)
            {
            }
        }

        private void StartMicrophone()
        {
            if (Microphone.devices.Length == 0)
            {
                _micDevice = null;
                return;
            }

            _micDevice = Microphone.devices[0];
            _micClip = Microphone.Start(_micDevice, true, 1, (int)SampleProtocol.AudioSampleRate);
            _micPosition = 0;
        }

        private void StartCamera()
        {
            if (WebCamTexture.devices.Length == 0)
            {
                return;
            }

            _webCam = new WebCamTexture(WebCamTexture.devices[0].name, SampleProtocol.VideoWidth, SampleProtocol.VideoHeight, SampleProtocol.VideoFps);
            _webCam.Play();
        }

        private void PumpMicrophone()
        {
            if (_audio == null)
            {
                return;
            }

            if (_micClip == null || string.IsNullOrEmpty(_micDevice))
            {
                WriteToneFrame();
                return;
            }

            int position = Microphone.GetPosition(_micDevice);
            if (position < 0 || _micClip.samples == 0)
            {
                return;
            }

            int available = position - _micPosition;
            if (available < 0)
            {
                available += _micClip.samples;
            }

            if (available == 0)
            {
                return;
            }

            if (available > _captureScratch.Length)
            {
                available = _captureScratch.Length;
            }

            _micClip.GetData(_captureScratch, _micPosition);
            _micPosition = (_micPosition + available) % _micClip.samples;
            if (_micClip.frequency == (int)SampleProtocol.AudioSampleRate)
            {
                AppendPcm(_captureScratch, available);
                return;
            }

            int resampled = ResampleLinear(_captureScratch, available, _micClip.frequency, (int)SampleProtocol.AudioSampleRate, _resampledScratch);
            AppendPcm(_resampledScratch, resampled);
        }

        private void WriteToneFrame()
        {
            for (int i = 0; i < SampleProtocol.AudioFrameSamples; i++)
            {
                _resampledScratch[i] = Mathf.Sin(_tonePhase) * 0.12f;
                _tonePhase += 2f * Mathf.PI * 440f / SampleProtocol.AudioSampleRate;
            }

            AppendPcm(_resampledScratch, SampleProtocol.AudioFrameSamples);
        }

        private void AppendPcm(float[] samples, int length)
        {
            int offset = 0;
            while (offset < length)
            {
                int take = Math.Min(SampleProtocol.AudioFrameSamples - _pendingCount, length - offset);
                Array.Copy(samples, offset, _pending, _pendingCount, take);
                _pendingCount += take;
                offset += take;
                if (_pendingCount == SampleProtocol.AudioFrameSamples)
                {
                    Buffer.BlockCopy(_pending, 0, _pcmBytes, 0, _pcmBytes.Length);
                    _audio.Write(_pcmBytes, SampleProtocol.TimestampUs());
                    _pendingCount = 0;
                }
            }
        }

        private void CaptureVideoFrame()
        {
            if (_webCam != null && _webCam.didUpdateThisFrame && _webCam.width > 16)
            {
                if (_webCamPixels == null || _webCamPixels.Length != _webCam.width * _webCam.height)
                {
                    _webCamPixels = new Color32[_webCam.width * _webCam.height];
                }

                _webCam.GetPixels32(_webCamPixels);
                ScaleNearest(_webCamPixels, _webCam.width, _webCam.height, _scaledPixels, SampleProtocol.VideoWidth, SampleProtocol.VideoHeight);
            }
            else
            {
                FillGenerated(_scaledPixels, Time.realtimeSinceStartup);
            }

            for (int i = 0; i < _scaledPixels.Length; i++)
            {
                int o = i * 4;
                Color32 c = _scaledPixels[i];
                _rgba[o] = c.r;
                _rgba[o + 1] = c.g;
                _rgba[o + 2] = c.b;
                _rgba[o + 3] = c.a;
            }

            _previewTexture.SetPixels32(_scaledPixels);
            _previewTexture.Apply(false, false);
            if (_video != null)
            {
                _video.Write(_rgba, SampleProtocol.TimestampUs());
            }

            if (_preview != null)
            {
                byte[] jpeg = _previewTexture.EncodeToJPG(55);
                if (jpeg != null && jpeg.Length > 2 && jpeg.Length <= SampleProtocol.MaxPreviewBytes)
                {
                    _preview.WriteFrame(jpeg, SampleProtocol.TimestampUs());
                }
            }
        }

        private static int ResampleLinear(float[] source, int length, int fromRate, int toRate, float[] destination)
        {
            if (fromRate <= 0 || toRate <= 0 || length <= 0)
            {
                return 0;
            }

            int output = Math.Min(destination.Length, Math.Max(1, (int)((long)length * toRate / fromRate)));
            for (int i = 0; i < output; i++)
            {
                float src = i * (fromRate / (float)toRate);
                int i0 = Math.Min(length - 1, (int)src);
                int i1 = Math.Min(length - 1, i0 + 1);
                float f = src - i0;
                destination[i] = source[i0] + ((source[i1] - source[i0]) * f);
            }

            return output;
        }

        private static void ScaleNearest(Color32[] src, int sw, int sh, Color32[] dst, int dw, int dh)
        {
            for (int y = 0; y < dh; y++)
            {
                int sy = y * sh / dh;
                for (int x = 0; x < dw; x++)
                {
                    dst[(y * dw) + x] = src[(sy * sw) + (x * sw / dw)];
                }
            }
        }

        private static void FillGenerated(Color32[] pixels, float time)
        {
            int width = SampleProtocol.VideoWidth;
            int bar = ((int)(time * 80f) % width + width) % width;
            for (int y = 0; y < SampleProtocol.VideoHeight; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte v = (byte)((x * 255) / width);
                    pixels[(y * width) + x] = x >= bar && x < bar + 18
                        ? new Color32(20, 20, 20, 255)
                        : new Color32(v, (byte)(180 - (y / 2)), 70, 255);
                }
            }
        }

        private void StopSession()
        {
            _running = false;
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (!string.IsNullOrEmpty(_micDevice))
            {
                Microphone.End(_micDevice);
            }

            _micDevice = null;
            _micClip = null;
            if (_webCam != null)
            {
                _webCam.Stop();
                Destroy(_webCam);
                _webCam = null;
            }

            DisposeQuiet(_audio);
            DisposeQuiet(_video);
            DisposeQuiet(_preview);
            DisposeQuiet(_broadcast);
            DisposeQuiet(_audioConsumer);
            DisposeQuiet(_previewConsumer);
            DisposeQuiet(_consumeBroadcast);
            DisposeQuiet(_session);
            DisposeQuiet(_client);
            _audio = null;
            _video = null;
            _preview = null;
            _broadcast = null;
            _audioConsumer = null;
            _previewConsumer = null;
            _consumeBroadcast = null;
            _session = null;
            _client = null;
            _playback.Clear();
            _pendingCount = 0;
            if (_status != null && _status.StartsWith("Failed", StringComparison.Ordinal) == false)
            {
                _status = "Stopped.";
            }
        }

        private static void DisposeQuiet(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (MoqException)
            {
            }
        }
    }
}
