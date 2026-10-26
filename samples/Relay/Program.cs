using MoqNet.Samples;

var bind = args.Length > 0 ? args[0] : SampleProtocol.DefaultBind;

await using var relay = new RelayHost();
var address = await relay.StartAsync(bind);
Console.WriteLine($"MoqNet sample relay listening on {address}");
Console.WriteLine($"Connect publishers and subscribers to {SampleProtocol.DefaultRelayUrl}");
Console.WriteLine("Development TLS is self-signed. Clients should call SetTlsDisableVerify(true).");
Console.WriteLine("Press Ctrl+C to stop.");

var stopping = new TaskCompletionSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopping.TrySetResult();
};

await stopping.Task;
