# Data model & tenancy

Single explanation of the persistence model shared by all packages; package-level details are in
[ballware-shared-data.md](../packages/ballware-shared-data.md) and
[ballware-shared-data-ef.md](../packages/ballware-shared-data-ef.md).

## Interface hierarchy

```
IEntity { long? Id; Guid Uuid }            database row identity
  ├─ + ITenantable { Guid TenantId }       tenant-scoped rows
  └─ + IAuditable { CreatorId, CreateStamp, LastChangerId, LastChangeStamp }

IEditable { Guid Id }                      public DTO contract (get-only Id)
```

Evidence: `src/Ballware.Shared.Data/Persistables/IEntity.cs`, `ITenantable.cs`, `IAuditable.cs`,
`src/Ballware.Shared.Data/Public/IEditable.cs`.

The public `IEditable.Id` **is** the entity's `Uuid`; the `long? Id` database key never crosses the
API boundary. Mapping between the two shapes is Mapster's job inside
`BaseRepository`/`TenantableBaseRepository` (`src/Ballware.Shared.Data.Ef/Repository/BaseRepository.cs` →
`Mapper.Map`).

## Repository method set

`IRepository<TEditable>` and `ITenantableRepository<TEditable>` expose the same 10 operations —
`AllAsync`, `QueryAsync`, `CountAsync`, `ByIdAsync`, `NewAsync`, `NewQueryAsync`, `SaveAsync`,
`RemoveAsync`, `ImportAsync`, `ExportAsync` — the tenantable variant prefixing `Guid tenantId`
(`src/Ballware.Shared.Data/Repository/IRepository.cs`, `ITenantableRepository.cs`). Reads always
carry `identifier` + `claims`; mutations additionally carry the acting `Guid? userId`.

`identifier` names a **query definition** (host-side query configuration, e.g. `primary`, `export`),
not a row id; repository subclasses translate `queryParams` into predicates (the EF base classes only
pre-implement `id` filtering: `BaseRepository.ListQuery`).

## Tenancy enforcement (EF implementation)

`TenantableBaseRepository` (`src/Ballware.Shared.Data.Ef/Repository/TenantableBaseRepository.cs`):

- every read composes `Where(t => t.TenantId == tenantId)` before any other filtering;
- `SaveAsync`/`RemoveAsync` look rows up by `TenantId == tenantId && Uuid == id`;
- inserts force `TenantId = tenantId` after mapping, independent of the DTO;
- cross-tenant access is silent: a foreign row simply is not found (`ByIdAsync` → `null`).

## Result records

- `RemoveResult<TEditable>` (struct): `bool Result`, `IEnumerable<string> Messages`, `TEditable? Value`
  — the channel for hook vetoes and the deleted value (`src/Ballware.Shared.Data/Repository/RemoveResult.cs`).
- `ExportResult` (struct): `FileName`, `MediaType`, `byte[] Data`
  (`src/Ballware.Shared.Data/Repository/ExportResult.cs`); the EF export always produces
  `{identifier}.json` / `application/json` JSON of the query result
  (`TenantableBaseRepository.ExportAsync`).

## Hook sequence on Save/Remove

Hooks are default-interface-method seams (`src/Ballware.Shared.Data/Repository/IRepositoryHook.cs`,
`ITenantableRepositoryHook.cs`), dispatched by the EF base classes in this order:

**SaveAsync** (`TenantableBaseRepository.SaveAsync`):
1. row lookup by tenant+`Uuid` (insert vs. update decision),
2. `BeforeSave(..., insert)` →
3. Mapster map into persisted entity (insert: Mapster map + `IAuditable` creator+changer stamping;
   update: Mapster map-onto + changer-only stamping),
4. `AfterSave(..., persistable, insert)` →
5. `SaveChangesAsync()`.

**RemoveAsync** (`TenantableBaseRepository.RemoveAsync`):
1. locate row from `removeParams["Id"]`,
2. `RemovePreliminaryCheck(..., removeValue)` — `Result = false` short-circuits, nothing is deleted,
3. `BeforeRemove(persistable)` →
4. `Remove` + `SaveChangesAsync()`. A missing row still returns `Result = true`, `Value = null`.

**Read enrichment:** `ByIdAsync` and `ExportAsync` run each result through `ExtendById` (via the
`ExtendByIdAsync` wrapper) so hosts can enrich DTOs (permissions, computed fields) per read.

## Import/export as data paths

- `ImportAsync` takes a `Stream` (JSON array of DTOs in the EF implementation) plus a
  caller-supplied `Func<TEditable, Task<bool>> authorized` predicate; items failing the predicate
  are skipped without error (`BaseRepository.ImportAsync`). Who supplies the predicate: the import
  jobs (see [editing-api-and-import-jobs.md](editing-api-and-import-jobs.md)).
- `ExportAsync` serializes the *full query result* (after `ExtendById`) — no pagination or
  field selection exists at this level.
