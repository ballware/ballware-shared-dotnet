# Ballware.Shared.Web.Oidc

## Purpose & dependencies

One-call composition of browser authentication: Cookie + OpenID Connect (public client, code flow) +
JwtBearer schemes, scope-based authorization policies, distributed sessions (via
`Ballware.Shared.Web.Session`), reverse-proxy-safe `redirect_uri`, diagnostic fallback for broken
OIDC challenges, and the out-of-band `/signon` endpoint for SPA-held id tokens. Overview in
[concepts/web-authentication-and-sessions.md](../concepts/web-authentication-and-sessions.md).

Dependencies: project `Ballware.Shared.Web.Session`; packages
Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.AspNetCore.Authentication.OpenIdConnect
(`src/Ballware.Shared.Web.Oidc/Ballware.Shared.Web.Oidc.csproj`).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `BrowserAuthorizationOptions` | `[Required] Authority`, `Audience`, `ClientId`, `RequiredUserScope`; `Issuer?`; `RequireHttpsMetadata = true`; `UserScopePolicyName?`; `ServiceScopePolicyName?`; `SignOnAllowedHosts = []`; `SignOnAllowedRedirectPathPrefixes = []`; `PublicBaseUrl?` | `src/Ballware.Shared.Web.Oidc/Configuration/BrowserAuthorizationOptions.cs` |
| `ConfigurationException` | Thrown for missing/invalid configuration (missing `Authorization` section, invalid `PublicBaseUrl`) | `src/Ballware.Shared.Web.Oidc/Configuration/ConfigurationException.cs` |
| `BrowserAuthorizationExtensions.AddBallwareBrowserAuthorization` (2 overloads) | Binds options (config section `"Authorization"` or direct instance), registers all schemes/policies/session, returns the options | `src/Ballware.Shared.Web.Oidc/BrowserAuthorizationExtensions.cs` |
| `OpenIdConnectPublicRedirectExtensions.RegisterBallwarePublicRedirectUriHandling(options, authorizationOptions)` | Deterministic `redirect_uri` from `PublicBaseUrl` + callback path; exposes the effective URI under `HttpContext.Items` key `Ballware.Oidc.EffectiveRedirectUri` | `src/Ballware.Shared.Web.Oidc/OpenIdConnectPublicRedirectExtensions.cs` |
| `OpenIdConnectPublicRedirectExtensions.NormalizePublicBaseUrl(value)` | `null` when unset; otherwise absolute http(s) URL without query/fragment and without trailing slash; throws `ConfigurationException` on invalid values | `src/Ballware.Shared.Web.Oidc/OpenIdConnectPublicRedirectExtensions.cs` |
| `WebApplicationExtensions.UseBallwareSignInChallengeFallback()` | Middleware converting failed OIDC challenges into 401 with diagnostic log | `src/Ballware.Shared.Web.Oidc/WebApplicationExtensions.cs` |
| `SignOnEndpointExtensions.MapBallwareSignOnEndpoint()` | Maps `GET /signon/{idToken}?redirect=…` (excluded from API descriptions) | `src/Ballware.Shared.Web.Oidc/SignOnEndpointExtensions.cs` |

## Wiring

```csharp
var sessionOptions = new WebSessionOptions();
var authOptions = services.AddBallwareBrowserAuthorization(Configuration, sessionOptions);

// pipeline (order as used by ballware-llm-service/src/Ballware.Llm.App/Startup.cs):
app.UseForwardedHeaders(...);
app.UseRouting();
app.UseBallwareWebSession();                 // from Ballware.Shared.Web.Session
app.UseBallwareChallengeDifferentiation();   // from Ballware.Shared.Web.Session
app.UseAuthentication();
app.UseBallwareSignInChallengeFallback();
app.UseAuthorization();
app.UseAntiforgery();
app.MapBallwareSignOnEndpoint();
```

## Behavior & limits

- **Schemes:** default scheme Cookie, default challenge OIDC. OIDC is configured as a **public
  client**: `ClientSecret = null`, `ResponseType = "code"`, scopes `openid profile` +
  `RequiredUserScope`, `ValidIssuer = Issuer ?? Authority`. It automatically applies
  `RegisterBallwareWebSessionTokenHandling()` (session storage instead of cookie tokens) and
  `RegisterBallwarePublicRedirectUriHandling(...)`. JwtBearer is added with
  `MapInboundClaims = false` and `ValidIssuer = Issuer ?? Authority` for API calls to the same app.
- **Policies:** user policy name = `UserScopePolicyName ?? RequiredUserScope`, asserting the space
  split `scope` claim contains `RequiredUserScope` (ordinal); service policy registered **only when
  `ServiceScopePolicyName` is set**, with the policy name itself as required scope value. Both
  pin the JwtBearer scheme.
- **`PublicBaseUrl` rule:** the OIDC `redirect_uri` is deterministically
  `{PublicBaseUrl}/signin-oidc` (callback path honored if customized); unset ⇒ fall back to the
  request origin (scheme://host + pathbase). Rationale: IdPs with a Pushed-Authorization endpoint
  validate `redirect_uri` server-side during the challenge; a proxy-derived internal origin gets
  rejected there (400 → protocol exception). Invalid `PublicBaseUrl` fails at **composition time**
  with `ConfigurationException` instead of surfacing as an IdP rejection.
- **Challenge fallback:** `UseBallwareSignInChallengeFallback` catches `OpenIdConnectProtocolException`
  before the response has started, logs the effective `redirect_uri` + client + `PublicBaseUrl`, and
  answers 401 so clients can re-init via `/signon`; unrelated exceptions and already-started
  responses are rethrown.
- **`/signon/{idToken}` flow:** the id_token is validated against the IdP's discovery signing keys
  (`ConfigurationManager<OpenIdConnectConfiguration>` built from the OIDC scheme's authority),
  `ValidIssuer = Issuer ?? Authority`, `ValidAudience = ClientId`, lifetime enforced. On success a
  `ClaimsIdentity` ("Bearer") is stored through `ISessionPrincipalProvider` (session cookie set) and
  the client is redirected to `redirect`; unreadable/invalid tokens ⇒ 401.
- **Open-redirect allow-list** (`SignOnEndpointExtensions.IsSafeRedirect`): relative paths must be
  exactly `/` (always allowed) or match a `SignOnAllowedRedirectPathPrefixes` segment prefix;
  absolute http(s) URLs must target the request host (same-host non-default ports must match) or a
  `SignOnAllowedHosts` host (port ignored for configured hosts); percent-encoded detours (`%2F`,
  `%2E`), `..` traversal, and protocol-relative (`//host`) paths are rejected; non-http schemes are
  rejected; violations yield 400 before any token validation.

## Implementation details

*Implementation detail, not contract:* `SignInChallengeFallbackMiddleware` is internal
(`src/Ballware.Shared.Web.Oidc/SignInChallengeFallbackMiddleware.cs`); the options-instance overload
additionally registers an `IConfigureOptions<BrowserAuthorizationOptions>` so `IOptions<...>`
resolution mirrors the passed instance.

## Evidence

`test/Ballware.Shared.Web.Oidc.Tests/BrowserAuthorizationExtensionsTests.cs`
(`AddBallwareBrowserAuthorization_RegistersCookieOidcJwtAndSessionSchemes`,
`AddBallwareBrowserAuthorization_BindsConfigurationSectionToOptions`,
`AddBallwareBrowserAuthorization_ThrowsWhenAuthorizationSectionIsMissing`,
`AddBallwareBrowserAuthorization_ThrowsOnInvalidPublicBaseUrl`,
`UserPolicy_RejectsAuthenticatedUserWithoutRequiredScope`,
`ServicePolicy_IsSkippedWhenServiceScopePolicyNameIsNotConfigured`),
`OpenIdConnectPublicRedirectExtensionsTests.cs`
(`RegisterBallwarePublicRedirectUriHandling_UsesPublicBaseUrlDerivedRedirectUri`,
`..._FallsBackToRequestOriginWithoutPublicBaseUrl`, `..._ChainsPreviouslyConfiguredHandler`,
`NormalizePublicBaseUrl_*`),
`SignInChallengeFallbackMiddlewareTests.cs`
(`InvokeAsync_Returns401AndKeepsChallengeExceptionFromEscaping`,
`InvokeAsync_RethrowsWhenResponseHasAlreadyStarted`),
`SignOnRedirectSafetyTests.cs` (all `IsSafeRedirect_*` allow/reject cases incl.
`IsSafeRedirect_RejectsPercentEncodedDetours`, `IsSafeRedirect_AllowsRootRedirectWithoutConfiguredPrefixes`).
