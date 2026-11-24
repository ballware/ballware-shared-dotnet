# Host composition

The ordered `Add*` / `Use*` / `Map*` sequence per application type. Snippets use only the shared
packages' APIs; real-world wiring lives in the consumer repositories (named, not linked:
`ballware-meta-service/src/Ballware.Meta.Service/Startup.cs`,
`ballware-generic-service/src/Ballware.Generic.Service/Startup.cs`,
`ballware-llm-service/src/Ballware.Llm.App/Startup.cs`,
`ballware-llm-service/src/Ballware.Llm.Service/Startup.cs`). Each extension is documented on its
package page; this page only fixes **order and pairing**.

## A. API service with editing API + import jobs

Services phase:

```csharp
services.AddQuartz(/* host scheduler setup */);

services.AddBallwareSharedAuthorizationUtils(
    tenantClaim: "tenant", userIdClaim: "sub", rightClaim: "right");   // host's claim names
services.AddBallwareSharedJintRightsChecker();

services.AddBallwareSharedApiDependencies();    // EditingEndpointBuilderFactory (scoped)

// host-supplied seams — the shared packages never register these:
services.AddScoped<IAuthorizationMetadataProvider, HostMetadataProvider>();
services.AddScoped<IFileStorageProvider, HostFileStorage>();
services.AddScoped<IJobMetadataProvider, HostJobStore>();
services.AddScoped<IExportMetadataProvider, HostExportStore>();
services.AddScoped<ITenantableRepository<MyEditable>, MyEditableRepository>();

// Quartz job type for the import trigger (JobKey group = the entity name passed to MapTenantableEditingApi):
services.AddQuartz(q => q.AddJob<TenantableImportJob<MyEditable, ITenantableRepository<MyEditable>>>(
    JobKey.Create("import", "myentity")));

services.AddAuthorizationBuilder()
    .AddPolicy("metaApi", /* host assertion, e.g. scope check */);
```

Pipeline phase:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapTenantableEditingApi<MyEditable>(
    "/api/myentity", application: "app", entity: "myentity",
    apiTag: "MyEntity", apiOperationPrefix: "MyEntity");   // authorizationScope defaults to "metaApi"
```

Reference wiring: `ballware-generic-service/src/Ballware.Generic.Service/Startup.cs`
(`AddBallwareSharedAuthorizationUtils` → `AddBallwareSharedJintRightsChecker`); `MapTenantableEditingApi`
usage in `ballware-meta-service/src/Ballware.Meta.Service/Startup.cs`. The policy named by
`authorizationScope` **must** exist or every mapped route fails at authorization.

## B. MCP server (bearer API)

Services phase:

```csharp
services.AddBallwareSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim);
services.AddBallwareSharedJintRightsChecker();
// + the host's IAuthorizationMetadataProvider, repositories etc. (see A)

var mcpOptions = new McpEndpointOptions
{
    Enabled = true,
    RequiredMcpScope = "mcp",
    ResourceUri = "https://example.org/generic/mcp"
};

services.AddBallwareMcpTools((serviceProvider, registry) =>
{
    registry.RegisterStaticTool(new Tool
    {
        Name = "myentity.query",
        Description = "Query myentity",
        Params = [ new ToolParam { Type = ToolParamType.String, Name = "search", Description = "..." } ],
        IsAuthorizedAsync = McpAuthorizationHandlerFactory
            .CreateStaticEntityRightAuthorizationHandler("app", "myentity", "view"),
        ExecuteAsync = async (sp, user, args) => { /* resolve scoped services from sp, call repository */ }
    });
});

services.AddBallwareMcpEndpoint(mcpOptions);

services.AddAuthentication().AddJwtBearer(/* host JWT setup */);
```

Pipeline phase — protected-resource middleware **before** authentication:

```csharp
app.UseBallwareMcpOAuthProtectedResource(mcpOptions, authorizationServerUri);  // IdP base URL
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapBallwareUserMcpEndpoint("/generic/mcp", mcpOptions);
```

Reference wiring: `ballware-generic-service/src/Ballware.Generic.Service/Startup.cs`
(`AddBallwareMcpTools` → `AddBallwareMcpEndpoint`, `MapBallwareUserMcpEndpoint("/generic/mcp", …)`);
`ballware-meta-service/src/Ballware.Meta.Service/Startup.cs`
(`UseBallwareMcpOAuthProtectedResource(mcpOptions, Issuer ?? Authority)` immediately before
`MapBallwareUserMcpEndpoint("/meta/mcp", …)`).

Bearer userinfo enrichment pairs with this type (API services enrich JWT principals):

```csharp
services.AddBallwareSharedUserInfoEnrichment(Configuration);   // section "Authorization:UserInfo"
// in AddJwtBearer: options.Events = new JwtBearerEvents { ... }.AttachUserInfoEnrichment();
```

Reference: `ballware-meta-service/src/Ballware.Meta.Service/Startup.cs`
(`AddBallwareSharedUserInfoEnrichment`, `.AttachUserInfoEnrichment()`).

## C. Blazor / browser app (cookie + OIDC)

Services phase:

```csharp
var sessionOptions = new WebSessionOptions();               // defaults are production-shaped
var authOptions = services.AddBallwareBrowserAuthorization(Configuration, sessionOptions);
// binds config section "Authorization" → BrowserAuthorizationOptions
```

Pipeline phase (order as practiced in `ballware-llm-service/src/Ballware.Llm.App/Startup.cs`):

```csharp
app.UseForwardedHeaders(/* reverse-proxy setup */);
app.UseRouting();
app.UseBallwareWebSession();                 // session + principal rehydration
app.UseBallwareChallengeDifferentiation();   // 401 for /_blazor, /DXX, /dxDR transport paths
app.UseAuthentication();                     // Cookie + OIDC + JwtBearer from AddBallwareBrowserAuthorization
app.UseBallwareSignInChallengeFallback();    // broken OIDC challenge → logged 401
app.UseAuthorization();
app.UseAntiforgery();
app.UseStaticFiles();

app.MapBallwareSignOnEndpoint();             // GET /signon/{idToken}?redirect=…
```

The user-facing endpoints then authorize via the scope policies from `BrowserAuthorizationOptions`
(`UserScopePolicyName ?? RequiredUserScope`, optional `ServiceScopePolicyName`).

## Order rules that matter

1. `UseBallwareMcpOAuthProtectedResource` **before** `UseAuthentication`/`UseAuthorization` —
   otherwise the well-known endpoint is challenged and 401 decoration is skipped
   (`src/Ballware.Shared.Mcp.Endpoints/McpEndpoint.cs`).
2. `UseBallwareWebSession` (rehydration) **before** `UseAuthentication` so the `DistributedSession`
   scheme and policies see the rehydrated principal
   (`src/Ballware.Shared.Web.Session/WebApplicationExtensions.cs`).
3. `UseBallwareChallengeDifferentiation` belongs between session rehydration and authentication so
   transport paths can answer 401 without triggering an OIDC challenge
   (`src/Ballware.Shared.Web.Session/ChallengeDifferentiationMiddleware.cs`).
4. `AddBallwareBrowserAuthorization` internally calls `AddBallwareWebSession` and applies
   `RegisterBallwareWebSessionTokenHandling()` + `RegisterBallwarePublicRedirectUriHandling(...)` to
   its OIDC scheme — do not register the session or these OIDC hooks twice when using the
   composition overload; call the two `Register*` extensions manually only when wiring OpenIdConnect
   yourself (`src/Ballware.Shared.Web.Oidc/BrowserAuthorizationExtensions.cs`).
5. Host-side seams (`IAuthorizationMetadataProvider`, `IFileStorageProvider`, `IJobMetadataProvider`,
   `IExportMetadataProvider`, repositories) must be registered before any route that resolves them —
   the shared packages deliberately register none of them.
