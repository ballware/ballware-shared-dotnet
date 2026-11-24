# Ballware.Shared.Api.Jobs

## Purpose & dependencies

Quartz `IJob` implementations that execute the asynchronous bulk imports triggered by the editing
API: they reconstruct the request context from the Quartz `JobDataMap`, authorize each item through
the tenant rights checker, run `ImportAsync`, clean up the uploaded file, and report progress
through `JobStates`.

Dependencies: projects `Ballware.Shared.Api`, `Ballware.Shared.Authorization`,
`Ballware.Shared.Data`; packages Quartz, Quartz.AspNetCore, Newtonsoft.Json,
Microsoft.Extensions.DependencyInjection.Abstractions,
Microsoft.Extensions.Configuration.Abstractions
(`src/Ballware.Shared.Api.Jobs/Ballware.Shared.Api.Jobs.csproj`).

End-to-end flow: [concepts/editing-api-and-import-jobs.md](../concepts/editing-api-and-import-jobs.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `ImportJob<TEntity, TRepository>` | `IJob` for non-tenantable repositories (`TRepository : IRepository<TEntity>`) | `src/Ballware.Shared.Api.Jobs/ImportJob.cs` → `ImportJob` |
| `TenantableImportJob<TEntity, TRepository>` | `IJob` for `TRepository : ITenantableRepository<TEntity>`; adds `tenantId` to all repository calls | `src/Ballware.Shared.Api.Jobs/TenantableImportJob.cs` → `TenantableImportJob` |
| Job constructor contract | `(IServiceProvider, IJobMetadataProvider, IAuthorizationMetadataProvider, ITenantRightsChecker, IFileStorageProvider)` — the repository is resolved later from `IServiceProvider` | `ImportJob` / `TenantableImportJob` constructors |
| `JobDataMap` keys consumed | `tenantId`, `jobId`, `userId` (Guid), `identifier` (string), `claims` (JSON string), `file` (Guid temporary file id) | `TenantableImportJob.Execute` reads these; written by `TenantableEndpointHandlerFactory.CreateImportHandler` |
| `Utils.NormalizeJsonMember` / `Utils.DropNullMember` | Claim JSON normalization: `JToken` values converted to plain objects, null members dropped | `src/Ballware.Shared.Api.Jobs/Utils.cs` → `Utils` |

## Wiring

The jobs are not registered by the package itself. The host maps the closed generic job type to
Quartz (e.g. `TenantableImportJob<MyEditable, ITenantableRepository<MyEditable>>` via Quartz's job
type registry / `AddQuartz` job map), and the endpoints layer triggers it with
`JobKey.Create("import", entity)` (see the endpoints page). Quartz resolves the constructor from the
scoped `IServiceProvider`.

## Behavior & limits

Execution order in `Execute` (`TenantableImportJob.Execute`, mirrored by `ImportJob.Execute`):

1. Read `JobDataMap` values; missing `identifier`, `file` (= `Guid.Empty`) or unknown tenant
   (metadata provider returns `null`) abort with `ArgumentException`.
2. `UpdateJobAsync(..., JobStates.InProgress, "")`.
3. Open the temporary file via `IFileStorageProvider.TemporaryFileByIdAsync` and call
   `repository.ImportAsync` with the per-item authorization predicate
   `TenantRightsChecker.HasRightAsync(tenant, "meta", jobKey.Group, claims, identifier)` —
   application is the **literal `"meta"`**, entity is the **Quartz job group** (the endpoints layer
   creates the key with group = `entity`), right is the query `identifier`. Only tenant stage
   runs — no entity-stage check inside the job.
4. On success: remove the temporary file, `UpdateJobAsync(..., JobStates.Finished, "")`.
5. On failure: `UpdateJobAsync(..., JobStates.Error, serialized exception JSON)` (only when tenant
   metadata was found) and rethrow as `JobExecutionException` with `refireImmediately: false`.

- The `Queued` state is never written by the job; the endpoints layer created the job record before
  triggering, so hosts typically persist it as `Queued` in `CreateJobAsync`.
- Authorization is re-evaluated per item against the claims serialized at request time — a rights
  change between request and job execution is picked up, a revoked user is not re-authenticated.
- `ImportJob` (non-tenantable) carries the same flow without the `tenantId` argument; the tenant is
  still resolved from `JobDataMap` for metadata and job bookkeeping.

## Implementation details

*Implementation detail, not contract:* the two job classes differ only in the `msg` they put on
`JobExecutionException` (`ImportJob` uses `""`, `TenantableImportJob` uses `ex.Message`) and in
minor message wording — do not depend on the difference.

## Evidence

`test/Ballware.Shared.Api.Jobs.Tests/TenantableImportJobTest.cs` and
`test/Ballware.Shared.Api.Jobs.Tests/ImportJobTest.cs`: `Execute_succeeds`,
`Execute_failed_mandatory_parameter_missing`, `Execute_failed_unknown_tenant`.
