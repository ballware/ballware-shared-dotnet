using System.Security.Claims;
using Ballware.Shared.Web.Session.Configuration;
using Ballware.Shared.Web.Session.Internal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Session.Tests;

public class CachedClaimsPrincipalMiddlewareTests
{
    private static ClaimsPrincipal CreatePrincipal(string nameIdentifier) =>
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, nameIdentifier)], "TestAuth"));

    private static (SessionPrincipalProvider Provider, MemoryDistributedCache Cache, WebSessionOptions Options) CreateProvider(
        WebSessionOptions? options = null)
    {
        options ??= new WebSessionOptions();

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

        return (new SessionPrincipalProvider(cache, new EphemeralDataProtectionProvider(), options), cache, options);
    }

    private static async Task<(string SessionId, WebSessionOptions Options)> StorePrincipalAsync(
        SessionPrincipalProvider provider,
        WebSessionOptions options,
        string nameIdentifier)
    {
        var storeContext = new DefaultHttpContext();

        await provider.StorePrincipalAsync(storeContext, CreatePrincipal(nameIdentifier));

        var header = storeContext.Response.Headers["Set-Cookie"].ToString();
        var sessionId = header.Split(';')[0].Substring(header.Split(';')[0].IndexOf('=') + 1);

        return (sessionId, options);
    }

    [Test]
    public async Task InvokeAsync_SetsPrincipalFromCookieWhenUserIsUnauthenticated()
    {
        var (provider, cache, options) = CreateProvider();

        var (sessionId, _) = await StorePrincipalAsync(provider, options, "cookie-user");

        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Cookie"] = $"{options.SessionCookieName}={sessionId}";

            var nextCalled = false;

            var middleware = new CachedClaimsPrincipalMiddleware(
                _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                provider,
                options);

            await middleware.InvokeAsync(context);

            Assert.Multiple(() =>
            {
                Assert.That(nextCalled, Is.True);
                Assert.That(context.User.Identity?.IsAuthenticated, Is.True);
                Assert.That(context.User.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("cookie-user"));
            });
        }
    }

    [Test]
    public async Task InvokeAsync_DoesNotOverwriteAlreadyAuthenticatedUser()
    {
        var (provider, cache, options) = CreateProvider();

        var (sessionId, _) = await StorePrincipalAsync(provider, options, "cookie-user");

        {
            var context = new DefaultHttpContext();
            context.Request.Headers["Cookie"] = $"{options.SessionCookieName}={sessionId}";
            context.User = CreatePrincipal("existing-user");

            var middleware = new CachedClaimsPrincipalMiddleware(
                _ => Task.CompletedTask,
                provider,
                options);

            await middleware.InvokeAsync(context);

            Assert.That(context.User.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo("existing-user"));
        }
    }

    [Test]
    public async Task InvokeAsync_CallsNextWithoutCookie()
    {
        var (provider, cache, options) = CreateProvider();

        {
            var context = new DefaultHttpContext();

            var nextCalled = false;

            var middleware = new CachedClaimsPrincipalMiddleware(
                _ =>
                {
                    nextCalled = true;
                    return Task.CompletedTask;
                },
                provider,
                options);

            await middleware.InvokeAsync(context);

            Assert.Multiple(() =>
            {
                Assert.That(nextCalled, Is.True);
                Assert.That(context.User.Identity?.IsAuthenticated, Is.Not.EqualTo(true));
            });
        }
    }
}
