using Ballware.Shared.Mcp.Endpoints.Configuration;
using Ballware.Shared.Mcp.Endpoints.Internal;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Ballware.Shared.Mcp.Endpoints;

public static class McpEndpoint
{
    public static IEndpointRouteBuilder MapBallwareUserMcpEndpoint(this IEndpointRouteBuilder app, string basePath, McpEndpointOptions options)
    {
        if (options is { Enabled: true })
        {
            app.MapMcp(basePath)
                .RequireAuthorization(builder =>
            {
                builder.AuthenticationSchemes = [JwtBearerDefaults.AuthenticationScheme];
                builder.RequireAssertion(context =>
                    context.User
                        .Claims
                        .Where(c => "scope" == c.Type)
                        .SelectMany(c => c.Value.Split(' '))
                        .Any(s => s.Equals(options.RequiredMcpScope, StringComparison.Ordinal)));
            });    
        }
        
        return app;
    }
    
    /// <summary>
    /// Adds the MCP OAuth 2.0 Protected Resource middleware to the application pipeline.
    /// This must be called before UseAuthentication/UseAuthorization to ensure:
    /// 1. The /.well-known/oauth-protected-resource endpoint is accessible without authentication
    /// 2. 401 responses include proper WWW-Authenticate headers with resource_metadata
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="options">The MCP endpoint options.</param>
    /// <param name="authorizationServerUri">The URI of the external authorization server (IdP) for OAuth 2.0.</param>
    public static IApplicationBuilder UseBallwareMcpOAuthProtectedResource(this IApplicationBuilder app, McpEndpointOptions options, string authorizationServerUri)
    {
        if (options is { Enabled: true })
        {
            app.UseMiddleware<McpOAuthProtectedResourceMiddleware>(options, authorizationServerUri);
        }
        
        return app;
    }
}