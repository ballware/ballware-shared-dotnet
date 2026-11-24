# Ballware.Shared.Api

## Purpose & dependencies

Provider interfaces and DTOs that decouple the HTTP endpoints layer from file storage and job/export
bookkeeping. Every operation is tenant-scoped (`Guid tenantId` as leading parameter). This package
has **no dependencies** — no `PackageReference`, no `ProjectReference`
(`src/Ballware.Shared.Api/Ballware.Shared.Api.csproj`).

The consumers are [ballware-shared-api-endpoints.md](ballware-shared-api-endpoints.md) (HTTP side)
and [ballware-shared-api-jobs.md](ballware-shared-api-jobs.md) (Quartz side); the flow is described
in [concepts/editing-api-and-import-jobs.md](../concepts/editing-api-and-import-jobs.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `IFileStorageProvider.TemporaryFileByIdAsync` | Open a stored temporary file as `Stream` | `src/Ballware.Shared.Api/IFileStorageProvider.cs` → `TemporaryFileByIdAsync` |
| `IFileStorageProvider.UploadTemporaryFileBehalfOfUserAsync` | Store `(tenantId, userId, temporaryId, fileName, contentType, Stream data)`; `temporaryId` is caller-generated | `src/Ballware.Shared.Api/IFileStorageProvider.cs` → `UploadTemporaryFileBehalfOfUserAsync` |
| `IFileStorageProvider.RemoveTemporaryFileByIdBehalfOfUserAsync` | Delete a temporary file after processing | `src/Ballware.Shared.Api/IFileStorageProvider.cs` → `RemoveTemporaryFileByIdBehalfOfUserAsync` |
| `IJobMetadataProvider.CreateJobAsync` | Persist job bookkeeping `(tenantId, userId, scheduler, identifier, options?)`, returns the job id | `src/Ballware.Shared.Api/IJobMetadataProvider.cs` → `CreateJobAsync` |
| `IJobMetadataProvider.UpdateJobAsync` | Report state transition `(tenantId, userId, id, JobStates, result?)`; `result` carries serialized error details | `src/Ballware.Shared.Api/IJobMetadataProvider.cs` → `UpdateJobAsync` |
| `IExportMetadataProvider.CreateExportAsync` | Persist export bookkeeping `(tenantId, userId, Export)`, returns the export id | `src/Ballware.Shared.Api/IExportMetadataProvider.cs` → `CreateExportAsync` |
| `IExportMetadataProvider.GetExportByIdAsync` | Fetch export metadata or `null`; used to enforce expiration on download | `src/Ballware.Shared.Api/IExportMetadataProvider.cs` → `GetExportByIdAsync` |
| `Export` | DTO: `Guid Id`, `Application?`, `Entity?`, `Query?`, `DateTime? ExpirationStamp`, `MediaType?` | `src/Ballware.Shared.Api/Public/Export.cs` → `Export` |
| `ExportUrlResult` | Response DTO of the exporturl route: `Guid TenantId`, `Guid Id` — the download URL is built from these | `src/Ballware.Shared.Api/Public/ExportUrlResult.cs` → `ExportUrlResult` |
| `JobStates` | Lifecycle enum: `Unknown = 0`, `Queued = 1`, `InProgress = 5`, `Finished = 10`, `Error = 99` | `src/Ballware.Shared.Api/Public/Job.cs` → `JobStates` |

## Wiring

No DI extension methods. Hosts implement the three provider interfaces against their own storage
(usually temporary blob storage plus `Job`/`Export` entities) and register them in DI; the endpoints
and jobs packages resolve them per request.

## Behavior & limits

- The interfaces define **no** semantics for where files live or how jobs are persisted — only the
  tenant/user scoping and the id handoff contract (`temporaryId`, job id, export id).
- `JobStates.Unknown` exists as enum default but is never reported by the shared job
  implementations; `Queued` is written by host code at creation time (the endpoints layer's
  `CreateJobAsync` call happens before any `UpdateJobAsync`), transitions during execution are
  `InProgress` → `Finished` or `Error`. Whether hosts honor `Queued` and whether hosts purge expired
  exports is host responsibility — not implemented in this repository.
- `Export.ExpirationStamp` is advisory in this package: only the endpoints download handler
  enforces it (see the endpoints page evidence `HandleDownloadExport_notFound`).

## Implementation details

None — pure interfaces and DTOs.

## Evidence

No sibling test project. The contracts are exercised by the consumers' tests:
`test/Ballware.Shared.Api.Endpoints.Tests/Editing/TenantableEditingApiTest.cs` mocks all three
providers (`HandleImport_succeeds`, `HandleExportToUrl_succeeds`, `HandleDownloadExport_succeeds`,
`HandleDownloadExport_notFound`) and
`test/Ballware.Shared.Api.Jobs.Tests/ImportJobTest.cs` /
`test/Ballware.Shared.Api.Jobs.Tests/TenantableImportJobTest.cs` exercise `UpdateJobAsync` state
reporting.
