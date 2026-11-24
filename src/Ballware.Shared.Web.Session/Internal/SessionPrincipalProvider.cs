using System.Security.Claims;
using Ballware.Shared.Web.Session.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace Ballware.Shared.Web.Session.Internal;

class SessionPrincipalProvider : ISessionPrincipalProvider
{
    private IDistributedCache Cache { get; }
    private IDataProtectionProvider DataProtectionProvider { get; }
    private WebSessionOptions Options { get; }

    public SessionPrincipalProvider(IDistributedCache cache, IDataProtectionProvider dataProtectionProvider, WebSessionOptions options)
    {
        Cache = cache;
        DataProtectionProvider = dataProtectionProvider;
        Options = options;
    }

    public async Task<ClaimsPrincipal?> GetPrincipalAsync(string sessionId)
    {
        var serializedTicket = await Cache.GetStringAsync($"{Options.SessionCacheKeyPrefix}{sessionId}");

        if (!string.IsNullOrEmpty(serializedTicket))
        {
            var dataProtector = DataProtectionProvider.CreateProtector(Options.DataProtectionPurpose);

            var binaryProtectedTicket = Convert.FromBase64String(serializedTicket);
            var binaryUnprotectedTicket = dataProtector.Unprotect(binaryProtectedTicket);
            var ticket = TicketSerializer.Default.Deserialize(binaryUnprotectedTicket);

            return ticket?.Principal;
        }

        return null;
    }

    public async Task StorePrincipalAsync(HttpContext context, ClaimsPrincipal principal)
    {
        var sessionId = Guid.NewGuid().ToString();

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Options.SessionCacheExpirationInMinutes)
        };

        var dataProtector = DataProtectionProvider.CreateProtector(Options.DataProtectionPurpose);

        var ticket = new AuthenticationTicket(principal, SessionAuthenticationSchemes.DistributedSession);
        var unprotectedBytes = TicketSerializer.Default.Serialize(ticket);
        var protectedBytes = dataProtector.Protect(unprotectedBytes);
        var serializedPrincipal = Convert.ToBase64String(protectedBytes);

        await Cache.SetStringAsync($"{Options.SessionCacheKeyPrefix}{sessionId}", serializedPrincipal, options);

        context.Response.Cookies.Append(Options.SessionCookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(Options.SessionCacheExpirationInMinutes)
        });
    }
}
