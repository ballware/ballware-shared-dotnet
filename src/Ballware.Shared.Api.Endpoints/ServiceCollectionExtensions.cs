using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Api.Endpoints;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareSharedApiDependencies(this IServiceCollection services)
    {
        services.AddScoped<EditingEndpointBuilderFactory>();

        return services;
    }
}