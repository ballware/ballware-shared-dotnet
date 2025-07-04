using Ballware.Shared.Authorization.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Authorization;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareSharedAuthorizationUtils(this IServiceCollection services, string tenantClaim, string userIdClaim, string rightClaim)
    {
        services.AddSingleton<IPrincipalUtils>(new DefaultPrincipalUtils(tenantClaim, userIdClaim, rightClaim));

        return services;
    }
}