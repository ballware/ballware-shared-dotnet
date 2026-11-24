# Extension points

Every seam the shared packages leave open for hosts. Each row names the contract to implement and
the source that invokes it.

## Persistence

| Extension point | Implement | Hooked in by |
| --- | --- | --- |
| Repository hooks | `IRepositoryHook<TEditable, TPersistable>` / `ITenantableRepositoryHook<TEditable, TPersistable>` — override only the seams you need (`ExtendById`, `BeforeSave`, `AfterSave`, `RemovePreliminaryCheck`, `BeforeRemove`) | `BaseRepository`/`TenantableBaseRepository` dispatch wrappers (`src/Ballware.Shared.Data.Ef/Repository/BaseRepository.cs`) |
| Query filtering / new-instance shaping | derive from `BaseRepository`/`TenantableBaseRepository` and override `ListQuery`, `ByIdQuery`, `ProduceNewAsync` | template calls inside the same files |
| DbContext seam | implement `IDbContext` around the host's `DbContext` | `src/Ballware.Shared.Data.Ef/IDbContext.cs` (constructor-injected everywhere) |
| Entity model | implement `IEntity` (+ `ITenantable`, `IAuditable`) and `IEditable` DTOs with Mapster-compatible names | mapping in `BaseRepository.SaveAsync`/`ByIdAsync` |

A `RemovePreliminaryCheck` returning `Result = false` is the supported way to enforce domain rules
on deletes — the endpoints layer turns it into 400 with the collected messages
(`TenantableEndpointHandlerFactory.CreateRemoveHandler`).

## Authorization engine

| Extension point | Implement | Hooked in by |
| --- | --- | --- |
| Rights engine | `ITenantRightsChecker` + `IEntityRightsChecker` (replace the Jint pair by registering your own implementations — do not call `AddBallwareSharedJintRightsChecker`) | editing endpoints, import jobs, MCP gate factory (`src/Ballware.Shared.Api.Endpoints/EditingEndpointBuilderFactory.cs`, `src/Ballware.Shared.Api.Jobs/TenantableImportJob.cs`, `src/Ballware.Shared.Mcp.Authorization/McpAuthorizationHandlerFactory.cs`) |
| Rule storage | `IAuthorizationMetadataProvider` backed by the host's tenant/entity metadata store | the same call sites |
| Principal extraction | register your own `IPrincipalUtils` instead of `AddBallwareSharedAuthorizationUtils` (the built-in `DefaultPrincipalUtils` is internal) | minimal-API bindings `UserId`/`UserTenantId`/`UserClaims`/`UserRights` (`src/Ballware.Shared.Api.Endpoints/Bindings/`) |

Engine replacement must preserve the two-stage contract: stage 2 receives stage 1's boolean as
`tenantResult` and owns the override decision
(see [concepts/authorization-and-rights-scripts.md](../concepts/authorization-and-rights-scripts.md)).

## Storage & jobs

| Extension point | Implement | Hooked in by |
| --- | --- | --- |
| Temporary files | `IFileStorageProvider` (upload/fetch/remove, tenant+user scoped) | import endpoint + exporturl + download + import jobs (`src/Ballware.Shared.Api/IFileStorageProvider.cs`) |
| Job bookkeeping | `IJobMetadataProvider` (`CreateJobAsync` / `UpdateJobAsync` with `JobStates`) | import endpoint + jobs (`src/Ballware.Shared.Api/IJobMetadataProvider.cs`) |
| Export bookkeeping | `IExportMetadataProvider` (`CreateExportAsync` / `GetExportByIdAsync`) | exporturl + anonymous download (`src/Ballware.Shared.Api/IExportMetadataProvider.cs`) |
| Import authorization predicate | the `Func<TEditable, Task<bool>> authorized` argument of `ImportAsync` (the jobs supply the tenant rights check; direct repository callers may supply their own) | `ITenantableRepository.ImportAsync` (`src/Ballware.Shared.Data/Repository/ITenantableRepository.cs`) |

Expiration cleanup (old `Export`/`Job` records, expired export files) is host-side housekeeping — no
shared implementation exists.

## MCP tools

| Extension point | How | Hooked in by |
| --- | --- | --- |
| Dynamic tool providers | `IToolRegistry.RegisterDynamicToolProvider((serviceProvider, user) => …)` — per-request tool sets resolved from the request scope and user | `ToolRegistry.BuildToolListAsync` (`src/Ballware.Shared.Mcp/Internal/ToolRegistry.cs`) |
| Custom authorization gates | any `Func<IServiceProvider, ClaimsPrincipal, Task<bool>>` on `Tool.IsAuthorizedAsync` (the rights-cascade factory is just the standard one) | `ToolRegistryRequestHandlers.ListToolsAsync` / `CallToolAsync` (`src/Ballware.Shared.Mcp.Endpoints/Internal/ToolRegistryRequestHandlers.cs`) |
| Tool execution | `Tool.ExecuteAsync` resolves scoped services itself — repositories, providers, checkers are all reachable via the passed `IServiceProvider` | `CallToolAsync` |

## Web plumbing

| Extension point | How | Hooked in by |
| --- | --- | --- |
| Transport path list | `app.UseBallwareChallengeDifferentiation(new[] { "/mytransport", ... })` overrides the `/_blazor`, `/DXX`, `/dxDR` defaults | `ChallengeDifferentiationMiddleware` (`src/Ballware.Shared.Web.Session/ChallengeDifferentiationMiddleware.cs`) |
| Session configuration | pass a configured `WebSessionOptions` (cookie name, cache prefix, TTL, Data Protection purpose) to `AddBallwareWebSession` | `SessionPrincipalProvider` (`src/Ballware.Shared.Web.Session/Internal/SessionPrincipalProvider.cs`) |
| Enrichment policy | configure `UserInfoEnrichmentOptions` (scopes, TTLs, limits) or replace `IUserInfoEnricher` registration entirely | `AttachUserInfoEnrichment` resolves `IUserInfoEnricher` from request services (`src/Ballware.Shared.Authentication/JwtBearerEventsExtensions.cs`) |
| OIDC event chaining | host handlers registered *before* `AttachUserInfoEnrichment` / `RegisterBallwarePublicRedirectUriHandling` are preserved — both wrap the previously configured delegate | `JwtBearerEventsExtensions.AttachUserInfoEnrichment`, `OpenIdConnectPublicRedirectExtensions.RegisterBallwarePublicRedirectUriHandling` |
