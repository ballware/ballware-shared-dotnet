using Ballware.Shared.Web.Session.Configuration;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Web.Session;

public static class OpenIdConnectSessionOptionsExtensions
{
    public static void RegisterBallwareWebSessionTokenHandling(this OpenIdConnectOptions options)
    {
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;

        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                var httpContext = context.HttpContext;
                var sessionOptions = httpContext.RequestServices.GetRequiredService<WebSessionOptions>();
                var sessionProvider = httpContext.RequestServices.GetRequiredService<ISessionPrincipalProvider>();

                var principal = context.Principal!;
                await sessionProvider.StorePrincipalAsync(httpContext, principal);

                if (sessionOptions.SuppressDefaultSessionCookie)
                {
                    var returnUrl = context.Properties?.RedirectUri;

                    if (string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative))
                    {
                        returnUrl = "/";
                    }

                    context.HandleResponse();
                    httpContext.Response.Redirect(returnUrl!);
                }
            }
        };
    }
}
