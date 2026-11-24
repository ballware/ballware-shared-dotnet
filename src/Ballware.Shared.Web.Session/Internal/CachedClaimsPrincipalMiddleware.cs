using Ballware.Shared.Web.Session.Configuration;
using Microsoft.AspNetCore.Http;

namespace Ballware.Shared.Web.Session.Internal;

class CachedClaimsPrincipalMiddleware
{
    private readonly RequestDelegate _next;

    private ISessionPrincipalProvider SessionPrincipalProvider { get; }
    private WebSessionOptions Options { get; }

    public CachedClaimsPrincipalMiddleware(RequestDelegate next, ISessionPrincipalProvider provider, WebSessionOptions options)
    {
        _next = next;
        SessionPrincipalProvider = provider;
        Options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true && context.Request.Cookies.TryGetValue(Options.SessionCookieName, out var sessionId))
        {
            var principal = await SessionPrincipalProvider.GetPrincipalAsync(sessionId);

            if (principal != null)
            {
                context.User = principal;
            }
        }

        await _next(context);
    }
}
