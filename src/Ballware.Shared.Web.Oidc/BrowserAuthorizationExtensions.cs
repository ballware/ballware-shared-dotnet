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
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Web.Oidc;

public static class BrowserAuthorizationExtensions
{
    private const string ClaimTypeScope = "scope";

    public static BrowserAuthorizationOptions AddBallwareBrowserAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        WebSessionOptions sessionOptions)
    {
        services.AddOptionsWithValidateOnStart<BrowserAuthorizationOptions>()
            .Bind(configuration.GetSection("Authorization"))
            .ValidateDataAnnotations();

        BrowserAuthorizationOptions? options =
            configuration.GetSection("Authorization").Get<BrowserAuthorizationOptions>();

        if (options == null)
        {
            throw new ConfigurationException("Required configuration for authorization is missing");
        }

        return AddBrowserAuthentication(services, options, sessionOptions);
    }

    public static BrowserAuthorizationOptions AddBallwareBrowserAuthorization(
        this IServiceCollection services,
        BrowserAuthorizationOptions options,
        WebSessionOptions sessionOptions)
    {
        services.AddSingleton(options);
        services.AddSingleton<IConfigureOptions<BrowserAuthorizationOptions>>(new BrowserAuthorizationOptionsConfigurator(options));

        return AddBrowserAuthentication(services, options, sessionOptions);
    }

    private static BrowserAuthorizationOptions AddBrowserAuthentication(
        IServiceCollection services,
        BrowserAuthorizationOptions options,
        WebSessionOptions sessionOptions)
    {
        // wirft bei unzulässiger Angabe, damit sie nicht verschleiert als Abweisung der
        // OIDC-Challenge beim IdP endet
        OpenIdConnectPublicRedirectExtensions.NormalizePublicBaseUrl(options.PublicBaseUrl);

        services.AddAuthentication(authenticationOptions =>
            {
                authenticationOptions.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                authenticationOptions.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie()
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, openIdConnectOptions =>
            {
                openIdConnectOptions.Authority = options.Authority;
                openIdConnectOptions.ClientId = options.ClientId;
                openIdConnectOptions.ClientSecret = null;
                openIdConnectOptions.ResponseType = "code";
                openIdConnectOptions.Scope.Add("openid");
                openIdConnectOptions.Scope.Add("profile");
                openIdConnectOptions.Scope.Add(options.RequiredUserScope);
                openIdConnectOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;
                openIdConnectOptions.TokenValidationParameters.ValidIssuer = options.Issuer ?? options.Authority;

                openIdConnectOptions.RegisterBallwareWebSessionTokenHandling();
                openIdConnectOptions.RegisterBallwarePublicRedirectUriHandling(options);
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwtBearerOptions =>
            {
                jwtBearerOptions.MapInboundClaims = false;
                jwtBearerOptions.Authority = options.Authority;
                jwtBearerOptions.Audience = options.Audience;
                jwtBearerOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;
                jwtBearerOptions.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
                {
                    ValidIssuer = options.Issuer ?? options.Authority
                };
            });

        services.AddSingleton<ConfigurationManager<OpenIdConnectConfiguration>>(sp =>
        {
            var openIdConnectOptions = sp.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(OpenIdConnectDefaults.AuthenticationScheme);

            var retriever = new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata };

            return new ConfigurationManager<OpenIdConnectConfiguration>(
                openIdConnectOptions.Authority!.TrimEnd('/') + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(), retriever);
        });

        var userScopePolicyName =
            string.IsNullOrWhiteSpace(options.UserScopePolicyName) ? options.RequiredUserScope : options.UserScopePolicyName!;

        var authorizationBuilder = services.AddAuthorizationBuilder()
            .AddPolicy(userScopePolicyName, policy => RequireScopeAssertion(policy, options.RequiredUserScope));

        if (!string.IsNullOrWhiteSpace(options.ServiceScopePolicyName))
        {
            authorizationBuilder.AddPolicy(options.ServiceScopePolicyName, policy => RequireScopeAssertion(policy, options.ServiceScopePolicyName));
        }

        services.AddBallwareWebSession(sessionOptions);

        return options;
    }

    private static void RequireScopeAssertion(AuthorizationPolicyBuilder policy, string scope)
    {
        policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                context.User
                    .Claims
                    .Where(c => ClaimTypeScope == c.Type)
                    .SelectMany(c => c.Value.Split(' '))
                    .Any(s => s.Equals(scope, StringComparison.Ordinal)));
    }

    private sealed class BrowserAuthorizationOptionsConfigurator(BrowserAuthorizationOptions instance)
        : IConfigureOptions<BrowserAuthorizationOptions>
    {
        public void Configure(BrowserAuthorizationOptions options)
        {
            options.Authority = instance.Authority;
            options.Audience = instance.Audience;
            options.ClientId = instance.ClientId;
            options.Issuer = instance.Issuer;
            options.RequireHttpsMetadata = instance.RequireHttpsMetadata;
            options.RequiredUserScope = instance.RequiredUserScope;
            options.UserScopePolicyName = instance.UserScopePolicyName;
            options.ServiceScopePolicyName = instance.ServiceScopePolicyName;
            options.SignOnAllowedHosts = instance.SignOnAllowedHosts;
            options.SignOnAllowedRedirectPathPrefixes = instance.SignOnAllowedRedirectPathPrefixes;
            options.PublicBaseUrl = instance.PublicBaseUrl;
        }
    }
}
