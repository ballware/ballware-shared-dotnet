using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Session.Internal;

class SessionPrincipalAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public SessionPrincipalAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            var ticket = new AuthenticationTicket(Context.User, SessionAuthenticationSchemes.DistributedSession);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        return Task.FromResult(AuthenticateResult.Fail("No user found."));
    }
}
