namespace MoqNet.Samples.Auth;

/// <summary>Configuration for the development MoQ ticket issuer.</summary>
public sealed class MoqAuthOptions
{
    public const string SectionName = "MoqAuth";

    /// <summary>Public relay URL without a room path.</summary>
    public string RelayUrl { get; set; } = "https://localhost:4443/";

    /// <summary>JWK shared with the local moq-relay process.</summary>
    public string KeyFile { get; set; } = "dev-key.jwk";

    /// <summary>Lifetime of an issued development ticket.</summary>
    public int TicketLifetimeSeconds { get; set; } = 300;
}
