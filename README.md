# Ballware Shared (.NET)

A collection of reusable **NET 10** class libraries that provide the shared building blocks for Ballware's multi-tenant, API-driven web applications: tenant-scoped persistence, claim- and script-based authorization, standard CRUD/REST endpoint mapping, asynchronous import jobs, Model Context Protocol (MCP) server support, and OIDC/JWT/session authentication plumbing.

Each library is a small, dependency-light NuGet package built around plain interfaces and ASP.NET Core minimal APIs, so consuming applications compose only the pieces they need.

## Repository Layout

```
ballware-shared.sln
├── src/     # 12 library projects (net10.0)
├── test/    # 10 NUnit test projects (NUnit 4, Moq, coverlet)
├── nupkgs/  # packed NuGet packages produced by the build
└── GitVersion.yml  # continuous-deployment versioning (main/develop/feature/bugfix branches)
```

Each `Ballware.Shared.*` project has a sibling `Ballware.Shared.*.Tests` project; implementation details are kept `internal` and exposed to tests via `InternalsVisibleTo`.

## Documentation

Detailed documentation of how the libraries work lives in [`docs/`](docs/README.md):

- [docs/README.md](docs/README.md) — index, reading order, refresh checklist
- [docs/concepts/](docs/concepts/) — cross-cutting mechanics (architecture, tenancy, rights cascade, editing API & jobs, MCP, web auth)
- [docs/packages/](docs/packages/) — one page per library (contract, wiring, behavior, tests)
- [docs/usage/](docs/usage/) — host composition and extension points

## Architecture Overview

The libraries form four cooperating layers plus a set of cross-cutting conventions:

```
                    ┌─────────────────────────────────────────────────────┐
  ASP.NET Core      │ Web.Oidc ──► Web.Session      Authentication        │  browser / bearer auth
  host application  ├─────────────────────────────────────────────────────┤
                    │ Api.Endpoints   Api.Jobs        Mcp.Endpoints       │  HTTP endpoints, Quartz jobs, MCP
                    │        │             │               │              │
                    │      Api         Mcp                │               │  contracts & tool registry
                    ├────────┼─────────────┼───────────────┼───────────────┤
                    │ Data.Ef        Authorization ◄── Authorization.Jint │  persistence & rights engine
                    │   │               Mcp.Authorization                 │
                    │  Data                                               │  contracts
                    └─────────────────────────────────────────────────────┘
```

Key cross-cutting conventions:

- **Multi-tenancy by convention.** Every repository, endpoint, job, storage, and authorization call carries a `Guid tenantId`; the current tenant is extracted from the user's claims via `IPrincipalUtils`.
- **Dual identity.** Persisted entities implement `IEntity` (`long? Id` database key + `Guid Uuid` public key); public DTOs implement `IEditable` (`Guid Id`), which is always mapped onto the entity's `Uuid`.
- **Auth context travels as data.** Authorization is not injected into repositories; identity/claims are passed as `string identifier` + `IDictionary<string, object> claims` arguments through the whole call chain.
- **Scripted rights.** Tenant/entity permission rules are stored as JavaScript snippets per tenant/entity and evaluated with the Jint engine (any engine can be plugged in via the checker interfaces).
- **Two-stage rights pipeline.** A tenant-level check (`ITenantRightsChecker`) produces a boolean that is fed into the entity-level check (`IEntityRightsChecker`), optionally refined per entity state (`StateAllowedScript`).

## Projects

### Ballware.Shared.Data — persistence contracts (no dependencies)

Pure abstraction layer defining the storage model:

- `IEntity` (`Id`/`Uuid`), `ITenantable` (`TenantId`), `IAuditable` (creator/last-changer id + timestamps) and `IEditable` as the public DTO contract.
- `IRepository<TEditable>` / `ITenantableRepository<TEditable>` with a uniform method set: `AllAsync`, `QueryAsync`, `CountAsync`, `ByIdAsync`, `NewAsync`, `NewQueryAsync`, `SaveAsync`, `RemoveAsync`, `ImportAsync`, `ExportAsync` — each taking the entity `identifier`, the caller's `claims`, and (for mutations) the acting `userId`.
- Extension seam via `IRepositoryHook` / `ITenantableRepositoryHook` using default interface methods: `ExtendById`, `BeforeSave`/`AfterSave`, `RemovePreliminaryCheck`, `BeforeRemove`.
- Result records `RemoveResult<T>` and `ExportResult` (`FileName`, `MediaType`, `byte[] Data`).

### Ballware.Shared.Data.Ef — EF Core repository base classes

`BaseRepository<TEditable, TPersistable>` and `TenantableBaseRepository<TEditable, TPersistable>` implement the Data contracts generically:

- Editable-DTO ⇄ persistable-entity mapping through Mapster; a thin `IDbContext` facade for testability.
- Template-method extension points (`ListQuery`, `ByIdQuery`, `ProduceNewAsync`) that subclasses override to add filtering.
- `SaveAsync` upserts by `Uuid` and stamps `IAuditable` fields automatically; `ImportAsync` gates every item through a caller-supplied authorization predicate; `ExportAsync` returns a JSON export.
- The tenantable variant applies a `TenantId` predicate to every query and forces `TenantId` on insert.

### Ballware.Shared.Authorization — rights contracts (no data dependencies)

Claim-based authorization abstractions:

- `ITenantRightsChecker` / `IEntityRightsChecker` — tenant-level and entity-level right evaluation, including `StateAllowedAsync` for state-machine rights gating.
- `IEntityAuthorizationMetadata` (`RightsCheckScript`, `StateAllowedScript`) and `ITenantAuthorizationMetadata` carry the rule scripts; `IAuthorizationMetadataProvider` resolves them per tenant/entity.
- `IPrincipalUtils` extracts user id, tenant id(s), claims, and rights from a `ClaimsPrincipal` (`AddBallwareSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim)` configures claim names).

### Ballware.Shared.Authorization.Jint — JavaScript rights engine

Implements the checkers with the embedded Jint interpreter:

- Tenant script: sees `right` (`{application}.{entity}.{right}`) and `userinfo` (claims as JSON), must return a boolean; missing script defaults to allow.
- Entity script: additionally sees `application`, `entity`, `param`, and the incoming `result` of the tenant check, which the script may override; missing script passes the tenant result through.
- State script: evaluated with `state` plus host-provided helpers `hasRight(right)` / `hasAnyRight(prefix)`; missing script defaults to deny.

### Ballware.Shared.Api — async-file API contracts (no dependencies)

Provider interfaces and DTOs decoupling the HTTP layer from storage/job bookkeeping: `IExportMetadataProvider`, `IJobMetadataProvider`, `IFileStorageProvider` (all tenant-scoped), plus `Export`, `ExportUrlResult`, and the `JobStates` lifecycle enum (`Queued`, `InProgress`, `Finished`, `Error`).

### Ballware.Shared.Api.Endpoints — standard editing REST endpoints

Maps a uniform 11-route CRUD/query/import/export API for a tenantable entity in one call:

```csharp
app.MapTenantableEditingApi<MyEditable>("/api/myentity", application: "app", entity: "myentity", ...);
// GET all | query | new | byid · POST save | savebatch | import · DELETE remove/{id}
// GET export | download/{tenantId}/{id} · POST exporturl
```

- Minimal-API parameter bindings (`UserId`, `UserTenantId`, `UserClaims`, `UserRights`, `QueryValueBag`) pull the auth context from the `ClaimsPrincipal` via `IPrincipalUtils`.
- A fluent `EditingEndpointBuilder` (`WithTenantAndEntityRightCheck`, `WithBatchTenantAndEntityRightCheck`) enforces the two-stage tenant→entity rights check (rights: `view`, `add`, `edit`, `delete`) before every handler.
- Import uploads the file through `IFileStorageProvider`, persists job bookkeeping via `IJobMetadataProvider`, and triggers a Quartz job; export/exporturl/download produce JSON exports with expiring, anonymous download URLs.

### Ballware.Shared.Api.Jobs — Quartz import workers

`ImportJob<TEntity, TRepository>` and `TenantableImportJob<TEntity, TRepository>` execute asynchronous bulk imports scheduled by the endpoints layer: they read `tenantId`, `userId`, `identifier`, serialized claims, and the temporary file id from the Quartz `JobDataMap`, authorize each item via the tenant rights checker, run `ImportAsync`, clean up the uploaded file, and report progress through `JobStates`.

### Ballware.Shared.Mcp — MCP tool model & registry (transport-agnostic)

- `Tool` (`Name`, `Description`, `ToolParam` list, JSON `OutputSchema`, optional `IsAuthorizedAsync` gate, `ExecuteAsync(serviceProvider, user, args)`), `ToolResult` (text and/or structured content).
- `IToolRegistry` supports **static tools** and **dynamic tool providers** (per-request, per-user tool sets resolved from DI at query time).
- `AddBallwareMcpTools(configure)` registers the registry plus a hosted initializer that runs the configuration delegate once at startup with full DI access.

### Ballware.Shared.Mcp.Endpoints — MCP HTTP transport

Bridges the tool registry to the official `ModelContextProtocol.AspNetCore` SDK:

- `AddBallwareMcpEndpoint(options)` wires `ListTools`/`CallTool` handlers to the registry with per-request DI scoping; `MapBallwareUserMcpEndpoint(basePath, options)` maps the server and pins it to JwtBearer authentication with a required `scope` claim.
- `tools/list` returns only tools the current user is authorized for; `tools/call` validates arguments against the declared parameter types (schema generated with NJsonSchema) and re-checks authorization before execution.
- `UseBallwareMcpOAuthProtectedResource` serves OAuth **protected resource metadata** at `/.well-known/oauth-protected-resource` and decorates 401 responses with `WWW-Authenticate: Bearer resource_metadata="..."` so MCP clients can bootstrap the authorization flow.

### Ballware.Shared.Mcp.Authorization — MCP ⇄ rights-engine adapter

`McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(application, entity, right)` produces a `Tool.IsAuthorizedAsync` gate that runs the standard tenant→entity rights cascade against the calling principal's tenant and claims.

### Ballware.Shared.Authentication — OIDC userinfo enrichment

Enriches JWT-authenticated principals with claims from the identity provider's `/userinfo` endpoint (`AddBallwareSharedUserInfoEnrichment` + `AttachUserInfoEnrichment()` on JwtBearer events): scope-gated, single-flight cached (positive/negative TTL), size- and claim-count-limited, sub-matched, and protected against overwriting core JWT claims; a failed enrichment fails authentication hard.

### Ballware.Shared.Web.Session — distributed cookie sessions

After OIDC login, the `ClaimsPrincipal` is stored as a Data-Protection-protected authentication ticket in `IDistributedCache` behind a `.BallwareSession` cookie (`HttpOnly; Secure; SameSite=None`); `CachedClaimsPrincipalMiddleware` rehydrates the principal on later requests:

- A `DistributedSession` authentication scheme lets policies bind to the rehydrated user.
- `ChallengeDifferentiationMiddleware` answers 401 (instead of a 302 OIDC challenge) for transport paths (`/_blazor`, DevExpress `/DXX`, `/dxDR`) that cannot follow redirects.
- OIDC hook `RegisterBallwareWebSessionTokenHandling()` stores the principal at token validation and suppresses the default cookie sign-in, keeping raw tokens out of the session.

### Ballware.Shared.Web.Oidc — browser authentication composition

One-call wiring (`AddBallwareBrowserAuthorization`) of Cookie + OpenID Connect (public client, code flow) + JwtBearer schemes, scope-based authorization policies, distributed sessions, and reverse-proxy hardening:

- `PublicBaseUrl` fixes the OIDC `redirect_uri` behind proxies (avoids Pushed-Authorization-Request rejections at the IdP); `SignInChallengeFallbackMiddleware` converts broken OIDC challenges into diagnostic 401s.
- `MapBallwareSignOnEndpoint()` exposes `GET /signon/{idToken}?redirect=...`: an out-of-band id_token from a SPA frontend is validated against the IdP's discovery keys and converted into a server-side session, with strict open-redirect allow-lists.

## Build & Test

```bash
dotnet build ballware-shared.sln
dotnet test ballware-shared.sln
```

Tests use NUnit 4 with Moq and coverlet. Package versions follow GitVersion continuous deployment: `main` increments patch, `develop` and `feature/*` increment minor, `bugfix/*` increments patch; commit-message markers (`+semver: major|minor|patch`) override the branch rules. Built packages are emitted to `nupkgs/`.

## License

See [LICENSE](LICENSE).
