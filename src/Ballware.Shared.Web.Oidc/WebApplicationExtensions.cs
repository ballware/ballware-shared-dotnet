using Microsoft.AspNetCore.Builder;

namespace Ballware.Shared.Web.Oidc;

public static class WebApplicationExtensions
{
    public static WebApplication UseBallwareSignInChallengeFallback(this WebApplication app)
    {
        app.UseMiddleware<SignInChallengeFallbackMiddleware>();

        return app;
    }
}
