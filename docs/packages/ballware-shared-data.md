# Ballware.Shared.Data

## Purpose & dependencies

Pure persistence contracts: the storage model (persistable interfaces), the repository method set,
the extension hooks, and result records. This package contains **no implementation** and has **no
dependencies** — its csproj declares neither `PackageReference` nor `ProjectReference`
(`src/Ballware.Shared.Data/Ballware.Shared.Data.csproj`).

Everything downstream (`Ballware.Shared.Data.Ef`, `Ballware.Shared.Api.Endpoints`,
`Ballware.Shared.Api.Jobs`) consumes these contracts. See
[concepts/data-model-and-tenancy.md](../concepts/data-model-and-tenancy.md) for the cross-package
model this package defines.

## Public contract

### Persistables (`Ballware.Shared.Data.Persistables`)

| Member | Semantics | Evidence |
| --- | --- | --- |
| `IEntity` | Database row: `long? Id` (db key) + `Guid Uuid` (public key) | `src/Ballware.Shared.Data/Persistables/IEntity.cs` → `IEntity.Id`, `IEntity.Uuid` |
| `ITenantable` | Row belongs to a tenant: `Guid TenantId` | `src/Ballware.Shared.Data/Persistables/ITenantable.cs` → `ITenantable.TenantId` |
| `IAuditable` | `Guid? CreatorId`, `DateTime? CreateStamp`, `Guid? LastChangerId`, `DateTime? LastChangeStamp` — stamped by the EF implementation, never by callers | `src/Ballware.Shared.Data/Persistables/IAuditable.cs` → `IAuditable` |
| `IEditable` | Public DTO contract: get-only `Guid Id`, always mapped onto the entity's `Uuid` | `src/Ballware.Shared.Data/Public/IEditable.cs` → `IEditable.Id` |

### Repositories (`Ballware.Shared.Data.Repository`)

| Member | Semantics | Evidence |
| --- | --- | --- |
| `IRepository<TEditable>.AllAsync` / `QueryAsync` / `CountAsync` / `ByIdAsync` / `NewAsync` / `NewQueryAsync` | Reads; every method takes `string identifier` + `IDictionary<string, object> claims` | `src/Ballware.Shared.Data/Repository/IRepository.cs` → `IRepository` |
| `IRepository<TEditable>.SaveAsync` / `RemoveAsync` / `ImportAsync` | Mutations; additionally take the acting `Guid? userId` | `src/Ballware.Shared.Data/Repository/IRepository.cs` → `IRepository` |
| `IRepository<TEditable>.ExportAsync` | Returns `ExportResult` for a query identifier + params | `src/Ballware.Shared.Data/Repository/IRepository.cs` → `ExportAsync` |
| `ITenantableRepository<TEditable>` | Identical method set, with `Guid tenantId` as leading parameter on every call | `src/Ballware.Shared.Data/Repository/ITenantableRepository.cs` → `ITenantableRepository` |
| `IRepositoryHook<TEditable, TPersistable>` / `ITenantableRepositoryHook<TEditable, TPersistable>` | Extension seam via default interface methods: `ExtendById`, `BeforeSave`, `AfterSave`, `RemovePreliminaryCheck`, `BeforeRemove` | `src/Ballware.Shared.Data/Repository/IRepositoryHook.cs`, `src/Ballware.Shared.Data/Repository/ITenantableRepositoryHook.cs` |
| `RemoveResult<TEditable>` | struct: `bool Result`, `IEnumerable<string> Messages`, `TEditable? Value`; hook default is `Result = true` | `src/Ballware.Shared.Data/Repository/RemoveResult.cs` → `RemoveResult` |
| `ExportResult` | struct: `string FileName`, `string MediaType`, `byte[] Data` | `src/Ballware.Shared.Data/Repository/ExportResult.cs` → `ExportResult` |

The `identifier` argument is a query/configuration name chosen by the caller (e.g. `"primary"`,
`"export"`), not an entity id; `claims` is the acting user's claims as a dictionary — identity
travels as data, nothing is injected ambiently.

## Wiring

This package provides **no DI or mapping extension methods**. It is consumed by implementing or
consuming types directly.

## Behavior & limits

- Hook methods are default interface methods: an implementer may override only the seam it needs;
  untouched seams pass values through unchanged (`ExtendById` returns the value,
  `BeforeSave`/`AfterSave`/`BeforeRemove` do nothing, `RemovePreliminaryCheck` allows).
- `RemovePreliminaryCheck` is the veto point for deletes: returning `Result = false` with messages
  aborts the removal before anything is deleted.
- Contracts are synchronous-shaped `Task` APIs with no cancellation-token parameters.
- `TEditable : class` is the only generic constraint at the contract level; the EF base classes add
  `IEditable` / `IEntity` constraints (see [ballware-shared-data-ef.md](ballware-shared-data-ef.md)).

## Implementation details

None — the package contains only public interfaces and result structs.

## Evidence

No sibling test project exists for this package. The contracts are verified through the
implementation tests of the consuming layer:
`test/Ballware.Shared.Data.Ef.Tests/Repository/RepositoryTest.cs` and
`test/Ballware.Shared.Data.Ef.Tests/Tenantable/TenantableRepositoryTest.cs` exercise the repository
and hook behavior defined here (`Save_and_remove_value_with_hook_succeeds`,
`Save_and_remove_value_declined`, `Import_values_succeeds`, `Export_values_succeeds`).
