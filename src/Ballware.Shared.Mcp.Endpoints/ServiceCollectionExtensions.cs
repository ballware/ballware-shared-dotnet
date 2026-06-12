using Ballware.Shared.Mcp.Endpoints.Configuration;
using Ballware.Shared.Mcp.Endpoints.Internal;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Ballware.Shared.Mcp.Endpoints;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareMcpEndpoint(this IServiceCollection services, McpEndpointOptions options)
    {
        if (options.Enabled)
        {
            services.AddMcpServer(builder =>
            {
                builder.ScopeRequests = true;
                
                builder.Handlers = new McpServerHandlers()
                {
                    ListToolsHandler = ToolRegistryRequestHandlers.ListToolsAsync,
                    CallToolHandler = ToolRegistryRequestHandlers.CallToolAsync
                };
            }).WithHttpTransport();
        }

        return services;
    }
}