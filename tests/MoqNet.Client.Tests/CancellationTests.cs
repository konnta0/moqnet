using MoqNet.Client;
using MoqNet.Native;
using Xunit;

namespace MoqNet.Client.Tests;

public class CancellationTests
{
    [Fact]
    public async Task ConnectAsync_CanceledToken_Throws()
    {
        using var client = new Client();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync("https://127.0.0.1:1/", cts.Token));
    }

    [Fact]
    public async Task ConnectAsync_InvalidUrl_ThrowsMoqException()
    {
        using var client = new Client();
        await Assert.ThrowsAsync<MoqException>(() => client.ConnectAsync("not a url"));
    }

    [Fact]
    public async Task TrackProducer_LocalRoundTrip_ReadFrameAsync()
    {
        using var broadcast = BroadcastProducer.Create();
        using var track = broadcast.PublishTrack("status");
        using var consumer = track.Consume();
        var payload = new byte[] { 9, 8, 7 };
        track.WriteFrame(payload, 7);
        track.Finish();
        var frame = await consumer.ReadFrameAsync();
        Assert.NotNull(frame);
        Assert.Equal(payload, frame!.Value.Payload);
        Assert.Equal(7UL, frame.Value.TimestampUs);
    }

    [Fact]
    public async Task ReadFrameAsync_AlreadyCanceledToken_Throws()
    {
        using var broadcast = BroadcastProducer.Create();
        using var track = broadcast.PublishTrack("live");
        using var consumer = track.Consume();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumer.ReadFrameAsync(cts.Token));
    }

    [Fact]
    public async Task OptionalRelayConnect_WhenConfigured()
    {
        var url = Environment.GetEnvironmentVariable("MOQNET_RELAY_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        using var client = new Client();
        client.SetTlsDisableVerify(true);
        await using var session = await client.ConnectAsync(url);
        Assert.False(string.IsNullOrEmpty(await session.StatsJsonAsync()));
    }
}
