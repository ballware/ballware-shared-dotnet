using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Ballware.Shared.Authentication.Tests;

public class JwtBearerEventsExtensionsTest
{
    private static (DefaultHttpContext HttpContext, TokenValidatedContext Context) CreateContext(
        IUserInfoEnricher enricher,
        ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddSingleton(enricher);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        httpContext.Request.Headers.Authorization = "Bearer token123";

        var context = new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = principal
        };

        return (httpContext, context);
    }

    [Test]
    public void AttachUserInfoEnrichment_SetsOnTokenValidated_AndReturnsEvents()
    {
        var events = new JwtBearerEvents();

        var result = events.AttachUserInfoEnrichment();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.SameAs(events));
            Assert.That(events.OnTokenValidated, Is.Not.Null);
        });
    }

    [Test]
    public async Task AttachUserInfoEnrichment_ReplacesPrincipalOnEnrichedResult()
    {
        var enrichedIdentity = new ClaimsIdentity("test-auth");
        enrichedIdentity.AddClaim(new Claim("tenant", "t1"));
        var enrichedPrincipal = new ClaimsPrincipal(enrichedIdentity);

        var enricherMock = new Mock<IUserInfoEnricher>();
        enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<ClaimsPrincipal>(), "token123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserInfoEnrichmentResult.Enriched(enrichedPrincipal));

        var (httpContext, context) = CreateContext(enricherMock.Object, new ClaimsPrincipal(new ClaimsIdentity("original")));

        var events = new JwtBearerEvents().AttachUserInfoEnrichment();

        await events.OnTokenValidated(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.SameAs(enrichedPrincipal));
            Assert.That(context.Result?.Failure, Is.Null);
        });
    }

    [Test]
    public async Task AttachUserInfoEnrichment_KeepsPrincipalOnNotEnrichedResult()
    {
        var originalPrincipal = new ClaimsPrincipal(new ClaimsIdentity("original"));

        var enricherMock = new Mock<IUserInfoEnricher>();
        enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<ClaimsPrincipal>(), "token123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserInfoEnrichmentResult.NotEnriched());

        var (_, context) = CreateContext(enricherMock.Object, originalPrincipal);

        var events = new JwtBearerEvents().AttachUserInfoEnrichment();

        await events.OnTokenValidated(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.SameAs(originalPrincipal));
            Assert.That(context.Result?.Failure, Is.Null);
        });
    }

    [Test]
    public async Task AttachUserInfoEnrichment_FailsAuthenticationOnFailedResult()
    {
        var enricherMock = new Mock<IUserInfoEnricher>();
        enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<ClaimsPrincipal>(), "token123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserInfoEnrichmentResult.Failed());

        var (_, context) = CreateContext(enricherMock.Object, new ClaimsPrincipal(new ClaimsIdentity("original")));

        var events = new JwtBearerEvents().AttachUserInfoEnrichment();

        await events.OnTokenValidated(context);

        Assert.That(context.Result?.Failure, Is.Not.Null);
    }

    [Test]
    public async Task AttachUserInfoEnrichment_PreservesPreviousOnTokenValidated()
    {
        var enricherMock = new Mock<IUserInfoEnricher>();
        enricherMock
            .Setup(e => e.EnrichAsync(It.IsAny<ClaimsPrincipal>(), "token123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserInfoEnrichmentResult.NotEnriched());

        var previousCalled = false;

        var (_, context) = CreateContext(enricherMock.Object, new ClaimsPrincipal(new ClaimsIdentity("original")));

        var events = new JwtBearerEvents
        {
            OnTokenValidated = _ =>
            {
                previousCalled = true;

                return Task.CompletedTask;
            }
        }.AttachUserInfoEnrichment();

        await events.OnTokenValidated(context);

        Assert.That(previousCalled, Is.True);
    }

    [Test]
    public async Task AttachUserInfoEnrichment_WithoutRegisteredEnricher_IsNoOp()
    {
        var originalPrincipal = new ClaimsPrincipal(new ClaimsIdentity("original"));

        var services = new ServiceCollection();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        httpContext.Request.Headers.Authorization = "Bearer token123";

        var context = new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = originalPrincipal
        };

        var events = new JwtBearerEvents().AttachUserInfoEnrichment();

        await events.OnTokenValidated(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Principal, Is.SameAs(originalPrincipal));
            Assert.That(context.Result?.Failure, Is.Null);
        });
    }
}
