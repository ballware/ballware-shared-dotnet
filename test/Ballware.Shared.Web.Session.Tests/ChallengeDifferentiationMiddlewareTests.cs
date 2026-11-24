using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Session.Tests;

public class ChallengeDifferentiationMiddlewareTests
{
    private const string StubAuthItemKey = "stubAuthSuccess";

    private class StubSessionHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public StubSessionHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Context.Items.TryGetValue(StubAuthItemKey, out var value) && value is true)
            {
                var identity = new ClaimsIdentity("StubAuth");
                var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    private static DefaultHttpContext CreateContext(string path, bool authenticated)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, StubSessionHandler>(SessionAuthenticationSchemes.DistributedSession, null);

        var context = new DefaultHttpContext();
        context.RequestServices = services.BuildServiceProvider();
        context.Request.Path = path;
        context.Items[StubAuthItemKey] = authenticated;

        return context;
    }

    [Test]
    public async Task InvokeAsync_Returns401ForTransportPathWithoutSession()
    {
        var nextCalled = false;

        var middleware = new ChallengeDifferentiationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("/_blazor/negotiate", authenticated: false);

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(nextCalled, Is.False);
        });
    }

    [Test]
    public async Task InvokeAsync_SetsPrincipalAndCallsNextForTransportPathWithSession()
    {
        var nextCalled = false;

        var middleware = new ChallengeDifferentiationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("/_blazor/disconnect", authenticated: true);

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(context.User.Identity?.IsAuthenticated, Is.True);
        });
    }

    [Test]
    public async Task InvokeAsync_PassesThroughNonTransportPathsWithoutAuthentication()
    {
        var nextCalled = false;

        var middleware = new ChallengeDifferentiationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("/chat/global", authenticated: false);

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        });
    }

    [Test]
    public async Task InvokeAsync_UsesConfiguredPrefixesInsteadOfDefaults()
    {
        var nextCalled = false;

        var middleware = new ChallengeDifferentiationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            ["/ws"]);

        var blockedContext = CreateContext("/ws/socket", authenticated: false);
        var passedContext = CreateContext("/_blazor/negotiate", authenticated: false);

        await middleware.InvokeAsync(blockedContext);
        await middleware.InvokeAsync(passedContext);

        Assert.Multiple(() =>
        {
            Assert.That(blockedContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(passedContext.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(nextCalled, Is.True);
        });
    }
}
