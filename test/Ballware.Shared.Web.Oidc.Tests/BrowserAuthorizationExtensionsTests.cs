using Ballware.Shared.Web.Oidc.Configuration;
using Ballware.Shared.Web.Session;
using Ballware.Shared.Web.Session.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Oidc.Tests;

public class BrowserAuthorizationExtensionsTests
{
    private static BrowserAuthorizationOptions CreateOptions(string? publicBaseUrl = null) => new()
    {
        Authority = "https://idp.example/realms/ballware",
        Audience = "metaApi",
        ClientId = "llm-app",
        RequiredUserScope = "mlApi",
        RequireHttpsMetadata = false,
        PublicBaseUrl = publicBaseUrl
    };

    private static IConfiguration CreateConfiguration(string? serviceScopePolicyName = "serviceApi")
    {
        var values = new Dictionary<string, string?>
        {
            ["Authorization:Authority"] = "https://idp.example/realms/ballware",
            ["Authorization:Audience"] = "metaApi",
            ["Authorization:ClientId"] = "llm-app",
            ["Authorization:RequiredUserScope"] = "mlApi",
            ["Authorization:RequireHttpsMetadata"] = "false",
            ["Authorization:PublicBaseUrl"] = "https://llm.example"
        };

        if (serviceScopePolicyName != null)
        {
            values["Authorization:ServiceScopePolicyName"] = serviceScopePolicyName;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Test]
    public void AddBallwareBrowserAuthorization_RegistersCookieOidcJwtAndSessionSchemes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBallwareBrowserAuthorization(CreateOptions(), new WebSessionOptions());

        var provider = services.BuildServiceProvider();
        var authenticationOptions = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(authenticationOptions.DefaultScheme, Is.EqualTo(CookieAuthenticationDefaults.AuthenticationScheme));
            Assert.That(authenticationOptions.DefaultChallengeScheme, Is.EqualTo(OpenIdConnectDefaults.AuthenticationScheme));
            Assert.That(authenticationOptions.Schemes.Any(scheme => scheme.Name == CookieAuthenticationDefaults.AuthenticationScheme), Is.True);
            Assert.That(authenticationOptions.Schemes.Any(scheme => scheme.Name == OpenIdConnectDefaults.AuthenticationScheme), Is.True);
            Assert.That(authenticationOptions.Schemes.Any(scheme => scheme.Name == JwtBearerDefaults.AuthenticationScheme), Is.True);
            Assert.That(authenticationOptions.Schemes.Any(scheme => scheme.Name == SessionAuthenticationSchemes.DistributedSession), Is.True);
        });
    }

    [Test]
    public async Task AddBallwareBrowserAuthorization_RegistersScopedPoliciesFromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var options = services.AddBallwareBrowserAuthorization(CreateConfiguration(), new WebSessionOptions());

        Assert.Multiple(() =>
        {
            Assert.That(options.RequiredUserScope, Is.EqualTo("mlApi"));
            Assert.That(options.PublicBaseUrl, Is.EqualTo("https://llm.example"));
        });

        var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var userPolicy = await policyProvider.GetPolicyAsync("mlApi");
        var servicePolicy = await policyProvider.GetPolicyAsync("serviceApi");

        Assert.Multiple(() =>
        {
            Assert.That(userPolicy, Is.Not.Null);
            Assert.That(servicePolicy, Is.Not.Null);
        });
    }

    [Test]
    public async Task UserPolicy_RejectsAuthenticatedUserWithoutRequiredScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBallwareBrowserAuthorization(CreateOptions(), new WebSessionOptions());

        var provider = services.BuildServiceProvider();
        var authorizationService = provider.GetRequiredService<IAuthorizationService>();

        var scopedUser = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("scope", "mlApi other")], "TestAuth"));
        var unscopedUser = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("scope", "otherApi")], "TestAuth"));
        var anonymousUser = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        var scopedResult = await authorizationService.AuthorizeAsync(scopedUser, resource: null, "mlApi");
        var unscopedResult = await authorizationService.AuthorizeAsync(unscopedUser, resource: null, "mlApi");
        var anonymousResult = await authorizationService.AuthorizeAsync(anonymousUser, resource: null, "mlApi");

        Assert.Multiple(() =>
        {
            Assert.That(scopedResult.Succeeded, Is.True);
            Assert.That(unscopedResult.Succeeded, Is.False);
            Assert.That(anonymousResult.Succeeded, Is.False);
        });
    }

    [Test]
    public async Task ServicePolicy_IsSkippedWhenServiceScopePolicyNameIsNotConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBallwareBrowserAuthorization(CreateConfiguration(serviceScopePolicyName: null), new WebSessionOptions());

        var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var userPolicy = await policyProvider.GetPolicyAsync("mlApi");
        var servicePolicy = await policyProvider.GetPolicyAsync("serviceApi");

        Assert.Multiple(() =>
        {
            Assert.That(userPolicy, Is.Not.Null);
            Assert.That(servicePolicy, Is.Null);
        });
    }

    [Test]
    public void AddBallwareBrowserAuthorization_ThrowsWhenAuthorizationSectionIsMissing()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        Assert.Throws<ConfigurationException>(() =>
            services.AddBallwareBrowserAuthorization(configuration, new WebSessionOptions()));
    }

    [Test]
    public void AddBallwareBrowserAuthorization_ThrowsOnInvalidPublicBaseUrl()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<ConfigurationException>(() =>
            services.AddBallwareBrowserAuthorization(CreateOptions(publicBaseUrl: "https://llm.example/?invalid=1"), new WebSessionOptions()));
    }

    [Test]
    public void AddBallwareBrowserAuthorization_BindsConfigurationSectionToOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddBallwareBrowserAuthorization(CreateConfiguration(), new WebSessionOptions());

        var provider = services.BuildServiceProvider();
        var boundOptions = provider.GetRequiredService<IOptions<BrowserAuthorizationOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(boundOptions.Authority, Is.EqualTo("https://idp.example/realms/ballware"));
            Assert.That(boundOptions.ClientId, Is.EqualTo("llm-app"));
            Assert.That(boundOptions.SignOnAllowedHosts, Is.Empty);
            Assert.That(boundOptions.SignOnAllowedRedirectPathPrefixes, Is.Empty);
        });
    }
}
