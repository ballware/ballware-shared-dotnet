using Ballware.Shared.Web.Session.Configuration;
using Ballware.Shared.Web.Session.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Session;

public static class WebApplicationExtensions
{
    public static WebApplication UseBallwareWebSession(this WebApplication app)
    {
        var options =
            app.Services.GetService<WebSessionOptions>()
            ?? app.Services.GetRequiredService<IOptions<WebSessionOptions>>().Value;

        if (options.EnableSession)
        {
            app.UseSession();
        }

        app.UseMiddleware<CachedClaimsPrincipalMiddleware>();

        return app;
    }

    public static WebApplication UseBallwareChallengeDifferentiation(this WebApplication app, string[]? transportPathPrefixes = null)
    {
        app.UseMiddleware<ChallengeDifferentiationMiddleware>(
            new object[] { transportPathPrefixes is { Length: > 0 } ? transportPathPrefixes : ChallengeDifferentiationMiddleware.DefaultTransportPathPrefixes });

        return app;
    }
}
