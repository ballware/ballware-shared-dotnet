# Ballware.Shared.Data.Ef

## Purpose & dependencies

Generic EF Core implementation of the `Ballware.Shared.Data` repository contracts:
`BaseRepository<TEditable, TPersistable>` and `TenantableBaseRepository<TEditable, TPersistable>`
map public DTOs to persistable entities and implement the full read/save/remove/import/export
method set once.

Dependencies: project `Ballware.Shared.Data`; packages Mapster,
Microsoft.EntityFrameworkCore (+ Abstractions, + Relational), Newtonsoft.Json,
Microsoft.Extensions.Logging.Abstractions (`src/Ballware.Shared.Data.Ef/Ballware.Shared.Data.Ef.csproj`).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `IDbContext` | Thin facade over `DbContext` for testability: `Set<TEntity>()`, `SaveChangesAsync` | `src/Ballware.Shared.Data.Ef/IDbContext.cs` → `IDbContext` |
| `BaseRepository<TEditable, TPersistable>` | `TEditable : class, IEditable`, `TPersistable : class, IEntity, new()`; ctor `(IMapper, IDbContext, IRepositoryHook<TEditable, TPersistable>?)` | `src/Ballware.Shared.Data.Ef/Repository/BaseRepository.cs` → `BaseRepository` |
| `TenantableBaseRepository<TEditable, TPersistable>` | Same for `TPersistable : class, IEntity, ITenantable, new()`; implements `ITenantableRepository<TEditable>` | `src/Ballware.Shared.Data.Ef/Repository/TenantableBaseRepository.cs` → `TenantableBaseRepository` |
| Template points `ListQuery`, `ByIdQuery`, `ProduceNewAsync` | `protected virtual` seams subclasses override to add filtering or pre-populate new instances | `BaseRepository.ListQuery`, `ByIdQuery`, `ProduceNewAsync` in `src/Ballware.Shared.Data.Ef/Repository/BaseRepository.cs` |
| Async hook wrappers `ExtendByIdAsync`, `BeforeSaveAsync`, `AfterSaveAsync`, `RemovePreliminaryCheckAsync`, `BeforeRemoveAsync` | `protected virtual` dispatch to the registered hook, if any | `src/Ballware.Shared.Data.Ef/Repository/BaseRepository.cs` → `BaseRepository` |

## Wiring

No DI extension methods. Hosts register their own `IDbContext` implementation, a Mapster
`IMapper`, an optional hook, and derive from the base repositories, then register the derived
repository in DI (hosts such as ballware-meta-service / ballware-generic-service wrap this in their
own storage-registration extensions).

## Behavior & limits

- **Mapping.** DTO→entity and entity→DTO conversion always goes through Mapster (`Mapper.Map`);
  `IEditable.Id` binds to `IEntity.Uuid` by convention of the mapped types.
- **Query filtering built into `ListQuery`.** `queryParams["id"]` is honored: an
  `IEnumerable<string>` filters by `Uuid` containment, a single Guid-parsable value filters by
  `Uuid` equality. Subclasses extend by chaining onto the passed `IQueryable`.
- **Save upsert.** `SaveAsync` looks the row up by `Uuid == value.Id` (tenantable: also
  `TenantId == tenantId`). Insert stamps all four `IAuditable` fields with `DateTime.Now`; update
  stamps only `LastChangerId`/`LastChangeStamp` when the entity implements `IAuditable`. Hook order:
  `BeforeSave` → map/add-or-update → `AfterSave` → `SaveChangesAsync`.
- **Remove.** `removeParams["Id"]` (exact key `Id`) locates the row; then
  `RemovePreliminaryCheck` may veto (`Result = false` short-circuits without delete), otherwise
  `BeforeRemove` runs, the row is removed and saved. No row found is not an error — the result is
  `Result = true` with `Value = null`.
- **Import authorization.** `ImportAsync` reads the stream as a JSON array of `TEditable` and calls
  `SaveAsync` per item **only if** the caller-supplied `Func<TEditable, Task<bool>> authorized`
  returns true; unauthorized items are skipped silently.
- **Export.** `ExportAsync` re-runs `QueryAsync`, applies `ExtendByIdAsync` per item, and returns a
  UTF-8 JSON payload as `ExportResult` with `FileName = "{identifier}.json"`,
  `MediaType = "application/json"`.
- **Tenancy enforcement.** The tenantable variant applies `TenantId == tenantId` to *every* query
  (`AllAsync`, `QueryAsync`, `CountAsync`, `ByIdAsync`, `SaveAsync` lookup, `RemoveAsync` lookup)
  and forces `TenantId` on inserts (`ProduceNewAsync` and explicit re-assignment before `Add`).
  A wrong tenant simply yields no rows — there is no explicit cross-tenant error.
- **Threading.** List/count/export paths enumerate EF queries via `Task.Run` and
  `AsEnumerable().Select(Mapper.Map)`; they are not async database calls.

## Implementation details

*Implementation detail, not contract* (public but internal plumbing): the hook invocation helpers
wrap synchronous hook calls in `Task.Run`; `ByIdAsync` maps `FirstOrDefaultAsync()` results even
when null. Which query parameters are understood beyond `id` is entirely host-subclass territory.

## Evidence

`test/Ballware.Shared.Data.Ef.Tests/Repository/RepositoryTest.cs` and
`test/Ballware.Shared.Data.Ef.Tests/Tenantable/TenantableRepositoryTest.cs`:
`Query_items_succeeds`, `Save_and_remove_value_succeeds`, `Save_and_remove_value_with_hook_succeeds`,
`Save_and_remove_value_declined`, `Import_values_succeeds`, `Import_empty_succeeds`,
`Export_values_succeeds`.
