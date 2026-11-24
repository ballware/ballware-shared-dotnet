# Ballware.Shared.Authentication

## Purpose & dependencies

JWT bearer authentication enrichment: after a token validates, optionally fetch the identity
provider's OIDC `/userinfo` endpoint and merge additional claims into the principal — scope-gated,
single-flight cached, size- and claim-count-limited, sub-matched, with protected core claims and a
hard-fail policy. This is the **bearer path** of web authentication (the cookie path is
[ballware-shared-web-session.md](ballware-shared-web-session.md); overview in
[concepts/web-authentication-and-sessions.md](../concepts/web-authentication-and-sessions.md)).

Dependencies: Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.Extensions.Caching.Memory,
Microsoft.Extensions.Configuration.Binder, Microsoft.Extensions.Http, Microsoft.Extensions.Options
(`src/Ballware.Shared.Authentication/Ballware.Shared.Authentication.csproj`).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `UserInfoEnrichmentOptions` | `Enabled` (default false), `Scopes` (list), `ScopeClaimType` (default `"scope"`, `[Required]`), `Authority?`, `Issuer?`, `UserinfoEndpoint?`, `CacheTtlSeconds = 60`, `NegativeCacheTtlSeconds = 10`, `TimeoutSeconds = 5`, `MaxResponseBytes = 262144`, `MaxClaims = 1000` | `src/Ballware.Shared.Authentication/UserInfoEnrichmentOptions.cs` |
| `IUserInfoEnricher.EnrichAsync(principal, accessToken, ct)` | Returns `UserInfoEnrichmentResult` | `src/Ballware.Shared.Authentication/IUserInfoEnricher.cs` |
| `UserInfoEnrichmentResult` / `UserInfoEnrichmentStatus` | `Status` ∈ `NotEnriched` / `Enriched` / `Failed`; `Enriched` carries the new `Principal` | `src/Ballware.Shared.Authentication/UserInfoEnrichmentResult.cs` |
| `ServiceCollectionExtensions.AddBallwareSharedUserInfoEnrichment` (2 overloads) | Bind from `IConfiguration` section (default `"Authorization:UserInfo"`) or from a configure delegate; registers options validation, memory cache, named HttpClient, singleton enricher | `src/Ballware.Shared.Authentication/ServiceCollectionExtensions.cs` |
| `JwtBearerEventsExtensions.AttachUserInfoEnrichment(events)` | Hooks `OnTokenValidated`: chains any previously configured handler, extracts the `Bearer` token from the Authorization header, and applies the enricher result | `src/Ballware.Shared.Authentication/JwtBearerEventsExtensions.cs` |
| `UserInfoEnricher` (public impl) | `EnrichAsync` implementation incl. the protected-claims set | `src/Ballware.Shared.Authentication/UserInfoEnricher.cs` → `UserInfoEnricher.EnrichAsync`, `ProtectedClaims` |

## Wiring

```csharp
services.AddBallwareSharedUserInfoEnrichment(Configuration);   // section "Authorization:UserInfo"

services.AddAuthentication()
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents { /* host handlers... */ }.AttachUserInfoEnrichment();
    });
```

Both `AddBallwareSharedUserInfoEnrichment` overloads validate **on start**: when `Enabled`,
`Scopes` must be non-empty and either `UserinfoEndpoint` or `Authority` must be set
(`ServiceCollectionExtensions.AddBallwareSharedUserInfoEnrichmentCore` validation lambdas).

## Behavior & limits

- **Scope gate:** enrichment runs only when a claim of type `ScopeClaimType` *or* `scp` contains one
  of the configured `Scopes` (space-split, ordinal). Otherwise `NotEnriched` — no request is made.
- **Subject match:** the token needs `sub` (or `ClaimTypes.NameIdentifier`); the userinfo response's
  `sub` must match it ordinally, else `Failed`.
- **Endpoint resolution:** explicit `UserinfoEndpoint` wins; otherwise discovery via
  `{Authority}/.well-known/openid-configuration` → `userinfo_endpoint` (discovery result cached for
  `CacheTtlSeconds`). No endpoint and no Authority ⇒ `Failed`.
- **Caching:** key `userinfo:{Issuer ?? Authority}:{azp}:{sub}`; positive results cached for
  `CacheTtlSeconds`, failures for `NegativeCacheTtlSeconds`; concurrent fetches for the same key are
  deduplicated (single-flight `ConcurrentDictionary` of in-flight tasks).
- **Hard limits:** response larger than `MaxResponseBytes` ⇒ failed (checked both on `Content-Length`
  and while streaming); more than `MaxClaims` parsed claims ⇒ failed; HTTP request timeout
  `TimeoutSeconds` (client timeout `TimeoutSeconds + 1`); the enricher HttpClient **does not follow
  redirects**.
- **Claim protection:** userinfo properties named `iss`, `aud`, `azp`, `sub`, `scope`, `scp`, `nbf`,
  `exp`, `iat`, `jti`, `nonce` are never added; existing principal claims are never duplicated or
  overwritten; array values become repeated claims; scalar JSON values become string claims with the
  raw JSON text.
- **Failure semantics:** `Failed` **fails authentication** — `AttachUserInfoEnrichment` calls
  `context.Fail(...)` (`src/Ballware.Shared.Authentication/JwtBearerEventsExtensions.cs` →
  `AttachUserInfoEnrichment`); `NotEnriched` passes the original principal through untouched;
  `Enriched` replaces `context.Principal`.
- If no enricher is registered, `AttachUserInfoEnrichment` is a no-op passthrough; a previously
  configured `OnTokenValidated` handler is always invoked first.

## Implementation details

*Implementation detail, not contract:* the single-flight map is per enricher instance (singleton);
the enriched principal is rebuilt with a new `ClaimsIdentity` set preserving authentication and name/role
claim types, additions applied to the first identity only (`UserInfoEnricher.BuildEnrichedPrincipal`).

## Evidence

`test/Ballware.Shared.Authentication.Tests/JwtBearerEventsExtensionsTest.cs`:
`AttachUserInfoEnrichment_FailsAuthenticationOnFailedResult` (hard-fail),
`AttachUserInfoEnrichment_ReplacesPrincipalOnEnrichedResult`,
`AttachUserInfoEnrichment_KeepsPrincipalOnNotEnrichedResult`,
`AttachUserInfoEnrichment_PreservesPreviousOnTokenValidated`,
`AttachUserInfoEnrichment_WithoutRegisteredEnricher_IsNoOp`.

`test/Ballware.Shared.Authentication.Tests/UserInfoEnricherTest.cs`: scope gating
(`EnrichAsync_TokenWithoutConfiguredScope_DoesNotCallUserinfo_AndReturnsNotEnriched`,
`EnrichAsync_ScpClaimVariant_MatchesConfiguredScope`), caching
(`EnrichAsync_CacheHit_CallsUserinfoOnlyOnce`, `EnrichAsync_NegativeCache_PreventsImmediateRetry`),
limits (`EnrichAsync_ResponseExceedingMaxBytes_Fails`, `EnrichAsync_ExceedingMaxClaims_Fails`),
matching/protection (`EnrichAsync_UserinfoSubMismatch_Fails`,
`EnrichAsync_ProtectedUserinfoClaims_AreIgnored`, `EnrichAsync_IdenticalClaims_AreNotDuplicated`),
discovery (`EnrichAsync_WithoutConfiguredEndpoint_UsesOpenIdDiscovery`,
`EnrichAsync_WithoutEndpointAndWithoutAuthority_Fails`).

Registration/validation: `test/Ballware.Shared.Authentication.Tests/ServiceCollectionExtensionsTest.cs`
(`AddBallwareSharedUserInfoEnrichment_EnabledWithoutScopes_ThrowsOnOptionsResolve`,
`AddBallwareSharedUserInfoEnrichment_EnabledWithoutAuthorityOrEndpoint_ThrowsOnOptionsResolve`).
