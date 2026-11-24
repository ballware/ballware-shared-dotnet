# Web authentication & sessions

Three packages cover browser and API authentication; this page draws the boundaries and the shared
flows. Packages: [ballware-shared-authentication.md](../packages/ballware-shared-authentication.md),
[ballware-shared-web-session.md](../packages/ballware-shared-web-session.md),
[ballware-shared-web-oidc.md](../packages/ballware-shared-web-oidc.md).

## Boundaries

| Package | Path | Identity carrier |
| --- | --- | --- |
| `Ballware.Shared.Authentication` | **Bearer path**: JWT validates, then optional `/userinfo` claim enrichment inside `JwtBearerEvents.OnTokenValidated` | enriched `ClaimsPrincipal` per request, nothing persisted |
| `Ballware.Shared.Web.Session` | **Cookie path**: principal stored as protected ticket in `IDistributedCache`, rehydrated from `.BallwareSession` cookie | Data-Protection ticket + session cookie |
| `Ballware.Shared.Web.Oidc` | **Composition**: Cookie + OIDC (public client code flow) + JwtBearer schemes, scope policies, `PublicBaseUrl` redirect handling, `/signon` endpoint | orchestrates the session package |

`Web.Oidc` is the only one with a project dependency in this trio (`Web.Oidc → Web.Session`;
`src/Ballware.Shared.Web.Oidc/Ballware.Shared.Web.Oidc.csproj`); `Authentication` is standalone and
usable in pure API services without any web session.

## Session ticket anatomy

`SessionPrincipalProvider.StorePrincipalAsync` (`src/Ballware.Shared.Web.Session/Internal/SessionPrincipalProvider.cs`):
fresh `Guid` session id → `AuthenticationTicket(principal, "DistributedSession")` →
`TicketSerializer.Default.Serialize` → Data Protection protect with purpose
`WebSessionOptions.DataProtectionPurpose` → Base64 → `IDistributedCache` under
`{SessionCacheKeyPrefix}{sessionId}` with absolute expiry (`SessionCacheExpirationInMinutes`, default
60 min, no sliding renewal) → cookie `.BallwareSession` (`HttpOnly; Secure; SameSite=None; Path=/`,
expiry = TTL).

Consequences: **raw OIDC tokens never enter the session** — the OIDC hook
`RegisterBallwareWebSessionTokenHandling` sets `SaveTokens = false` and
`GetClaimsFromUserInfoEndpoint = false` and stores only the validated principal
(`src/Ballware.Shared.Web.Session/OpenIdConnectSessionOptionsExtensions.cs`). The `DistributedSession`
authentication scheme exposes the rehydrated user to policies;
`CachedClaimsPrincipalMiddleware` performs rehydration only for unauthenticated contexts
(`src/Ballware.Shared.Web.Session/Internal/CachedClaimsPrincipalMiddleware.cs`).

## 401 vs 302: transport paths

SignalR/Blazor circuits and DevExpress transport endpoints (`/_blazor`, `/DXX`, `/dxDR` —
`ChallengeDifferentiationMiddleware.DefaultTransportPathPrefixes`,
`src/Ballware.Shared.Web.Session/ChallengeDifferentiationMiddleware.cs`) cannot follow a 302 OIDC
challenge. The middleware answers **401** on those paths when `DistributedSession` authentication
fails, and lets all other paths keep the normal OIDC challenge (→ `/signin-oidc` → cookie). The
prefix list is overridable via `UseBallwareChallengeDifferentiation(transportPathPrefixes)`.

## `/signon/{idToken}` flow

For SPA frontends holding an id_token out-of-band (`MapBallwareSignOnEndpoint`,
`src/Ballware.Shared.Web.Oidc/SignOnEndpointExtensions.cs`):

1. `redirect` validated **first** against the open-redirect allow-list (relative prefix list +
   always-allowed exact `/`; absolute URLs only to the request host or `SignOnAllowedHosts`;
   percent-encoding decoded before checks; traversal/protocol-relative rejected).
2. id_token validated against IdP discovery signing keys, `ValidIssuer = Issuer ?? Authority`,
   `ValidAudience = ClientId`, lifetime enforced.
3. Valid ⇒ principal stored via `ISessionPrincipalProvider` (session ticket + cookie) → redirect;
   invalid/unreadable ⇒ 401.

This is how a Blazor/SPA app bootstraps a server session after an auth failure that answered 401
(see transport paths above).

## `PublicBaseUrl` rule for `redirect_uri`

Behind reverse proxies the framework-derived `redirect_uri` (scheme/host of the *internal* request)
usually is not a registered redirect URI at the IdP — IdPs with a Pushed-Authorization endpoint
validate it during the challenge and fail it with 400. `RegisterBallwarePublicRedirectUriHandling`
therefore fixes `redirect_uri` to `{PublicBaseUrl}/signin-oidc` (`RegisterBallwarePublicRedirectUriHandling`,
`src/Ballware.Shared.Web.Oidc/OpenIdConnectPublicRedirectExtensions.cs`) — the exact value must be
registered for the client at the IdP. Unset → framework fallback to request origin. Invalid values
(`PublicBaseUrl`) throw `ConfigurationException` at composition time, and a failing challenge itself
becomes a logged 401 via `UseBallwareSignInChallengeFallback` instead of a 500
(`src/Ballware.Shared.Web.Oidc/SignInChallengeFallbackMiddleware.cs`).

## Bearer enrichment interplay

`AttachUserInfoEnrichment` + `AddBallwareSharedUserInfoEnrichment` add IdP userinfo claims to bearer
principals (scope-gated, cached, hard-fail on errors — see the package page). It composes with both
web paths (a browser app may still call APIs with bearer tokens) but has **no effect** on the cookie
path: session tickets contain whatever the principal was at login time.
