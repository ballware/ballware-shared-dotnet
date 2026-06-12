using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ballware.Shared.Mcp.Internal;

internal class ToolRegistryInitializer(IServiceProvider serviceProvider, Action<IServiceProvider, IToolRegistry> configure) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        
        var toolRegistry = scope.ServiceProvider.GetRequiredService<IToolRegistry>();

        configure(scope.ServiceProvider, toolRegistry);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}