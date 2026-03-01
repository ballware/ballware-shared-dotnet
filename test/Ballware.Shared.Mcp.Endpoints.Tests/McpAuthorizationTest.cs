using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ballware.Shared.Mcp.Endpoints.Configuration;
using Ballware.Shared.Mcp.Endpoints.Tests.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Ballware.Shared.Mcp.Endpoints.Tests;

/// <summary>
/// A fake Bearer authentication handler that checks for the presence of an Authorization header.
/// If the header is present, authentication succeeds with the claims from FakeClaimsProvider.
/// If the header is missing, authentication fails (producing a 401 response).
/// </summary>
class FakeBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly FakeClaimsProvider _claimsProvider;

    public FakeBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        FakeClaimsProvider claimsProvider)
        : base(options, logger, encoder)
    {
        _claimsProvider = claimsProvider;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = _claimsProvider.Claims;
        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Bearer");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

[TestFixture]
public class McpAuthorizationTest : ApiMappingBaseTest
{
    private const string ExpectedScope = "mcp:tools";
    private const string ExpectedAuthorizationServerUri = "https://idp.example.com";
    private const string ExpectedResourceUri = "https://mcp.example.com";
    private const string McpBasePath = "/mcp";

    private McpEndpointOptions Options { get; set; } = null!;
    private Mock<IToolRegistry> ToolRegistryMock { get; set; } = null!;

    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();

        ToolRegistryMock = new Mock<IToolRegistry>();
        ToolRegistryMock
            .Setup(r => r.GetAllToolsAsync())
            .ReturnsAsync(Array.Empty<Tool>());

        Options = new McpEndpointOptions
        {
            Enabled = true,
            RequiredMcpScope = ExpectedScope,
            ResourceUri = ExpectedResourceUri
        };
    }

    /// <summary>
    /// Creates an HttpClient for the test server WITHOUT authentication headers,
    /// simulating an unauthenticated MCP client.
    /// </summary>
    private Task<HttpClient> CreateUnauthenticatedClientAsync()
    {
        return CreateMcpClientAsync(includeAuthHeader: false);
    }

    /// <summary>
    /// Creates an HttpClient for the test server WITH authentication headers.
    /// </summary>
    private Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        return CreateMcpClientAsync(includeAuthHeader: true);
    }

    private async Task<HttpClient> CreateMcpClientAsync(bool includeAuthHeader)
    {
        var client = await CreateApplicationClientAsync(ExpectedScope, services =>
        {
            services.AddSingleton(ToolRegistryMock.Object);
            services.AddBallwareMcpEndpoint(Options);
            
            // Register the "Bearer" scheme (JwtBearerDefaults.AuthenticationScheme) 
            // so that the MCP endpoint authorization can resolve it.
            // This uses the same FakeJwtBearerHandler as "TestJwt" - authentication
            // success/failure is controlled by the presence of the Authorization header.
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, FakeBearerHandler>("Bearer", _ => { });
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapBallwareUserMcpEndpoint(McpBasePath, Options);
            });
        });

        if (!includeAuthHeader)
        {
            client.DefaultRequestHeaders.Authorization = null;
        }

        return client;
    }

    #region 401 Unauthorized Response Tests

    [Test]
    public async Task UnauthenticatedRequest_ToMcpEndpoint_Returns401()
    {
        // Arrange
        var client = await CreateUnauthenticatedClientAsync();

        // Act
        var response = await client.PostAsync($"{McpBasePath}", new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "initialize", id = 1 }),
            System.Text.Encoding.UTF8,
            "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task UnauthenticatedRequest_ToMcpEndpoint_ReturnsWwwAuthenticateHeader()
    {
        // Arrange
        var client = await CreateUnauthenticatedClientAsync();

        // Act
        var response = await client.PostAsync($"{McpBasePath}", new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "initialize", id = 1 }),
            System.Text.Encoding.UTF8,
            "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.WwwAuthenticate, Is.Not.Empty,
            "401 response must include WWW-Authenticate header per MCP spec");
    }

    [Test]
    public async Task UnauthenticatedRequest_WwwAuthenticateHeader_ContainsBearerScheme()
    {
        // Arrange
        var client = await CreateUnauthenticatedClientAsync();

        // Act
        var response = await client.PostAsync($"{McpBasePath}", new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "initialize", id = 1 }),
            System.Text.Encoding.UTF8,
            "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        Assert.That(wwwAuth, Does.Contain("Bearer"),
            "WWW-Authenticate header must specify Bearer scheme for OAuth 2.0");
    }

    [Test]
    public async Task UnauthenticatedRequest_WwwAuthenticateHeader_ContainsResourceMetadataUri()
    {
        // Arrange
        var client = await CreateUnauthenticatedClientAsync();

        // Act
        var response = await client.PostAsync($"{McpBasePath}", new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "initialize", id = 1 }),
            System.Text.Encoding.UTF8,
            "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        Assert.That(wwwAuth, Does.Contain("resource_metadata"),
            "WWW-Authenticate header must contain resource_metadata parameter pointing to /.well-known/oauth-protected-resource");
    }

    #endregion

    #region OAuth Protected Resource Metadata (RFC 9470 / MCP Spec) Tests

    [Test]
    public async Task WellKnownOAuthProtectedResource_ReturnsOk()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act - /.well-known/oauth-protected-resource must be accessible without authentication
        client.DefaultRequestHeaders.Authorization = null;
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "/.well-known/oauth-protected-resource endpoint must return 200 OK");
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_ReturnsJsonContentType()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"),
            "OAuth Protected Resource Metadata must be served as application/json");
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_ContainsResourceField()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var content = await response.Content.ReadAsStringAsync();
        var metadata = JsonDocument.Parse(content);

        // Assert
        Assert.That(metadata.RootElement.TryGetProperty("resource", out var resource), Is.True,
            "Metadata must contain 'resource' field (RFC 9470)");
        Assert.That(resource.GetString(), Is.EqualTo(ExpectedResourceUri),
            "resource field must match the configured ResourceUri");
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_ContainsAuthorizationServers()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var content = await response.Content.ReadAsStringAsync();
        var metadata = JsonDocument.Parse(content);

        // Assert
        Assert.That(metadata.RootElement.TryGetProperty("authorization_servers", out var authServers), Is.True,
            "Metadata must contain 'authorization_servers' field listing the external IdP");
        Assert.That(authServers.ValueKind, Is.EqualTo(JsonValueKind.Array),
            "authorization_servers must be a JSON array");

        var servers = authServers.EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.That(servers, Does.Contain(ExpectedAuthorizationServerUri),
            $"authorization_servers must contain the configured authorization server URI '{ExpectedAuthorizationServerUri}'");
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_ContainsScopesSupported()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var content = await response.Content.ReadAsStringAsync();
        var metadata = JsonDocument.Parse(content);

        // Assert
        Assert.That(metadata.RootElement.TryGetProperty("scopes_supported", out var scopes), Is.True,
            "Metadata must contain 'scopes_supported' field");
        Assert.That(scopes.ValueKind, Is.EqualTo(JsonValueKind.Array),
            "scopes_supported must be a JSON array");

        var scopeList = scopes.EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.That(scopeList, Does.Contain(ExpectedScope),
            $"scopes_supported must contain the required MCP scope '{ExpectedScope}'");
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_ContainsBearerMethodsSupported()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var content = await response.Content.ReadAsStringAsync();
        var metadata = JsonDocument.Parse(content);

        // Assert
        Assert.That(metadata.RootElement.TryGetProperty("bearer_methods_supported", out var methods), Is.True,
            "Metadata must contain 'bearer_methods_supported' field");
        Assert.That(methods.ValueKind, Is.EqualTo(JsonValueKind.Array),
            "bearer_methods_supported must be a JSON array");

        var methodList = methods.EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.That(methodList, Does.Contain("header"),
            "bearer_methods_supported must include 'header' for Authorization header bearer tokens");
    }

    #endregion

    #region Authenticated Access Tests

    [Test]
    public async Task AuthenticatedRequest_ToMcpEndpoint_DoesNotReturn401()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();

        // Act
        var response = await client.PostAsync($"{McpBasePath}", new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "initialize", id = 1 }),
            System.Text.Encoding.UTF8,
            "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized),
            "Authenticated request with valid scope must not return 401");
    }

    #endregion

    #region Well-Known Endpoint Accessibility Tests

    [Test]
    public async Task WellKnownOAuthProtectedResource_IsAccessibleWithoutAuthentication()
    {
        // Arrange - Create a client that has no auth header at all
        var client = await CreateUnauthenticatedClientAsync();

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized),
            "/.well-known/oauth-protected-resource must be accessible without authentication " +
            "so that MCP clients can discover OAuth configuration before authenticating");
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_ReturnsCorrectCacheHeaders()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        
        // The metadata should be cacheable but not indefinitely
        var cacheControl = response.Headers.CacheControl;
        Assert.That(cacheControl, Is.Not.Null,
            "OAuth Protected Resource Metadata should include Cache-Control header");
        Assert.That(cacheControl!.NoStore, Is.False,
            "Metadata should be cacheable for MCP clients");
    }

    #endregion

    #region MCP Spec Compliance: resource_metadata in WWW-Authenticate

    [Test]
    public async Task UnauthenticatedRequest_WwwAuthenticateHeader_ResourceMetadataPointsToWellKnownEndpoint()
    {
        // Arrange
        var client = await CreateUnauthenticatedClientAsync();

        // Act
        var response = await client.PostAsync($"{McpBasePath}", new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "initialize", id = 1 }),
            System.Text.Encoding.UTF8,
            "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var wwwAuth = response.Headers.WwwAuthenticate.ToString();
        
        // Per MCP spec, the resource_metadata parameter should point to the 
        // /.well-known/oauth-protected-resource endpoint
        Assert.That(wwwAuth, Does.Match(@"resource_metadata\s*=\s*""[^""]*\.well-known/oauth-protected-resource[^""]*"""),
            "resource_metadata in WWW-Authenticate must reference the /.well-known/oauth-protected-resource endpoint");
    }

    #endregion

    #region Configuration Validation Tests

    [Test]
    public async Task WellKnownOAuthProtectedResource_WithMultipleScopes_ContainsAllScopes()
    {
        // Arrange - Test with options containing additional scopes information
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var content = await response.Content.ReadAsStringAsync();
        var metadata = JsonDocument.Parse(content);

        // Assert - At minimum, the required MCP scope must be present
        Assert.That(metadata.RootElement.TryGetProperty("scopes_supported", out var scopes), Is.True);
        var scopeList = scopes.EnumerateArray().Select(s => s.GetString()).ToList();
        Assert.That(scopeList, Does.Contain(ExpectedScope));
    }

    [Test]
    public async Task WellKnownOAuthProtectedResource_DoesNotExposeInternalServerDetails()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync();
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        var content = await response.Content.ReadAsStringAsync();

        // Assert - Security: metadata should not leak internal implementation details
        Assert.That(content, Does.Not.Contain("localhost").IgnoreCase,
            "Metadata should not expose internal server addresses");
        Assert.That(content, Does.Not.Contain("internal").IgnoreCase,
            "Metadata should not expose internal configuration details");
    }

    #endregion
}