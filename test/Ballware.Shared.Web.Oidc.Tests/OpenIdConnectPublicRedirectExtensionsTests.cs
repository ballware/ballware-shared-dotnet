using Ballware.Shared.Web.Oidc.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Ballware.Shared.Web.Oidc.Tests;

public class OpenIdConnectPublicRedirectExtensionsTests
{
    private static BrowserAuthorizationOptions CreateAuthorizationOptions(string? publicBaseUrl) => new()
    {
        Authority = "https://idp.example/realms/ballware",
        Audience = "metaApi",
        ClientId = "llm-app",
        RequiredUserScope = "mlApi",
        PublicBaseUrl = publicBaseUrl
    };

    private static RedirectContext CreateChallengeContext(OpenIdConnectOptions options, out DefaultHttpContext httpContext)
    {
        httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("internal", 5000);
        httpContext.Request.PathBase = "/app";

        var scheme = new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, "OpenID Connect", typeof(OpenIdConnectHandler));
        var properties = new AuthenticationProperties();

        var context = new RedirectContext(httpContext, scheme, options, properties);

        // Der Handler setzt ProtocolMessage vor dem Event-Aufruf; hier wird der interne Setter
        // per Reflection nachgestellt.
        typeof(RedirectContext)
            .GetProperty(nameof(RedirectContext.ProtocolMessage))!
            .SetValue(context, new OpenIdConnectMessage());

        return context;
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void NormalizePublicBaseUrl_ReturnsNullForUnconfiguredValue(string? value)
    {
        Assert.That(OpenIdConnectPublicRedirectExtensions.NormalizePublicBaseUrl(value), Is.Null);
    }

    [TestCase("https://reporting.ballware.de/", "https://reporting.ballware.de")]
    [TestCase("http://localhost:3003", "http://localhost:3003")]
    [TestCase("https://llm.example/base/", "https://llm.example/base")]
    [TestCase(" https://llm.example ", "https://llm.example")]
    public void NormalizePublicBaseUrl_ReturnsNormalizedBaseWithoutTrailingSlash(string value, string expected)
    {
        Assert.That(OpenIdConnectPublicRedirectExtensions.NormalizePublicBaseUrl(value), Is.EqualTo(expected));
    }

    [TestCase("/relative/base")]
    [TestCase("https://llm.example/?query=1")]
    [TestCase("https://llm.example#fragment")]
    [TestCase("ftp://llm.example")]
    public void NormalizePublicBaseUrl_ThrowsOnInvalidValue(string value)
    {
        Assert.Throws<ConfigurationException>(() => OpenIdConnectPublicRedirectExtensions.NormalizePublicBaseUrl(value));
    }

    [Test]
    public async Task RegisterBallwarePublicRedirectUriHandling_UsesPublicBaseUrlDerivedRedirectUri()
    {
        var options = new OpenIdConnectOptions { ClientId = "llm-app" };

        options.RegisterBallwarePublicRedirectUriHandling(CreateAuthorizationOptions("https://llm.example"));

        var context = CreateChallengeContext(options, out var httpContext);

        await options.Events.OnRedirectToIdentityProvider(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.ProtocolMessage.RedirectUri, Is.EqualTo("https://llm.example/signin-oidc"));
            Assert.That(httpContext.Items[OpenIdConnectPublicRedirectExtensions.EffectiveRedirectUriItemKey], Is.EqualTo("https://llm.example/signin-oidc"));
        });
    }

    [Test]
    public async Task RegisterBallwarePublicRedirectUriHandling_FallsBackToRequestOriginWithoutPublicBaseUrl()
    {
        var options = new OpenIdConnectOptions { ClientId = "llm-app" };

        options.RegisterBallwarePublicRedirectUriHandling(CreateAuthorizationOptions(null));

        var context = CreateChallengeContext(options, out _);

        await options.Events.OnRedirectToIdentityProvider(context);

        Assert.That(context.ProtocolMessage.RedirectUri, Is.EqualTo("http://internal:5000/app/signin-oidc"));
    }

    [Test]
    public async Task RegisterBallwarePublicRedirectUriHandling_ChainsPreviouslyConfiguredHandler()
    {
        var chainedHandlerCalled = false;

        var options = new OpenIdConnectOptions { ClientId = "llm-app" };
        options.Events.OnRedirectToIdentityProvider = _ =>
        {
            chainedHandlerCalled = true;
            return Task.CompletedTask;
        };

        options.RegisterBallwarePublicRedirectUriHandling(CreateAuthorizationOptions("https://llm.example"));

        var context = CreateChallengeContext(options, out _);

        await options.Events.OnRedirectToIdentityProvider(context);

        Assert.Multiple(() =>
        {
            Assert.That(chainedHandlerCalled, Is.True);
            Assert.That(context.ProtocolMessage.RedirectUri, Is.EqualTo("https://llm.example/signin-oidc"));
        });
    }
}
