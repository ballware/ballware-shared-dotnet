# Ballware.Shared.Api.Endpoints

## Purpose & dependencies

Maps the uniform 11-route tenantable editing REST API for one entity in a single call, with
minimal-API parameter bindings that pull the auth context out of the `ClaimsPrincipal` and the
two-stage tenant→entity rights cascade wrapped around every handler.

Dependencies: projects `Ballware.Shared.Api`, `Ballware.Shared.Authorization`,
`Ballware.Shared.Data`; packages Quartz, Newtonsoft.Json, MimeTypeMapOfficial, System.Linq.Async,
Microsoft.Extensions.Options, Microsoft.Extensions.Logging.Abstractions
(`src/Ballware.Shared.Api.Endpoints/Ballware.Shared.Api.Endpoints.csproj`).

Route-by-route mechanics and the import pipeline:
[concepts/editing-api-and-import-jobs.md](../concepts/editing-api-and-import-jobs.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `TenantableEditingEndpoint.MapTenantableEditingApi<TEntity>` | Maps all 11 routes under `basePath` | `src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs` → `MapTenantableEditingApi` |
| `TenantableEndpointHandlerFactory.Create*Handler` | The per-route handler delegates (`CreateAllHandler`, `CreateQueryHandler`, `CreateNewHandler`, `CreateByIdHandler`, `CreateSaveHandler`, `CreateSaveBatchHandler`, `CreateRemoveHandler`, `CreateImportHandler`, `CreateExportHandler`, `CreateExportUrlHandler`, `CreateDownloadExportHandler`) | `src/Ballware.Shared.Api.Endpoints/TenantableEndpointHandlerFactory.cs` |
| Bindings `UserId`, `UserTenantId`, `UserClaims`, `UserRights` | `BindAsync` via `IPrincipalUtils`; throw `UnauthorizedAccessException` when the principal is unauthenticated | `src/Ballware.Shared.Api.Endpoints/Bindings/UserId.cs` and siblings |
| Binding `QueryValueBag` | Captures the full query string as `Dictionary<string, StringValues>` | `src/Ballware.Shared.Api.Endpoints/Bindings/QueryValueBag.cs` → `QueryValueBag.BindAsync` |
| `EditingEndpointBuilder` | Fluent rights gate: `WithClaims`, `WithTenantAndEntityRightCheck(right, param)`, `WithBatchTenantAndEntityRightCheck(right, batch)`, `ExecuteAsync(executor)` | `src/Ballware.Shared.Api.Endpoints/EditingEndpointBuilder.cs` |
| `EditingEndpointBuilderFactory` | DI-resolved factory wiring metadata provider + both checkers into builders | `src/Ballware.Shared.Api.Endpoints/EditingEndpointBuilderFactory.cs` → `Create` |

Rights names enforced by the factory: `view` (all/query/new/byid), `add` (new), `edit` (save/savebatch
via the `primary` identifier), `delete` (remove); export/exporturl use the **query identifier itself**
as the right name. `save`/`savebatch` check right `edit` only when `identifier == "primary"`,
otherwise the identifier *is* the right (custom query = custom right). `byid`/`remove` fetch the
entry first and pass it to the entity check as `param`; batch save passes each value. A missing
tenant or entity metadata yields 404 before any rights evaluation; a denied check yields 401
(`EditingEndpointBuilder.ExecuteAsync`).

## Wiring

```csharp
services.AddBallwareSharedApiDependencies();          // registers EditingEndpointBuilderFactory (scoped)

app.MapTenantableEditingApi<MyEditable>(
    basePath: "/api/myentity",
    application: "app",
    entity: "myentity",
    apiTag: "MyEntity",
    apiOperationPrefix: "MyEntity",
    authorizationScope: "metaApi",   // default
    apiGroup: "meta");               // default
```

(`src/Ballware.Shared.Api.Endpoints/ServiceCollectionExtensions.cs` →
`AddBallwareSharedApiDependencies`; `src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs`
→ `MapTenantableEditingApi`)

- `authorizationScope` is applied via `RequireAuthorization(policyName)` — **the host must define an
  authorization policy with that exact name** (e.g. generic-service registers a `metaApi` policy in
  its Startup; llm-service's editing routes run under its browser-auth policies).
- Handler parameters (`UserTenantId`, `UserClaims`, `ITenantableRepository<TEntity>`,
  `ISchedulerFactory`, `IFileStorageProvider`, `IJobMetadataProvider`, `IExportMetadataProvider`)
  are bound per request by minimal-API binding/DI, so all dependencies must be registered.
- `identifier` is bound from the **query string** (`…/all?identifier=primary`), not from route.

## Behavior & limits

- Multi-value query parameters collapse to one pipe-joined string `|v1|v2|` before reaching the
  repository (`TenantableEndpointHandlerFactory.GetQueryParams`); `export` reads the raw request
  query, `exporturl` reads the **form body** (`Accepts<IFormCollection>("application/x-www-form-urlencoded")`).
- `remove/{id}` looks the entry up under query identifier `primary` (factory constant
  `DefaultQuery = "primary"`); a missing entry is 404, a hook veto (`RemoveResult.Result = false`)
  becomes 400 with the messages joined into one exception string.
- `import`: multipart upload (`DisableAntiforgery`), one Quartz trigger **per uploaded file**,
  job key `JobKey.Create("import", entity)`; returns 201. The endpoint itself performs no rights
  check beyond the `authorizationScope` policy — per-item authorization happens inside the job
  ([ballware-shared-api-jobs.md](ballware-shared-api-jobs.md)).
- `exporturl`: stores the export under a **1-day expiration** (`DateTime.Now.AddDays(1)`) and returns
  `ExportUrlResult { TenantId, Id }`; `download/{tenantId}/{id}` is mapped
  **`.AllowAnonymous()`** and 404s when metadata is missing, expired, or the file is gone —
  possession of the id is the only access control for the download.
- All authenticated routes declare `Produces 401` and `404` for OpenAPI.

## Implementation details

*Implementation detail, not contract:* `TenantableEndpointHandlerFactory` and
`EditingEndpointBuilderFactory`/`EditingEndpointBuilder` are public types but are plumbing — hosts
are expected to call `MapTenantableEditingApi`, not compose handlers by hand. The handler delegates'
parameter lists are DI-injected, which is why several are long (suppressed S107 warnings).

## Evidence

`test/Ballware.Shared.Api.Endpoints.Tests/Editing/TenantableEditingApiTest.cs` covers every route
with success / tenant-or-record-not-found / not-authorized triples: `HandleAll_*`, `HandleQuery_*`,
`HandleNew_*`, `HandleById_*`, `HandleSave_*`, `HandleSaveBatch_*`, `HandleRemove_*` (incl.
`HandleRemove_preliminary_check_declined`), `HandleExport_*`, `HandleExportToUrl_*`,
`HandleImport_succeeds`, `HandleDownloadExport_succeeds`, `HandleDownloadExport_notFound` (anonymous
download path asserted without credentials).
