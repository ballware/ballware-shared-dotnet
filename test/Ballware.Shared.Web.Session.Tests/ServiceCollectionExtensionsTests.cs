using Ballware.Shared.Web.Session.Configuration;
using Ballware.Shared.Web.Session.Internal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Session.Tests;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddBallwareWebSession_RegistersOptionsProviderAndAuthenticationScheme()
    {
        var services = new ServiceCollection();

        services.AddBallwareWebSession(new WebSessionOptions());

        Assert.Multiple(() =>
        {
            Assert.That(
                services.Any(d => d.ServiceType == typeof(WebSessionOptions) && d.Lifetime == ServiceLifetime.Singleton),
                Is.True);
            Assert.That(
                services.Any(d => d.ServiceType == typeof(ISessionPrincipalProvider) && d.ImplementationType == typeof(SessionPrincipalProvider)),
                Is.True);
            Assert.That(
                services.Any(d => d.ServiceType == typeof(IDataProtectionProvider)),
                Is.True);
        });

        var provider = services.BuildServiceProvider();
        var authenticationOptions = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.That(
            authenticationOptions.Schemes.Any(scheme => scheme.Name == SessionAuthenticationSchemes.DistributedSession),
            Is.True);
    }

    [Test]
    public void AddBallwareWebSession_RegistersSessionWhenEnableSessionIsTrue()
    {
        var services = new ServiceCollection();

        services.AddBallwareWebSession(new WebSessionOptions { EnableSession = true });

        Assert.That(
            services.Any(d => d.ServiceType == typeof(Microsoft.AspNetCore.Session.ISessionStore)),
            Is.True);
    }

    [Test]
    public void AddBallwareWebSession_DoesNotRegisterSessionWhenEnableSessionIsFalse()
    {
        var services = new ServiceCollection();

        services.AddBallwareWebSession(new WebSessionOptions { EnableSession = false });

        Assert.That(
            services.Any(d => d.ServiceType == typeof(Microsoft.AspNetCore.Session.ISessionStore)),
            Is.False);
    }
}
