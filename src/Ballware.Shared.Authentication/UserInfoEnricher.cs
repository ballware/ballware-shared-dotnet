using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ballware.Shared.Authentication;

public class UserInfoEnricher(
    IOptions<UserInfoEnrichmentOptions> options,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<UserInfoEnricher> logger) : IUserInfoEnricher
{
    public const string HttpClientName = "ballware-shared-userinfo-enrichment";

    private static readonly HashSet<string> ProtectedClaims = new(StringComparer.Ordinal)
    {
        "iss",
        "aud",
        "azp",
        "sub",
        "scope",
        "scp",
        "nbf",
        "exp",
        "iat",
        "jti",
        "nonce"
    };

    private readonly UserInfoEnrichmentOptions _options = options.Value;

    private readonly ConcurrentDictionary<string, Task<CachedEntry>> _inFlight = new();

    private sealed record CachedEntry(bool Success, IReadOnlyList<UserInfoClaim> Claims);

    private sealed record UserInfoClaim(string Type, string Value);

    public async Task<UserInfoEnrichmentResult> EnrichAsync(
        ClaimsPrincipal principal,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return UserInfoEnrichmentResult.NotEnriched();
        }

        if (!HasConfiguredScope(principal))
        {
            return UserInfoEnrichmentResult.NotEnriched();
        }

        var sub = GetSubject(principal);

        if (sub == null)
        {
            logger.LogWarning("Userinfo enrichment failed because the token has no subject claim.");
            return UserInfoEnrichmentResult.Failed();
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            logger.LogWarning("Userinfo enrichment failed because no access token is available for subject {Sub}.", sub);
            return UserInfoEnrichmentResult.Failed();
        }

        var azp = principal.FindFirst("azp")?.Value ?? "unknown";
        var authority = _options.Issuer ?? _options.Authority;
        var cacheKey = $"userinfo:{authority}:{azp}:{sub}";

        CachedEntry entry;

        try
        {
            entry = await GetOrFetchAsync(cacheKey, accessToken, sub, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Userinfo enrichment failed unexpectedly for subject {Sub}.", sub);
            return UserInfoEnrichmentResult.Failed();
        }

        if (!entry.Success)
        {
            return UserInfoEnrichmentResult.Failed();
        }

        var additions = BuildClaimAdditions(principal, entry.Claims);

        if (additions.Count == 0)
        {
            return UserInfoEnrichmentResult.NotEnriched();
        }

        var enrichedPrincipal = BuildEnrichedPrincipal(principal, additions);

        logger.LogDebug("Userinfo enrichment added {ClaimCount} claims for subject {Sub}.", additions.Count, sub);

        return UserInfoEnrichmentResult.Enriched(enrichedPrincipal);
    }

    private bool HasConfiguredScope(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.Claims)
        {
            if (!string.Equals(claim.Type, _options.ScopeClaimType, StringComparison.Ordinal) &&
                !string.Equals(claim.Type, "scp", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (_options.Scopes.Any(configured => string.Equals(configured, scope, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? GetSubject(ClaimsPrincipal principal)
    {
        var value = principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<CachedEntry> GetOrFetchAsync(
        string cacheKey,
        string accessToken,
        string sub,
        CancellationToken cancellationToken)
    {
        if (memoryCache.TryGetValue(cacheKey, out CachedEntry? cached) && cached != null)
        {
            return cached;
        }

        var completionSource = new TaskCompletionSource<CachedEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlightTask = _inFlight.GetOrAdd(cacheKey, completionSource.Task);

        if (inFlightTask != completionSource.Task)
        {
            return await inFlightTask;
        }

        CachedEntry entry;

        try
        {
            entry = await FetchAsync(accessToken, sub, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RemoveInFlight(cacheKey, completionSource.Task);
            completionSource.SetCanceled(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Userinfo retrieval failed for subject {Sub}.", sub);
            entry = new CachedEntry(false, []);
        }

        var ttl = entry.Success ? _options.CacheTtlSeconds : _options.NegativeCacheTtlSeconds;
        memoryCache.Set(cacheKey, entry, TimeSpan.FromSeconds(ttl));
        RemoveInFlight(cacheKey, completionSource.Task);
        completionSource.SetResult(entry);

        return entry;
    }

    private void RemoveInFlight(string cacheKey, Task<CachedEntry> task)
    {
        if (_inFlight.TryRemove(cacheKey, out var existing) && existing != task)
        {
            _inFlight.TryAdd(cacheKey, existing);
        }
    }

    private async Task<CachedEntry> FetchAsync(string accessToken, string sub, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var endpoint = _options.UserinfoEndpoint;

        if (string.IsNullOrEmpty(endpoint))
        {
            endpoint = await DiscoverUserInfoEndpointAsync(client, cancellationToken);

            if (string.IsNullOrEmpty(endpoint))
            {
                return new CachedEntry(false, []);
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Userinfo endpoint returned status {StatusCode} for subject {Sub}.", (int)response.StatusCode, sub);
            return new CachedEntry(false, []);
        }

        var body = await ReadLimitedAsync(response.Content, timeoutCts.Token);

        if (body == null)
        {
            logger.LogWarning("Userinfo response exceeded {MaxBytes} bytes for subject {Sub}.", _options.MaxResponseBytes, sub);
            return new CachedEntry(false, []);
        }

        return ParseUserInfo(body, sub);
    }

    private async Task<string?> DiscoverUserInfoEndpointAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var authority = _options.Authority;

        if (string.IsNullOrEmpty(authority))
        {
            logger.LogWarning("Userinfo endpoint discovery requires UserInfoEnrichmentOptions.Authority to be configured.");
            return null;
        }

        var cacheKey = $"userinfo-discovery:{authority}";

        if (memoryCache.TryGetValue(cacheKey, out string? cachedEndpoint) && !string.IsNullOrEmpty(cachedEndpoint))
        {
            return cachedEndpoint;
        }

        var metadataUri = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var response = await client.GetAsync(metadataUri, timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenID Connect metadata request for {MetadataUri} returned status {StatusCode}.", metadataUri, (int)response.StatusCode);
            return null;
        }

        var body = await ReadLimitedAsync(response.Content, timeoutCts.Token);

        if (body == null)
        {
            logger.LogWarning("OpenID Connect metadata response for {MetadataUri} exceeded {MaxBytes} bytes.", metadataUri, _options.MaxResponseBytes);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("userinfo_endpoint", out var endpointElement) &&
                endpointElement.ValueKind == JsonValueKind.String)
            {
                var endpoint = endpointElement.GetString();

                if (!string.IsNullOrEmpty(endpoint))
                {
                    memoryCache.Set(cacheKey, endpoint, TimeSpan.FromSeconds(_options.CacheTtlSeconds));
                    return endpoint;
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "OpenID Connect metadata response for {MetadataUri} was not valid JSON.", metadataUri);
            return null;
        }

        logger.LogWarning("OpenID Connect metadata for {MetadataUri} did not contain a userinfo_endpoint.", metadataUri);
        return null;
    }

    private async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers?.ContentLength > _options.MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;

            if (total > _options.MaxResponseBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private CachedEntry ParseUserInfo(byte[] body, string sub)
    {
        JsonDocument doc;

        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Userinfo response for subject {Sub} was not valid JSON.", sub);
            return new CachedEntry(false, []);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("Userinfo response for subject {Sub} was not a JSON object.", sub);
                return new CachedEntry(false, []);
            }

            if (!doc.RootElement.TryGetProperty("sub", out var subElement) ||
                subElement.ValueKind != JsonValueKind.String ||
                !string.Equals(subElement.GetString(), sub, StringComparison.Ordinal))
            {
                logger.LogWarning("Userinfo subject does not match the token subject for subject {Sub}.", sub);
                return new CachedEntry(false, []);
            }

            var claims = new List<UserInfoClaim>();

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (ProtectedClaims.Contains(property.Name))
                {
                    continue;
                }

                var value = property.Value;

                switch (value.ValueKind)
                {
                    case JsonValueKind.Null:
                        break;
                    case JsonValueKind.String:
                        claims.Add(new UserInfoClaim(property.Name, value.GetString() ?? string.Empty));
                        break;
                    case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                        claims.Add(new UserInfoClaim(property.Name, value.GetRawText()));
                        break;
                    case JsonValueKind.Array:
                        foreach (var element in value.EnumerateArray())
                        {
                            switch (element.ValueKind)
                            {
                                case JsonValueKind.Null:
                                    break;
                                case JsonValueKind.String:
                                    claims.Add(new UserInfoClaim(property.Name, element.GetString() ?? string.Empty));
                                    break;
                                case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                                    claims.Add(new UserInfoClaim(property.Name, element.GetRawText()));
                                    break;
                                default:
                                    claims.Add(new UserInfoClaim(property.Name, element.GetRawText()));
                                    break;
                            }
                        }

                        break;
                    default:
                        claims.Add(new UserInfoClaim(property.Name, value.GetRawText()));
                        break;
                }

                if (claims.Count > _options.MaxClaims)
                {
                    logger.LogWarning("Userinfo response for subject {Sub} exceeds {MaxClaims} claims.", sub, _options.MaxClaims);
                    return new CachedEntry(false, []);
                }
            }

            return new CachedEntry(true, claims);
        }
    }

    private static List<Claim> BuildClaimAdditions(ClaimsPrincipal principal, IReadOnlyList<UserInfoClaim> cachedClaims)
    {
        var existing = new HashSet<(string Type, string Value)>(principal.Claims.Select(c => (c.Type, c.Value)));
        var additions = new List<Claim>();

        foreach (var cachedClaim in cachedClaims)
        {
            if (ProtectedClaims.Contains(cachedClaim.Type))
            {
                continue;
            }

            if (existing.Add((cachedClaim.Type, cachedClaim.Value)))
            {
                additions.Add(new Claim(cachedClaim.Type, cachedClaim.Value));
            }
        }

        return additions;
    }

    private static ClaimsPrincipal BuildEnrichedPrincipal(ClaimsPrincipal original, List<Claim> additions)
    {
        var principal = new ClaimsPrincipal();
        var additionsApplied = false;

        foreach (var identity in original.Identities)
        {
            var claims = additionsApplied ? identity.Claims : identity.Claims.Concat(additions);

            var enrichedIdentity = new ClaimsIdentity(
                claims,
                identity.AuthenticationType,
                identity.NameClaimType,
                identity.RoleClaimType);

            principal.AddIdentity(enrichedIdentity);
            additionsApplied = true;
        }

        return principal;
    }
}
