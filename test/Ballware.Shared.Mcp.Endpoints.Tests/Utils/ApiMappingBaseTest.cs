using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Mcp.Endpoints.Tests.Utils;

class FakeClaimsProvider
{
    public List<Claim> Claims { get; set; } = new List<Claim>();
}

class FakeJwtBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly FakeClaimsProvider _claimsProvider;

    public FakeJwtBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        FakeClaimsProvider claimsProvider)
        : base(options, logger, encoder)
    {
        _claimsProvider = claimsProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = _claimsProvider.Claims;

        var identity = new ClaimsIdentity(claims, "TestJwt");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestJwt");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public abstract class ApiMappingBaseTest
{
    private FakeClaimsProvider ClaimsProvider { get; set; } = null!;

    protected List<Claim> Claims => ClaimsProvider.Claims;

    private TestServer? Server { get; set; }

    [SetUp]
    public virtual Task SetUp()
    {
        ClaimsProvider = new FakeClaimsProvider();

        return Task.CompletedTask;
    }
    
    [TearDown]
    public virtual Task TearDown()
    {
        if (Server != null)
        {
            Server.Dispose();
        }
        
        return Task.CompletedTask;
    }

    protected Task<HttpClient> CreateApplicationClientAsync(
        string expectedScope,
        Action<IServiceCollection>? configureServices = null, 
        Action<IApplicationBuilder>? configureApp = null)
    {
        Claims.Add(new Claim("scope", expectedScope));
        
        Server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.Configure<JsonOptions>(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = null;
            });
            
            services.AddSingleton(ClaimsProvider);
            services
                .AddAuthorization(options =>
                {
                    options.AddPolicy(expectedScope, policy =>
                        policy.RequireClaim("scope", expectedScope));
                })
                .AddAuthentication("TestJwt")
                .AddScheme<AuthenticationSchemeOptions, FakeJwtBearerHandler>("TestJwt", _ => { });

            services.AddRouting();
            
            configureServices?.Invoke(services);
        }).Configure(app =>
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            
            configureApp?.Invoke(app);
        }));

        var client = Server.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestJwt");

        return Task.FromResult(client);
    }
}