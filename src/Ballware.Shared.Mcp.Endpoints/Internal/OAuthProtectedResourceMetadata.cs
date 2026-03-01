using System.Text.Json.Serialization;

namespace Ballware.Shared.Mcp.Endpoints.Internal;

/// <summary>
/// Represents the OAuth 2.0 Protected Resource Metadata document (RFC 9470).
/// This is served at /.well-known/oauth-protected-resource to allow MCP clients
/// to discover the authorization requirements of the MCP server.
/// </summary>
internal class OAuthProtectedResourceMetadata
{
    /// <summary>
    /// The resource identifier for this protected resource.
    /// </summary>
    [JsonPropertyName("resource")]
    public required string Resource { get; set; }

    /// <summary>
    /// Array of authorization server URIs that can be used to obtain access tokens
    /// for this protected resource.
    /// </summary>
    [JsonPropertyName("authorization_servers")]
    public required string[] AuthorizationServers { get; set; }

    /// <summary>
    /// Array of OAuth 2.0 scope values that are used at this protected resource.
    /// </summary>
    [JsonPropertyName("scopes_supported")]
    public required string[] ScopesSupported { get; set; }

    /// <summary>
    /// Array of methods supported for sending bearer access tokens to this resource.
    /// Typically includes "header" for the Authorization header.
    /// </summary>
    [JsonPropertyName("bearer_methods_supported")]
    public required string[] BearerMethodsSupported { get; set; }
}

