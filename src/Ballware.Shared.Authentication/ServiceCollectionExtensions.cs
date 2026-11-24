using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Authentication;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareSharedUserInfoEnrichment(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Authorization:UserInfo")
    {
        services.AddOptionsWithValidateOnStart<UserInfoEnrichmentOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .Validate(options => !options.Enabled || options.Scopes.Count > 0,
                $"{sectionName}:Scopes must contain at least one scope when {sectionName}:Enabled is true.")
            .Validate(options => !options.Enabled || !string.IsNullOrEmpty(options.UserinfoEndpoint) || !string.IsNullOrEmpty(options.Authority),
                $"{sectionName}:Authority is required when {sectionName}:Enabled is true and no explicit {sectionName}:UserinfoEndpoint is configured.");

        return services.AddBallwareSharedUserInfoEnrichmentCore();
    }

    public static IServiceCollection AddBallwareSharedUserInfoEnrichment(
        this IServiceCollection services,
        Action<UserInfoEnrichmentOptions> configureOptions)
    {
        services.AddOptionsWithValidateOnStart<UserInfoEnrichmentOptions>()
            .Configure(configureOptions)
            .ValidateDataAnnotations()
            .Validate(options => !options.Enabled || options.Scopes.Count > 0,
                "UserInfoEnrichmentOptions:Scopes must contain at least one scope when Enabled is true.")
            .Validate(options => !options.Enabled || !string.IsNullOrEmpty(options.UserinfoEndpoint) || !string.IsNullOrEmpty(options.Authority),
                "UserInfoEnrichmentOptions:Authority is required when Enabled is true and no explicit UserinfoEndpoint is configured.");

        return services.AddBallwareSharedUserInfoEnrichmentCore();
    }

    private static IServiceCollection AddBallwareSharedUserInfoEnrichmentCore(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddHttpClient(UserInfoEnricher.HttpClientName, (serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<UserInfoEnrichmentOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds + 1);
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

        services.AddSingleton<IUserInfoEnricher, UserInfoEnricher>();

        return services;
    }
}
