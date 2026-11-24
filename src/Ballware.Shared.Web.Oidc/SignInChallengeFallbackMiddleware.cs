using Ballware.Shared.Web.Oidc.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Ballware.Shared.Web.Oidc;

// Scheitert die OIDC-Challenge selbst — typischerweise weil die verwendete redirect_uri nicht als
// gültige Redirect-URI für den Client beim IdP registriert ist und der IdP den
// Pushed-Authorization-Request mit 400 ablehnt — läuft die Exception unverarbeitet durch die
// Autorisierungsmiddleware und endet als 500. Ausgeliefert wird gezielt 401, damit Requests
// kontrolliert über /signon re-initialisiert werden können; die verwendete redirect_uri geht ins Log.
public class SignInChallengeFallbackMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BrowserAuthorizationOptions _authorizationOptions;
    private readonly ILogger<SignInChallengeFallbackMiddleware> _logger;

    public SignInChallengeFallbackMiddleware(RequestDelegate next, IOptions<BrowserAuthorizationOptions> authorizationOptions, ILogger<SignInChallengeFallbackMiddleware> logger)
    {
        _next = next;
        _authorizationOptions = authorizationOptions.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OpenIdConnectProtocolException exception) when (!context.Response.HasStarted)
        {
            var redirectUri = context.Items[OpenIdConnectPublicRedirectExtensions.EffectiveRedirectUriItemKey] as string;

            _logger.LogError(
                exception,
                "OIDC sign-in challenge failed for {Path}. redirect_uri {RedirectUri} was used for client {ClientId}, Authorization:PublicBaseUrl is {PublicBaseUrl}. Register the callback path as valid redirect URI for this client at the identity provider.",
                context.Request.Path.HasValue ? context.Request.Path.Value : string.Empty,
                redirectUri ?? "<unknown>",
                _authorizationOptions.ClientId,
                string.IsNullOrWhiteSpace(_authorizationOptions.PublicBaseUrl) ? "<not configured>" : _authorizationOptions.PublicBaseUrl);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        }
    }
}
