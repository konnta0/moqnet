using MoqNet.Samples;

var pcmReceived = new TaskCompletionSource<byte[]>();
var jpegReceived = new TaskCompletionSource<byte[]>();

RelayHost? relay = null;
string url;
var relayUrl = args.FirstOrDefault(static a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
if (!string.IsNullOrEmpty(relayUrl))
{
    url = relayUrl;
    Console.WriteLine($"using existing relay {url}");
}
else
{
    relay = new RelayHost();
    var listen = await relay.StartAsync("127.0.0.1:0");
    var host = listen.Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal);
    url = $"https://{host}/";
    Console.WriteLine($"relay {listen}");
}

await using (var publisher = new LivePublisherSession())
await using (var subscriber = new LiveSubscriberSession())
{
    await publisher.StartAsync(url, disableTlsVerify: true);
    Console.WriteLine($"publisher connected h264={publisher.VideoEncoderAvailable}");

    subscriber.PcmReceived += payload => pcmReceived.TrySetResult(payload);
    subscriber.JpegReceived += payload => jpegReceived.TrySetResult(payload);
    await subscriber.StartAsync(url, disableTlsVerify: true);
    Console.WriteLine("subscriber connected");

    var pcm = new byte[SampleProtocol.AudioFrameSamples * sizeof(float)];
    var samples = new float[SampleProtocol.AudioFrameSamples];
    Array.Fill(samples, 0.25f);
    Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);

    var jpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAn/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAGcP//Z");
    var rgba = new byte[SampleProtocol.MaxRgbaBytes];
    Array.Fill(rgba, (byte)80);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    for (var i = 0; i < 40 && (!pcmReceived.Task.IsCompleted || !jpegReceived.Task.IsCompleted); i++)
    {
        publisher.WritePcm(pcm);
        publisher.WriteJpeg(jpeg);
        publisher.WriteRgba(rgba);
        await Task.Delay(50, cts.Token);
    }

    var gotPcm = await pcmReceived.Task.WaitAsync(cts.Token);
    var gotJpeg = await jpegReceived.Task.WaitAsync(cts.Token);
    if (gotPcm.Length != pcm.Length)
    {
        throw new InvalidOperationException($"unexpected pcm length {gotPcm.Length}");
    }

    if (gotJpeg[0] != 0xFF || gotJpeg[1] != 0xD8)
    {
        throw new InvalidOperationException("preview was not JPEG");
    }

    Console.WriteLine($"ok pcm={gotPcm.Length} jpeg={gotJpeg.Length} h264={publisher.VideoEncoderAvailable}");
}

if (relay is not null)
{
    await relay.DisposeAsync();
}
