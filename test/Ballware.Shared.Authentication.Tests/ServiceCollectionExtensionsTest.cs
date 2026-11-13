using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Authentication.Tests;

public class ServiceCollectionExtensionsTest
{
    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services;
    }

    [Test]
    public void AddBallwareSharedUserInfoEnrichment_WithConfiguration_RegistersEnricherAndOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:UserInfo:Enabled"] = "true",
                ["Authorization:UserInfo:Scopes:0"] = "metaApi",
                ["Authorization:UserInfo:Authority"] = "https://idp.example/realms/ballware"
            })
            .Build();

        var services = CreateServices();
        services.AddBallwareSharedUserInfoEnrichment(configuration);

        using var provider = services.BuildServiceProvider();

        var enricher = provider.GetRequiredService<IUserInfoEnricher>();
        var options = provider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(enricher, Is.InstanceOf<UserInfoEnricher>());
            Assert.That(options.Enabled, Is.True);
            Assert.That(options.Scopes, Is.EquivalentTo(new[] {"metaApi"}));
            Assert.That(options.Authority, Is.EqualTo("https://idp.example/realms/ballware"));
            Assert.That(options.CacheTtlSeconds, Is.EqualTo(60));
        });
    }

    [Test]
    public void AddBallwareSharedUserInfoEnrichment_EnabledWithoutScopes_ThrowsOnOptionsResolve()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:UserInfo:Enabled"] = "true",
                ["Authorization:UserInfo:Authority"] = "https://idp.example/realms/ballware"
            })
            .Build();

        var services = CreateServices();
        services.AddBallwareSharedUserInfoEnrichment(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value;
        });
    }

    [Test]
    public void AddBallwareSharedUserInfoEnrichment_EnabledWithoutAuthorityOrEndpoint_ThrowsOnOptionsResolve()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authorization:UserInfo:Enabled"] = "true",
                ["Authorization:UserInfo:Scopes:0"] = "metaApi"
            })
            .Build();

        var services = CreateServices();
        services.AddBallwareSharedUserInfoEnrichment(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value;
        });
    }

    [Test]
    public void AddBallwareSharedUserInfoEnrichment_DisabledWithoutAuthority_IsValid()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = CreateServices();
        services.AddBallwareSharedUserInfoEnrichment(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IUserInfoEnricher>(), Is.InstanceOf<UserInfoEnricher>());
            Assert.That(provider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value.Enabled, Is.False);
        });
    }

    [Test]
    public void AddBallwareSharedUserInfoEnrichment_WithConfigureAction_RegistersEnricher()
    {
        var services = CreateServices();
        services.AddBallwareSharedUserInfoEnrichment(options =>
        {
            options.Enabled = true;
            options.Scopes = ["metaApi"];
            options.Authority = "https://idp.example/realms/ballware";
        });

        using var provider = services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IUserInfoEnricher>(), Is.InstanceOf<UserInfoEnricher>());
            Assert.That(provider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value.Scopes,
                Is.EquivalentTo(new[] {"metaApi"}));
        });
    }

    [Test]
    public void AddBallwareSharedUserInfoEnrichment_WithInvalidConfigureAction_ThrowsOnOptionsResolve()
    {
        var services = CreateServices();
        services.AddBallwareSharedUserInfoEnrichment(options =>
        {
            options.Enabled = true;
            options.Authority = "https://idp.example/realms/ballware";
        });

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value;
        });
    }
}
