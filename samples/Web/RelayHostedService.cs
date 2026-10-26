using Microsoft.Extensions.Options;
using MoqNet.Samples;

namespace MoqNet.Samples.Web;

/// <summary>Optionally hosts <see cref="RelayHost"/> for the web sample.</summary>
public sealed class RelayHostedService : IHostedService, IAsyncDisposable
{
    private readonly ILogger<RelayHostedService> _logger;
    private readonly MoqNetOptions _options;
    private RelayHost? _relay;

    /// <summary>Creates the hosted relay wrapper.</summary>
    public RelayHostedService(IOptions<MoqNetOptions> options, ILogger<RelayHostedService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Bound address after start, when this process owns the relay.</summary>
    public string? ListenAddress { get; private set; }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.HostRelay)
        {
            _logger.LogInformation("HostRelay is false; using existing relay at {Url}", _options.RelayUrl);
            return;
        }

        _relay = new RelayHost();
        ListenAddress = await _relay.StartAsync(_options.Bind, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Sample relay listening on {Address}", ListenAddress);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_relay is not null)
        {
            await _relay.DisposeAsync().ConfigureAwait(false);
            _relay = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_relay is not null)
        {
            await _relay.DisposeAsync().ConfigureAwait(false);
        }
    }
}
