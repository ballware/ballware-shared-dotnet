using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Ballware.Shared.Web.Session;

// SignalR-Circuits und DX-Transport-Endpoints können einer OIDC-302-Challenge nicht folgen.
// Fehlende Authentifizierung liefert auf diesen Pfaden gezielt 401, damit das Frontend
// erneut über /signon initialisieren kann. Seitenrequests behalten die OIDC-Challenge
// (-> /signin-oidc -> Cookie).
public class ChallengeDifferentiationMiddleware
{
    public static readonly string[] DefaultTransportPathPrefixes =
    [
        "/_blazor",
        "/DXX",
        "/dxDR"
    ];

    private readonly RequestDelegate _next;
    private readonly string[] _transportPathPrefixes;

    public ChallengeDifferentiationMiddleware(RequestDelegate next, string[]? transportPathPrefixes = null)
    {
        _next = next;
        _transportPathPrefixes = transportPathPrefixes is { Length: > 0 } ? transportPathPrefixes : DefaultTransportPathPrefixes;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsTransportPath(context.Request.Path))
        {
            await _next(context);

            return;
        }

        var result = await context.AuthenticateAsync(SessionAuthenticationSchemes.DistributedSession);

        if (!result.Succeeded)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        context.User = result.Principal;

        await _next(context);
    }

    private bool IsTransportPath(PathString path) =>
        Array.Exists(_transportPathPrefixes, prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
