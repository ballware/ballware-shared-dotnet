using System.Net;
using System.Text.Json;
using Ballware.Shared.Mcp.Endpoints.Configuration;
using Microsoft.AspNetCore.Http;

namespace Ballware.Shared.Mcp.Endpoints.Internal;

/// <summary>
/// Middleware that handles MCP OAuth 2.0 Protected Resource requirements:
/// 1. Serves the /.well-known/oauth-protected-resource metadata endpoint (RFC 9470)
/// 2. Enriches 401 responses with proper WWW-Authenticate Bearer challenge headers
///    including the resource_metadata parameter per MCP specification
/// </summary>
internal class McpOAuthProtectedResourceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly McpEndpointOptions _options;
    private readonly string _authorizationServerUri;
    private readonly string _wellKnownPath;

    public McpOAuthProtectedResourceMiddleware(
        RequestDelegate next,
        McpEndpointOptions options,
        string authorizationServerUri)
    {
        _next = next;
        _options = options;
        _authorizationServerUri = authorizationServerUri;
        _wellKnownPath = "/.well-known/oauth-protected-resource";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Handle the well-known metadata endpoint
        if (context.Request.Method == HttpMethods.Get &&
            context.Request.Path.Equals(_wellKnownPath, StringComparison.OrdinalIgnoreCase))
        {
            await HandleProtectedResourceMetadataAsync(context);
            return;
        }

        // Call the next middleware
        await _next(context);

        // Enrich 401 responses with proper WWW-Authenticate headers
        if (context.Response.StatusCode == (int)HttpStatusCode.Unauthorized)
        {
            EnrichUnauthorizedResponse(context);
        }
    }

    private async Task HandleProtectedResourceMetadataAsync(HttpContext context)
    {
        var metadata = new OAuthProtectedResourceMetadata
        {
            Resource = _options.ResourceUri,
            AuthorizationServers = [_authorizationServerUri],
            ScopesSupported = [_options.RequiredMcpScope],
            BearerMethodsSupported = ["header"]
        };

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "public, max-age=3600";

        await JsonSerializer.SerializeAsync(context.Response.Body, metadata);
    }

    private void EnrichUnauthorizedResponse(HttpContext context)
    {
        var resourceMetadataUrl = BuildResourceMetadataUrl(context);

        // Set the WWW-Authenticate header with Bearer scheme and resource_metadata parameter
        // per MCP specification for OAuth 2.0 protected resources
        context.Response.Headers["WWW-Authenticate"] =
            $"Bearer resource_metadata=\"{resourceMetadataUrl}\"";
    }

    private string BuildResourceMetadataUrl(HttpContext context)
    {
        var request = context.Request;
        return $"{request.Scheme}://{request.Host}{_wellKnownPath}";
    }
}
