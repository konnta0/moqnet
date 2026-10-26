namespace MoqNet.Samples.Web;

/// <summary>Relay and client settings for the Blazor sample.</summary>
public sealed class MoqNetOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MoqNet";

    /// <summary>QUIC bind address when <see cref="HostRelay"/> is true.</summary>
    public string Bind { get; set; } = Samples.SampleProtocol.DefaultBind;

    /// <summary>MoQ URL used by the publish and subscribe pages.</summary>
    public string RelayUrl { get; set; } = Samples.SampleProtocol.DefaultRelayUrl;

    /// <summary>When true, this process starts an in-process relay.</summary>
    public bool HostRelay { get; set; } = true;

    /// <summary>Development only. Disables TLS verification for the localhost self-signed cert.</summary>
    public bool TlsDisableVerify { get; set; } = true;
}
