using Ballware.Shared.Mcp.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Mcp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareMcpTools(this IServiceCollection services, Action<IServiceProvider, IToolRegistry> configure)
    {
        var toolRegistry = new ToolRegistry();
        
        services.AddSingleton<IToolRegistry>(toolRegistry);
        services.AddHostedService<ToolRegistryInitializer>((serviceProvider) =>
            new ToolRegistryInitializer(serviceProvider, configure));
        
        return services;
    }
}