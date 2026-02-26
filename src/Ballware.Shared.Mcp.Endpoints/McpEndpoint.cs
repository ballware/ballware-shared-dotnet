using Ballware.Shared.Mcp.Endpoints.Configuration;
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
            app.MapMcp(basePath).RequireAuthorization(builder =>
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
}