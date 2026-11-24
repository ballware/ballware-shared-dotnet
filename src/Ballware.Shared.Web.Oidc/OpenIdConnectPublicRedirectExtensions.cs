using Ballware.Shared.Web.Oidc.Configuration;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ballware.Shared.Web.Oidc;

// Der vom Framework standardmäßig aus Scheme/Host des eingehenden Requests abgeleitete redirect_uri
// der OIDC-Challenge ist auf der eigenen Origin der App (und hinter Proxies, die nur einen
// internen Host liefern) regelmäßig nicht als gültige Redirect-URI für den Client beim IdP
// registriert. IdPs mit Pushed-Authorization-Endpunkt (Keycloak bietet ihn in der Discovery an)
// prüfen den redirect_uri bereits serverseitig im Challenge-Async und antworten mit 400 — das
// führt als OpenIdConnectProtocolException zu 500 statt zu einem Login-Redirect.
// Deshalb wird der redirect_uri deterministisch aus Authorization:PublicBaseUrl gebildet.
public static class OpenIdConnectPublicRedirectExtensions
{
    // Die tatsächlich verwendete redirect_uri; wird der Fehlerbehandlung der Challenge bereitgestellt.
    public const string EffectiveRedirectUriItemKey = "Ballware.Oidc.EffectiveRedirectUri";

    private const string DefaultCallbackPath = "/signin-oidc";

    public static void RegisterBallwarePublicRedirectUriHandling(this OpenIdConnectOptions options, BrowserAuthorizationOptions authorizationOptions)
    {
        var callbackPath = options.CallbackPath.HasValue ? options.CallbackPath.Value : DefaultCallbackPath;
        var publicBase = NormalizePublicBaseUrl(authorizationOptions.PublicBaseUrl);

        // Vorher registrierter Handler des Delegaten (nicht die Methode RedirectToIdentityProvider,
        // die diesen Delegaten selbst aufruft und damit zur Endlosschleife führte).
        var configuredHandler = options.Events.OnRedirectToIdentityProvider;

        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            var redirectUri = publicBase != null
                ? publicBase + callbackPath
                : context.Request.Scheme + "://" + context.Request.Host + context.Request.PathBase + callbackPath;

            context.ProtocolMessage.RedirectUri = redirectUri;
            context.HttpContext.Items[EffectiveRedirectUriItemKey] = redirectUri;

            if (context.HttpContext.RequestServices != null)
            {
                context.HttpContext.RequestServices.GetService<ILoggerFactory>()
                    ?.CreateLogger(typeof(OpenIdConnectPublicRedirectExtensions).FullName!)
                    .LogDebug("OIDC challenge uses redirect_uri {RedirectUri} for client {ClientId}", redirectUri, options.ClientId);
            }

            if (configuredHandler != null)
            {
                await configuredHandler(context);
            }
        };
    }

    // Liefert die normalisierte Basis-URL ohne abschließenden Slash, oder null, wenn keine
    // konfiguriert ist. Unzulässige Angaben brechen die Konfiguration sichtbar ab, statt sie
    // verschwiegen in eine IdP-Ablehnung laufen zu lassen.
    public static string? NormalizePublicBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var publicBase)
            || (publicBase.Scheme != Uri.UriSchemeHttp && publicBase.Scheme != Uri.UriSchemeHttps)
            || publicBase.Query.Length > 0
            || publicBase.Fragment.Length > 0)
        {
            throw new ConfigurationException($"Invalid Authorization:PublicBaseUrl '{value}'. Expected an absolute http(s) URL without query or fragment, e.g. 'https://reporting.ballware.de'.");
        }

        var basePath = publicBase.AbsolutePath.TrimEnd('/');

        return publicBase.Scheme + "://" + publicBase.Authority + basePath;
    }
}
