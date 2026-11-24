using Ballware.Shared.Web.Session.Configuration;
using Ballware.Shared.Web.Session.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Web.Session;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareWebSession(this IServiceCollection services, WebSessionOptions options)
    {
        services.AddSingleton(options);
        services.AddDataProtection();
        services.AddSingleton<ISessionPrincipalProvider, SessionPrincipalProvider>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, SessionPrincipalAuthenticationHandler>(SessionAuthenticationSchemes.DistributedSession, null);

        if (options.EnableSession)
        {
            services.AddSession();
        }

        return services;
    }
}
