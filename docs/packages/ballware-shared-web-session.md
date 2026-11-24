# Ballware.Shared.Web.Session

## Purpose & dependencies

Distributed cookie sessions for browser apps: after OIDC login the `ClaimsPrincipal` is stored as a
Data-Protection-protected authentication **ticket** in `IDistributedCache` behind a session cookie,
rehydrated on later requests, exposed as its own authentication scheme, with transport-path
differentiation (401 instead of an OIDC 302 challenge for paths that cannot follow redirects).
This is the **cookie path** of web authentication (overview in
[concepts/web-authentication-and-sessions.md](../concepts/web-authentication-and-sessions.md)).

Dependencies: Microsoft.AspNetCore.Authentication.OpenIdConnect
(`src/Ballware.Shared.Web.Session/Ballware.Shared.Web.Session.csproj`).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `WebSessionOptions` | `EnableSession = true`; `SessionCookieName = ".BallwareSession"`; `SessionCacheKeyPrefix = "ballware:session:"`; `SessionCacheExpirationInMinutes = 60`; `SuppressDefaultSessionCookie = true`; `DataProtectionPurpose = "Ballware.Web.Session.ClaimsPrincipal"` (all `[Required]` string fields non-empty, minutes `≥ 1`) | `src/Ballware.Shared.Web.Session/Configuration/WebSessionOptions.cs` |
| `SessionAuthenticationSchemes.DistributedSession` | Scheme name constant `"DistributedSession"` | `src/Ballware.Shared.Web.Session/SessionAuthenticationSchemes.cs` |
| `ISessionPrincipalProvider` | `GetPrincipalAsync(sessionId)`, `StorePrincipalAsync(HttpContext, ClaimsPrincipal)` | `src/Ballware.Shared.Web.Session/ISessionPrincipalProvider.cs` |
| `ServiceCollectionExtensions.AddBallwareWebSession(options)` | Registers options singleton, Data Protection, `ISessionPrincipalProvider`, the `DistributedSession` authentication scheme, and `AddSession()` when `EnableSession` | `src/Ballware.Shared.Web.Session/ServiceCollectionExtensions.cs` |
| `WebApplicationExtensions.UseBallwareWebSession()` | `UseSession()` (when enabled) + the rehydration middleware | `src/Ballware.Shared.Web.Session/WebApplicationExtensions.cs` → `UseBallwareWebSession` |
| `WebApplicationExtensions.UseBallwareChallengeDifferentiation(transportPathPrefixes?)` | Installs the challenge-differentiation middleware; optional prefix list overrides the defaults | `src/Ballware.Shared.Web.Session/WebApplicationExtensions.cs` → `UseBallwareChallengeDifferentiation` |
| `ChallengeDifferentiationMiddleware.DefaultTransportPathPrefixes` | `"/_blazor"`, `"/DXX"`, `"/dxDR"` (case-insensitive segment-prefix match) | `src/Ballware.Shared.Web.Session/ChallengeDifferentiationMiddleware.cs` |
| `OpenIdConnectSessionOptionsExtensions.RegisterBallwareWebSessionTokenHandling()` | OIDC hook: sets `SaveTokens = false`, `GetClaimsFromUserInfoEndpoint = false`, stores the principal at `OnTokenValidated` and suppresses the default cookie sign-in | `src/Ballware.Shared.Web.Session/OpenIdConnectSessionOptionsExtensions.cs` |

## Wiring

```csharp
var sessionOptions = new WebSessionOptions(); // or bound from host configuration
services.AddBallwareWebSession(sessionOptions);

// pipeline, in this order:
app.UseBallwareWebSession();
app.UseBallwareChallengeDifferentiation();          // before UseAuthentication
app.UseAuthentication();
```

Or let `AddBallwareBrowserAuthorization` compose it — see
[ballware-shared-web-oidc.md](ballware-shared-web-oidc.md).

## Behavior & limits

- **Ticket storage:** session id is a fresh `Guid`; the payload is
  `TicketSerializer.Default`-serialized `AuthenticationTicket(principal, "DistributedSession")`,
  protected with a Data Protection protector for `DataProtectionPurpose`, Base64-encoded, cached
  under `{SessionCacheKeyPrefix}{sessionId}` with absolute expiry of
  `SessionCacheExpirationInMinutes`. Raw OIDC tokens never enter the session
  (`SaveTokens = false` + `GetClaimsFromUserInfoEndpoint = false`).
- **Cookie:** `.BallwareSession` with `HttpOnly`, `Secure`, `SameSite=None`, `Path=/`, expiry =
  cache TTL. `SameSite=None` + `Secure` implies HTTPS-only deployment.
- **Rehydration:** `CachedClaimsPrincipalMiddleware` (installed by `UseBallwareWebSession`) reads
  the cookie **only when `context.User` is unauthenticated** and sets `context.User` from the stored
  ticket; an already-authenticated user is never overwritten.
- **`DistributedSession` scheme:** `SessionPrincipalAuthenticationHandler` succeeds when
  `Context.User` is authenticated (after rehydration) and re-wraps it as a ticket — policies can
  bind to `DistributedSession` after the middleware ran.
- **Challenge differentiation:** on transport paths (default prefixes above), an unauthenticated
  request is answered with **401** and never triggers the OIDC redirect; authenticated transport
  requests get the rehydrated principal. Non-transport paths pass through untouched and keep the
  normal OIDC challenge. Prefix list is overridable per call
  (`UseBallwareChallengeDifferentiation(new[] { "/mytransport" })`).
- **`RegisterBallwareWebSessionTokenHandling` suppression:** when `SuppressDefaultSessionCookie` is
  true, `OnTokenValidated` calls `HandleResponse()` and redirects directly to the OIDC return URL
  (relative return URL honored, non-relative or missing falls back to `"/"`) instead of the
  framework's cookie sign-in round-trip.
- Session cache expiry is absolute — there is no sliding renewal; re-login is required after
  `SessionCacheExpirationInMinutes`. ASP.NET `AddSession`/`UseSession` (the framework session
  middleware itself) is included when `EnableSession = true` and skipped otherwise.

## Implementation details

*Implementation detail, not contract:* `SessionPrincipalProvider`,
`SessionPrincipalAuthenticationHandler`, and `CachedClaimsPrincipalMiddleware` are internal
(`src/Ballware.Shared.Web.Session/Internal/`).

## Evidence

`test/Ballware.Shared.Web.Session.Tests/SessionPrincipalProviderTests.cs`
(`StorePrincipalAsync_StoresProtectedTicketAndSetsSessionCookie`,
`GetPrincipalAsync_ReturnsStoredPrincipalForSessionId`,
`GetPrincipalAsync_ReturnsNullForUnknownSessionId`,
`SessionPrincipal_OnlyUnprotectableWithConfiguredDataProtectionPurpose`),
`CachedClaimsPrincipalMiddlewareTests.cs`
(`InvokeAsync_SetsPrincipalFromCookieWhenUserIsUnauthenticated`,
`InvokeAsync_DoesNotOverwriteAlreadyAuthenticatedUser`, `InvokeAsync_CallsNextWithoutCookie`),
`ChallengeDifferentiationMiddlewareTests.cs`
(`InvokeAsync_Returns401ForTransportPathWithoutSession`,
`InvokeAsync_SetsPrincipalAndCallsNextForTransportPathWithSession`,
`InvokeAsync_PassesThroughNonTransportPathsWithoutAuthentication`,
`InvokeAsync_UsesConfiguredPrefixesInsteadOfDefaults`),
`ServiceCollectionExtensionsTests.cs` / `WebApplicationExtensionsTests.cs` (registration and
middleware wiring, `EnableSession = false` variants).
