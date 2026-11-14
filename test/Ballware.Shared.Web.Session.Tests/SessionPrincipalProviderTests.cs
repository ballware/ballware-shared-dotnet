using System.Security.Claims;
using System.Security.Cryptography;
using Ballware.Shared.Web.Session.Configuration;
using Ballware.Shared.Web.Session.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Session.Tests;

public class SessionPrincipalProviderTests
{
    private static ClaimsPrincipal CreatePrincipal(string nameIdentifier) =>
        new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, nameIdentifier),
            new Claim("tenant", "tenant-1")
        ], "TestAuth"));

    private static string ReadSessionIdFromCookie(DefaultHttpContext context, string cookieName)
    {
        var header = context.Response.Headers["Set-Cookie"].ToString();

        Assert.That(header, Does.StartWith($"{cookieName}=").IgnoreCase);

        var cookiePart = header.Split(';')[0];

        return cookiePart.Substring(cookiePart.IndexOf('=') + 1);
    }

    [Test]
    public async Task StorePrincipalAsync_StoresProtectedTicketAndSetsSessionCookie()
    {
        var options = new WebSessionOptions
        {
            SessionCookieName = ".TestSession",
            SessionCacheKeyPrefix = "test:session:",
            DataProtectionPurpose = "Test.Purpose"
        };

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var dataProtection = new EphemeralDataProtectionProvider();
        var provider = new SessionPrincipalProvider(cache, dataProtection, options);

        var context = new DefaultHttpContext();

        await provider.StorePrincipalAsync(context, CreatePrincipal("user-1"));

        var header = context.Response.Headers["Set-Cookie"].ToString().ToLowerInvariant();
        var sessionId = ReadSessionIdFromCookie(context, options.SessionCookieName);

        Assert.Multiple(() =>
        {
            Assert.That(Guid.TryParse(sessionId, out _), Is.True);
            Assert.That(header, Does.Contain("path=/"));
            Assert.That(header, Does.Contain("httponly"));
            Assert.That(header, Does.Contain("secure"));
            Assert.That(header, Does.Contain("samesite=none"));
            Assert.That(header, Does.Contain("expires="));
        });

        var serializedTicket = await cache.GetStringAsync($"{options.SessionCacheKeyPrefix}{sessionId}");

        Assert.That(serializedTicket, Is.Not.Null.And.Not.Empty);

        var unprotectedTicket = dataProtection
            .CreateProtector(options.DataProtectionPurpose)
            .Unprotect(Convert.FromBase64String(serializedTicket));

        var ticket = TicketSerializer.Default.Deserialize(unprotectedTicket);

        Assert.Multiple(() =>
        {
            Assert.That(ticket, Is.Not.Null);
            Assert.That(ticket!.AuthenticationScheme, Is.EqualTo(SessionAuthenticationSchemes.DistributedSession));
            Assert.That(ticket.Principal.FindFirstValue("tenant"), Is.EqualTo("tenant-1"));
        });
    }

    [Test]
    public async Task GetPrincipalAsync_ReturnsStoredPrincipalForSessionId()
    {
        var options = new WebSessionOptions();

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var provider = new SessionPrincipalProvider(cache, new EphemeralDataProtectionProvider(), options);

        var storeContext = new DefaultHttpContext();

        await provider.StorePrincipalAsync(storeContext, CreatePrincipal("user-1"));

        var sessionId = ReadSessionIdFromCookie(storeContext, options.SessionCookieName);

        var principal = await provider.GetPrincipalAsync(sessionId);

        Assert.Multiple(() =>
        {
            Assert.That(principal, Is.Not.Null);
            Assert.That(principal!.Identity?.IsAuthenticated, Is.True);
            Assert.That(principal.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("user-1"));
            Assert.That(principal.FindFirstValue("tenant"), Is.EqualTo("tenant-1"));
        });
    }

    [Test]
    public async Task GetPrincipalAsync_ReturnsNullForUnknownSessionId()
    {
        var options = new WebSessionOptions();

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var provider = new SessionPrincipalProvider(cache, new EphemeralDataProtectionProvider(), options);

        var principal = await provider.GetPrincipalAsync(Guid.NewGuid().ToString());

        Assert.That(principal, Is.Null);
    }

    [Test]
    public async Task SessionPrincipal_OnlyUnprotectableWithConfiguredDataProtectionPurpose()
    {
        var options = new WebSessionOptions
        {
            DataProtectionPurpose = "Custom.Purpose"
        };

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var dataProtection = new EphemeralDataProtectionProvider();
        var provider = new SessionPrincipalProvider(cache, dataProtection, options);

        var context = new DefaultHttpContext();

        await provider.StorePrincipalAsync(context, CreatePrincipal("user-1"));

        var sessionId = ReadSessionIdFromCookie(context, options.SessionCookieName);
        var serializedTicket = (await cache.GetStringAsync($"{options.SessionCacheKeyPrefix}{sessionId}"))!;
        var protectedTicket = Convert.FromBase64String(serializedTicket);

        var ticketWithConfiguredPurpose = TicketSerializer.Default.Deserialize(
            dataProtection.CreateProtector("Custom.Purpose").Unprotect(protectedTicket));

        Assert.That(ticketWithConfiguredPurpose?.Principal.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("user-1"));

        Assert.Throws<CryptographicException>(
            () => dataProtection.CreateProtector("Ballware.Web.Session.ClaimsPrincipal").Unprotect(protectedTicket));
    }
}
