using Ballware.Shared.Authorization.Jint.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Authorization.Jint;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareSharedJintRightsChecker(this IServiceCollection services)
    {
        services.AddSingleton<ITenantRightsChecker, JavascriptTenantRightsChecker>();
        services.AddSingleton<IEntityRightsChecker, JavascriptEntityRightsChecker>();

        return services;
    }

}