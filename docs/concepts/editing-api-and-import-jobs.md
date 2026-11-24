# Editing API & import jobs

The mechanics behind `MapTenantableEditingApi` and the Quartz import workers. Package contracts:
[ballware-shared-api-endpoints.md](../packages/ballware-shared-api-endpoints.md),
[ballware-shared-api-jobs.md](../packages/ballware-shared-api-jobs.md),
[ballware-shared-api.md](../packages/ballware-shared-api.md).

## The 11 routes

Mapped in one call (`src/Ballware.Shared.Api.Endpoints/TenantantableEditingEndpoint.cs` →
`MapTenantableEditingApi`); all authenticated routes use `RequireAuthorization(authorizationScope)`
(default policy name `metaApi`) and declare 401/404 in OpenAPI.

| Route | Handler factory (`TenantableEndpointHandlerFactory`) | Rights check (`EditingEndpointBuilder`) | Produces |
| --- | --- | --- | --- |
| `GET {base}/all` | `CreateAllHandler` | `view`, param = empty map | 200 list |
| `GET {base}/query` | `CreateQueryHandler` | `view`, param = query params | 200 list |
| `GET {base}/new` | `CreateNewHandler` | `add`, param = empty map | 200 DTO |
| `GET {base}/byid` | `CreateByIdHandler` | `view`, param = fetched entry (404 first if missing) | 200 DTO |
| `POST {base}/save` | `CreateSaveHandler` | `identifier == "primary" ? edit : identifier`, param = value | 200 |
| `POST {base}/savebatch` | `CreateSaveBatchHandler` | batch variant, per-item param, first denial wins | 200 |
| `DELETE {base}/remove/{id}` | `CreateRemoveHandler` | `delete`, param = fetched entry (404 first) | 200; 400 on hook veto |
| `POST {base}/import` | `CreateImportHandler` | none at endpoint level (policy only) | 201 |
| `GET {base}/export` | `CreateExportHandler` | right = `identifier` itself | 200 content |
| `POST {base}/exporturl` | `CreateExportUrlHandler` | right = `identifier` itself | 200 `ExportUrlResult` |
| `GET {base}/download/{tenantId}/{id}` | `CreateDownloadExportHandler` | **`.AllowAnonymous()`** | 200 file / 404 |

Rights cascade per route: tenant stage then entity stage as described in
[authorization-and-rights-scripts.md](authorization-and-rights-scripts.md); `identifier` arrives
from the **query string**. Multi-value query params arrive pipe-joined `|v1|v2|`
(`TenantableEndpointHandlerFactory.GetQueryParams`).

## Import path (upload → job)

```
POST import (multipart file)
  → IFileStorageProvider.UploadTemporaryFileBehalfOfUserAsync   (temporaryId = new Guid)
  → IJobMetadataProvider.CreateJobAsync(tenantId, userId, "document", "import", options = serialized JobDataMap)
  → Quartz TriggerJob(JobKey.Create("import", entity), JobDataMap)
  → 201 Created
```

(`src/Ballware.Shared.Api.Endpoints/TenantableEndpointHandlerFactory.cs` → `CreateImportHandler`)

`JobDataMap` keys: `tenantId`, `userId`, `identifier`, `claims` (JSON), `file` (= temporaryId),
`jobId` (added after the metadata call). One trigger per uploaded file.

The Quartz job (`TenantableImportJob.Execute`, `ImportJob.Execute`) then:

1. validates `identifier` / `file` / tenant metadata (missing ⇒ `ArgumentException`),
2. `UpdateJobAsync(..., JobStates.InProgress)`,
3. `repository.ImportAsync(..., authorized: tenant-stage rights check with application `"meta"`,
   entity = job key group (= the endpoint's `entity`), right = `identifier`),
4. success: delete temporary file + `JobStates.Finished`; failure: `JobStates.Error` with serialized
   exception as `result` + `JobExecutionException(refireImmediately: false)`.

`JobStates` lifecycle: `Queued = 1` is the host's creation state, `InProgress = 5` / `Finished = 10`
/ `Error = 99` are written by the jobs (`src/Ballware.Shared.Api/Public/Job.cs` → `JobStates`;
`src/Ballware.Shared.Api.Jobs/TenantableImportJob.cs`). Whether a host honors `Queued` or prunes old
job records is outside the shared contracts.

## Export, ExportUrl, Download

- `GET export`: rights-checked export via `ITenantableRepository.ExportAsync` (JSON, see
  [data-model-and-tenancy.md](data-model-and-tenancy.md)), returned inline as content.
- `POST exporturl`: same export, then persisted for out-of-band download —
  `Export` metadata (`ExpirationStamp = DateTime.Now.AddDays(1)`, `Entity`, `Query`, `MediaType`) via
  `IExportMetadataProvider.CreateExportAsync` and the payload as a *temporary file* named
  `{exportId}{media extension}`; response `ExportUrlResult { TenantId, Id }`
  (`TenantableEndpointHandlerFactory.CreateExportUrlHandler`).
- `GET download/{tenantId}/{id}`: anonymous by design. 404 when metadata missing **or expired**
  (`export.ExpirationStamp <= DateTime.Now`) or file absent; serves the file as
  `{Query}_{timestamp}{extension}` (`CreateDownloadExportHandler`). The unguessable id is the entire
  access control — the URL is shareable until expiry.

Verified by `test/Ballware.Shared.Api.Endpoints.Tests/Editing/TenantableEditingApiTest.cs`
(`HandleExportToUrl_succeeds`, `HandleDownloadExport_succeeds`, `HandleDownloadExport_notFound`).
