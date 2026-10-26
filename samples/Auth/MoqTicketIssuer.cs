using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MoqNet.Samples.Auth;

/// <summary>Issues short-lived HS256 tokens understood by moq-relay.</summary>
public sealed class MoqTicketIssuer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] _secret;
    private readonly MoqAuthOptions _options;

    /// <summary>Loads the relay JWK and validates the sample configuration.</summary>
    public MoqTicketIssuer(IOptions<MoqAuthOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        if (_options.TicketLifetimeSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException("MoqAuth:TicketLifetimeSeconds must be between 1 and 3600.");
        }

        var keyPath = Path.IsPathRooted(_options.KeyFile)
            ? _options.KeyFile
            : Path.Combine(environment.ContentRootPath, _options.KeyFile);
        var jwk = JsonSerializer.Deserialize<SymmetricJwk>(File.ReadAllText(keyPath), JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse JWK at {keyPath}.");

        if (!string.Equals(jwk.KeyType, "oct", StringComparison.Ordinal) ||
            !string.Equals(jwk.Algorithm, "HS256", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The sample requires an oct/HS256 JWK.");
        }

        _secret = Base64UrlDecode(jwk.Key);
        if (_secret.Length < 32)
        {
            throw new InvalidOperationException("The HS256 secret must contain at least 32 bytes.");
        }
    }

    /// <summary>Issues a path-scoped publisher or subscriber ticket.</summary>
    public TicketResponse Issue(string userId, TicketRequest request)
    {
        var safeUser = RequirePathSegment(userId, nameof(userId));
        var room = RequirePathSegment(request.Room, nameof(request.Room));
        var role = request.Role?.Trim().ToLowerInvariant();
        if (role is not ("publisher" or "subscriber"))
        {
            throw new ArgumentException("Role must be either 'publisher' or 'subscriber'.", nameof(request.Role));
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt.AddSeconds(_options.TicketLifetimeSeconds);
        var root = $"rooms/{room}";
        var claims = new MoqClaims
        {
            Root = root,
            Publish = role == "publisher" ? [safeUser] : null,
            Subscribe = role == "subscriber" ? [""] : null,
            IssuedAt = issuedAt.ToUnixTimeSeconds(),
            ExpiresAt = expiresAt.ToUnixTimeSeconds()
        };

        var token = Sign(claims);
        var relayUri = new Uri(_options.RelayUrl, UriKind.Absolute);
        var builder = new UriBuilder(relayUri)
        {
            Path = CombinePath(relayUri.AbsolutePath, root),
            Query = $"jwt={Uri.EscapeDataString(token)}"
        };

        return new TicketResponse(
            builder.Uri.AbsoluteUri,
            token,
            expiresAt,
            root,
            role,
            role == "publisher" ? safeUser : null);
    }

    private string Sign(MoqClaims claims)
    {
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(
            new JwtHeader { Algorithm = "HS256", Type = "JWT" }, JsonOptions));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions));
        var unsignedToken = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(_secret, Encoding.ASCII.GetBytes(unsignedToken));
        return $"{unsignedToken}.{Base64UrlEncode(signature)}";
    }

    private static string RequirePathSegment(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            value.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new ArgumentException("Use 1-64 ASCII letters, digits, '-' or '_'.", parameterName);
        }

        return value;
    }

    private static string CombinePath(string basePath, string childPath) =>
        $"{basePath.TrimEnd('/')}/{childPath.TrimStart('/')}";

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException("Invalid base64url value.")
        };
        return Convert.FromBase64String(padded);
    }

    private sealed class SymmetricJwk
    {
        [JsonPropertyName("kty")]
        public string KeyType { get; init; } = string.Empty;

        [JsonPropertyName("alg")]
        public string Algorithm { get; init; } = string.Empty;

        [JsonPropertyName("k")]
        public string Key { get; init; } = string.Empty;
    }

    private sealed class JwtHeader
    {
        [JsonPropertyName("alg")]
        public string Algorithm { get; init; } = string.Empty;

        [JsonPropertyName("typ")]
        public string Type { get; init; } = string.Empty;
    }

    private sealed class MoqClaims
    {
        [JsonPropertyName("root")]
        public string Root { get; init; } = string.Empty;

        [JsonPropertyName("put")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Publish { get; init; }

        [JsonPropertyName("get")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Subscribe { get; init; }

        [JsonPropertyName("iat")]
        public long IssuedAt { get; init; }

        [JsonPropertyName("exp")]
        public long ExpiresAt { get; init; }
    }
}

/// <summary>Requested room and role for a development ticket.</summary>
public sealed record TicketRequest(string? Room, string? Role);

/// <summary>A short-lived MoQ connection ticket.</summary>
public sealed record TicketResponse(
    string RelayUrl,
    string Token,
    DateTimeOffset ExpiresAt,
    string Root,
    string Role,
    string? BroadcastPath);
