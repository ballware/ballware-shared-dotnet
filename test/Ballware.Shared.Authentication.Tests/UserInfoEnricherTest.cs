using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Ballware.Shared.Authentication.Tests;

public class UserInfoEnricherTest
{
    private const string UserinfoUrl = "https://idp.example/realms/ballware/protocol/openid-connect/userinfo";
    private const string MetadataUrl = "https://idp.example/realms/ballware/.well-known/openid-configuration";

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());

            return Task.FromResult(responder(request));
        }
    }

    private static UserInfoEnrichmentOptions DefaultOptions()
    {
        return new UserInfoEnrichmentOptions
        {
            Enabled = true,
            Scopes = ["metaApi"],
            Authority = "https://idp.example/realms/ballware",
            UserinfoEndpoint = UserinfoUrl
        };
    }

    private static ClaimsPrincipal CreatePrincipal(
        string? scope = "metaApi",
        string sub = "user-1",
        string? azp = null,
        params Claim[] extraClaims)
    {
        var identity = new ClaimsIdentity("test-auth");

        if (!string.IsNullOrEmpty(sub))
        {
            identity.AddClaim(new Claim("sub", sub));
        }

        if (scope != null)
        {
            identity.AddClaim(new Claim("scope", scope));
        }

        if (azp != null)
        {
            identity.AddClaim(new Claim("azp", azp));
        }

        foreach (var extraClaim in extraClaims)
        {
            identity.AddClaim(extraClaim);
        }

        return new ClaimsPrincipal(identity);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static (UserInfoEnricher Enricher, StubHttpMessageHandler Handler) CreateEnricher(
        UserInfoEnrichmentOptions options,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var httpClient = new HttpClient(handler);

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(UserInfoEnricher.HttpClientName)).Returns(httpClient);

        var enricher = new UserInfoEnricher(
            Options.Create(options),
            httpClientFactory.Object,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<ILogger<UserInfoEnricher>>().Object);

        return (enricher, handler);
    }

    [Test]
    public async Task EnrichAsync_Disabled_DoesNotCallUserinfo_AndReturnsNotEnriched()
    {
        var options = DefaultOptions();
        options.Enabled = false;

        var (enricher, handler) = CreateEnricher(options, _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.NotEnriched));
            Assert.That(handler.RequestedUrls, Is.Empty);
        });
    }

    [Test]
    public async Task EnrichAsync_TokenWithoutConfiguredScope_DoesNotCallUserinfo_AndReturnsNotEnriched()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(scope: "openid profile"), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.NotEnriched));
            Assert.That(handler.RequestedUrls, Is.Empty);
        });
    }

    [Test]
    public async Task EnrichAsync_TokenWithMetaScope_CallsUserinfo_AndAddsDomainClaims()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1","right":["read","write"]}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal, Is.Not.Null);
            Assert.That(handler.RequestedUrls, Has.Count.EqualTo(1));
            Assert.That(result.Principal!.FindAll("tenant").Select(c => c.Value), Is.EquivalentTo(new[] {"t1"}));
            Assert.That(result.Principal!.FindAll("right").Select(c => c.Value), Is.EquivalentTo(new[] {"read", "write"}));
            Assert.That(result.Principal!.FindAll("scope").Select(c => c.Value), Is.EquivalentTo(new[] {"metaApi"}));
            Assert.That(result.Principal!.Identities.First().AuthenticationType, Is.EqualTo("test-auth"));
        });
    }

    [Test]
    public async Task EnrichAsync_ServiceApiTokenNotInPositiveList_ReturnsNotEnriched()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(scope: "serviceApi"), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.NotEnriched));
            Assert.That(handler.RequestedUrls, Is.Empty);
        });
    }

    [Test]
    public async Task EnrichAsync_TokenWithMetaAndServiceScope_Enriches()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(scope: "metaApi serviceApi"), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindFirst("tenant")?.Value, Is.EqualTo("t1"));
        });
    }

    [Test]
    public async Task EnrichAsync_AllNonProtectedUserinfoClaims_AreAdded()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","foo":"f","bar":42,"baz":true,"custom":{"a":1}}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindFirst("foo")?.Value, Is.EqualTo("f"));
            Assert.That(result.Principal!.FindFirst("bar")?.Value, Is.EqualTo("42"));
            Assert.That(result.Principal!.FindFirst("baz")?.Value, Is.EqualTo("true"));
            Assert.That(result.Principal!.FindFirst("custom")?.Value, Is.EqualTo("""{"a":1}"""));
        });
    }

    [Test]
    public async Task EnrichAsync_IdenticalClaims_AreNotDuplicated()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var principal = CreatePrincipal(extraClaims: new Claim("tenant", "t1"));

        var result = await enricher.EnrichAsync(principal, "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.NotEnriched));
            Assert.That(result.Principal, Is.Null);
            Assert.That(principal.FindAll("tenant"), Has.Exactly(1).Items);
        });
    }

    [Test]
    public async Task EnrichAsync_ProtectedUserinfoClaims_AreIgnored()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","scope":"metaApi other","aud":"other-aud","azp":"other-client","iss":"other-iss","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindAll("tenant").Select(c => c.Value), Is.EquivalentTo(new[] {"t1"}));
            Assert.That(result.Principal!.FindAll("scope").Select(c => c.Value), Is.EquivalentTo(new[] {"metaApi"}));
            Assert.That(result.Principal!.FindAll("aud"), Is.Empty);
            Assert.That(result.Principal!.FindAll("azp"), Is.Empty);
            Assert.That(result.Principal!.FindAll("iss"), Is.Empty);
        });
    }

    [Test]
    public async Task EnrichAsync_UserinfoSubMismatch_Fails()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"other-user","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
    }

    [Test]
    public async Task EnrichAsync_UserinfoHttpError_Fails()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
    }

    [Test]
    public async Task EnrichAsync_TokenWithoutSub_Fails()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(sub: ""), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
            Assert.That(handler.RequestedUrls, Is.Empty);
        });
    }

    [Test]
    public async Task EnrichAsync_CacheHit_CallsUserinfoOnlyOnce()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var first = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);
        var second = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(second.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(handler.RequestedUrls, Has.Count.EqualTo(1));
            Assert.That(second.Principal!.FindFirst("tenant")?.Value, Is.EqualTo("t1"));
        });
    }

    [Test]
    public async Task EnrichAsync_NegativeCache_PreventsImmediateRetry()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var first = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);
        var second = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
            Assert.That(second.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
            Assert.That(handler.RequestedUrls, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task EnrichAsync_ArrayOfScalars_AddsMultipleClaims()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","roles":["a","b","c"]}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindAll("roles").Select(c => c.Value), Is.EquivalentTo(new[] {"a", "b", "c"}));
        });
    }

    [Test]
    public async Task EnrichAsync_ArrayOfObjects_AddsRawJsonClaims()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","members":[{"id":1},{"id":2}]}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindAll("members").Select(c => c.Value),
                Is.EquivalentTo(new[] {"""{"id":1}""", """{"id":2}"""}));
        });
    }

    [Test]
    public async Task EnrichAsync_ComplexObjectValue_AddsRawJsonClaim()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","profile":{"a":1,"b":[1,2]}}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindFirst("profile")?.Value, Is.EqualTo("""{"a":1,"b":[1,2]}"""));
        });
    }

    [Test]
    public async Task EnrichAsync_WithoutConfiguredEndpoint_UsesOpenIdDiscovery()
    {
        var options = DefaultOptions();
        options.UserinfoEndpoint = null;

        var (enricher, handler) = CreateEnricher(options, request =>
            request.RequestUri!.ToString() == MetadataUrl
                ? JsonResponse($$"""
                    {"userinfo_endpoint":"{{UserinfoUrl}}"}
                    """)
                : JsonResponse("""
                    {"sub":"user-1","tenant":"t1"}
                    """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindFirst("tenant")?.Value, Is.EqualTo("t1"));
            Assert.That(handler.RequestedUrls, Is.EqualTo(new[] {MetadataUrl, UserinfoUrl}));
        });
    }

    [Test]
    public async Task EnrichAsync_WithoutEndpointAndWithoutAuthority_Fails()
    {
        var options = DefaultOptions();
        options.UserinfoEndpoint = null;
        options.Authority = null;

        var (enricher, _) = CreateEnricher(options, _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
    }

    [Test]
    public async Task EnrichAsync_ScpClaimVariant_MatchesConfiguredScope()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var identity = new ClaimsIdentity("test-auth");
        identity.AddClaim(new Claim("sub", "user-1"));
        identity.AddClaim(new Claim("scp", "metaApi"));

        var result = await enricher.EnrichAsync(new ClaimsPrincipal(identity), "token", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Enriched));
            Assert.That(result.Principal!.FindFirst("tenant")?.Value, Is.EqualTo("t1"));
        });
    }

    [Test]
    public async Task EnrichAsync_ResponseExceedingMaxBytes_Fails()
    {
        var options = DefaultOptions();
        options.MaxResponseBytes = 32;

        var (enricher, _) = CreateEnricher(options, _ => JsonResponse("""
            {"sub":"user-1","tenant":"a-very-long-tenant-value-that-exceeds-the-limit"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
    }

    [Test]
    public async Task EnrichAsync_ExceedingMaxClaims_Fails()
    {
        var options = DefaultOptions();
        options.MaxClaims = 2;

        var (enricher, _) = CreateEnricher(options, _ => JsonResponse("""
            {"sub":"user-1","a":"1","b":"2","c":"3","d":"4"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
    }

    [Test]
    public async Task EnrichAsync_NonObjectUserinfoResponse_Fails()
    {
        var (enricher, _) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            [1,2,3]
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "token", CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
    }

    [Test]
    public async Task EnrichAsync_EmptyAccessToken_Fails()
    {
        var (enricher, handler) = CreateEnricher(DefaultOptions(), _ => JsonResponse("""
            {"sub":"user-1","tenant":"t1"}
            """));

        var result = await enricher.EnrichAsync(CreatePrincipal(), "", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(UserInfoEnrichmentStatus.Failed));
            Assert.That(handler.RequestedUrls, Is.Empty);
        });
    }
}
