using MoqNet.Native;
using Xunit;

namespace MoqNet.Native.Tests;

public class HandleTests
{
    [Fact]
    public void OriginProducer_CreateAndDispose_Succeeds()
    {
        using var origin = new OriginProducer();
        Assert.False(origin.IsDisposed);
        origin.Dispose();
        Assert.True(origin.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => origin.Consume());
    }

    [Fact]
    public void Client_CreateAndDispose_Succeeds()
    {
        using var client = new Client();
        Assert.False(client.IsDisposed);
    }

    [Fact]
    public void Client_InvalidUrl_ThrowsMoqException()
    {
        using var client = new Client();
        var ex = Assert.Throws<MoqException>(() => client.Connect("not a url"));
        Assert.False(string.IsNullOrEmpty(ex.Message));
    }

    [Fact]
    public void Client_InvalidBind_ThrowsMoqException()
    {
        using var client = new Client();
        Assert.Throws<MoqException>(() => client.SetBind("not-an-address"));
    }

    [Fact]
    public void Client_NullUrl_ThrowsArgumentNullException()
    {
        using var client = new Client();
        Assert.Throws<ArgumentNullException>(() => client.Connect(null!));
    }

    [Fact]
    public void FromLastError_WithoutNativeCall_UsesBaseMessage()
    {
        var ex = MoqException.FromLastError("nothing");
        Assert.StartsWith("nothing", ex.Message);
    }

    [Fact]
    public void BroadcastProducer_PublishAndConsumeTrack_RoundTripsFrame()
    {
        using var broadcast = BroadcastProducer.Create();
        using var track = broadcast.PublishTrack("status");
        Assert.Equal("status", track.Name);

        var payload = new byte[] { 1, 2, 3, 4 };
        using var consumer = track.Consume();
        track.WriteFrame(payload, timestampUs: 42);
        track.Finish();

        var frame = consumer.ReadFrame();
        Assert.NotNull(frame);
        Assert.Equal(42UL, frame!.Value.TimestampUs);
        Assert.Equal(payload, frame.Value.Payload);

        Assert.Null(consumer.ReadFrame());
    }

    [Fact]
    public void OriginProducer_CreateBroadcast_ReturnsProducer()
    {
        using var origin = new OriginProducer();
        using var broadcast = origin.CreateBroadcast("demos/test");
        using var track = broadcast.PublishTrack("ping");
        Assert.Equal("ping", track.Name);
        track.WriteFrame(System.Text.Encoding.UTF8.GetBytes("hello"), 0);
        track.Finish();
    }

    [Fact]
    public void Server_InvalidBind_ThrowsMoqException()
    {
        using var server = new Server();
        Assert.Throws<MoqException>(() => server.SetBind("nope"));
    }

    [Fact]
    public void Client_GarbageFingerprint_ConnectThrows()
    {
        using var client = new Client();
        client.SetTlsDisableVerify(true);
        client.SetTlsFingerprints("not-hex");
        Assert.Throws<MoqException>(() => client.Connect("https://127.0.0.1:1/"));
    }
}
