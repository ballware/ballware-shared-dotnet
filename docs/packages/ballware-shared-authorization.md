# Ballware.Shared.Authorization

## Purpose & dependencies

Claim-based authorization contracts: principal extraction from a `ClaimsPrincipal`, the two checker
interfaces of the rights cascade, the script-carrying metadata interfaces, and the metadata
provider seam. Depends only on Microsoft.Extensions.DependencyInjection.Abstractions
(`src/Ballware.Shared.Authorization/Ballware.Shared.Authorization.csproj`) — no data dependencies.

The cascade itself and the scripted evaluation semantics are described in
[concepts/authorization-and-rights-scripts.md](../concepts/authorization-and-rights-scripts.md);
the script engine lives in [ballware-shared-authorization-jint.md](ballware-shared-authorization-jint.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `IPrincipalUtils.GetUserId` | Guid user id read from the configured user-id claim | `src/Ballware.Shared.Authorization/IPrincipalUtils.cs` → `GetUserId` |
| `IPrincipalUtils.GetUserTenandId` | Single tenant id from the configured tenant claim (**name is misspelled in the API and kept as-is**) | `src/Ballware.Shared.Authorization/IPrincipalUtils.cs` → `GetUserTenandId` |
| `IPrincipalUtils.GetUserTenantIds` | All tenant ids (tenant claim may appear multiple times); throws `ArgumentException` on an unparsable claim value | `src/Ballware.Shared.Authorization/IPrincipalUtils.cs` → `GetUserTenantIds` |
| `IPrincipalUtils.GetUserClaims` | All claims as `Dictionary<string, object>`; repeated claim types collapse to a `string[]`, single values stay `string` | `src/Ballware.Shared.Authorization/IPrincipalUtils.cs` → `GetUserClaims` |
| `IPrincipalUtils.GetUserRights` | Right values from the configured right claim | `src/Ballware.Shared.Authorization/IPrincipalUtils.cs` → `GetUserRights` |
| `ITenantRightsChecker.HasRightAsync` | Stage 1: tenant-level decision for `(application, entity, right)` against tenant metadata + claims | `src/Ballware.Shared.Authorization/ITenantRightsChecker.cs` → `HasRightAsync` |
| `IEntityRightsChecker.HasRightAsync` | Stage 2: entity-level decision; receives the tenant stage result as `tenantResult` and the handler-supplied `param` | `src/Ballware.Shared.Authorization/IEntityRightsChecker.cs` → `HasRightAsync` |
| `IEntityRightsChecker.StateAllowedAsync` | State-machine gating: `currentState` int + the user's rights, evaluated by the entity's `StateAllowedScript` | `src/Ballware.Shared.Authorization/IEntityRightsChecker.cs` → `StateAllowedAsync` |
| `ITenantAuthorizationMetadata` | Carries `string? RightsCheckScript` | `src/Ballware.Shared.Authorization/ITenantAuthorizationMetadata.cs` |
| `IEntityAuthorizationMetadata` | `Application`, `Entity`, `RightsCheckScript?`, `StateAllowedScript?` | `src/Ballware.Shared.Authorization/IEntityAuthorizationMetadata.cs` |
| `IAuthorizationMetadataProvider` | Resolves metadata per tenant / per `(tenantId, entity)`; `null` means "not found" | `src/Ballware.Shared.Authorization/IAuthorizationMetadataProvider.cs` |

## Wiring

```csharp
services.AddBallwareSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim);
```

Registers `IPrincipalUtils` as a singleton configured with the three claim type names
(`src/Ballware.Shared.Authorization/ServiceCollectionExtensions.cs` →
`ServiceCollectionExtensions.AddBallwareSharedAuthorizationUtils`). Hosts pick claim names matching
their identity provider (e.g. ballware-generic-service passes its `Authorization:TenantClaim`,
`Authorization:UserIdClaim`, `Authorization:RightClaim` options).

The checkers and the metadata provider are **not** registered by this package — hosts supply an
engine (see the Jint package) and their own `IAuthorizationMetadataProvider` implementation backed
by their metadata store.

## Behavior & limits

- `GetUserId` / `GetUserTenandId` return `Guid.Empty` when the claim is absent or unparsable
  (`Guid.TryParse` swallows failure); only `GetUserTenantIds` throws.
- `GetUserClaims` includes every claim of the principal — including internal ones like `sub`,
  `iss`, `scope` — because scripts may legitimately inspect them.
- Rights are just claim values; this package defines no rights grammar. The conventional
  `<application>.<entity>.<right>` shape is only assembled inside the checkers (see the Jint
  package and concepts page).
- All checker methods are per-call; implementations are expected to be stateless (the Jint
  implementation is registered singleton and creates a fresh engine per evaluation).

## Implementation details

*Implementation detail, not contract:* `DefaultPrincipalUtils` (internal,
`src/Ballware.Shared.Authorization/Internal/DefaultPrincipalUtils.cs`) is the built-in
`IPrincipalUtils`; hosts may replace the registration with their own implementation.

## Evidence

`test/Ballware.Shared.Authorization.Tests/DefaultPrincipalUtilsTest.cs`:
`Get_userid_from_principal_should_succeed`, `Get_tenant_from_principal_should_succeed`,
`Get_tenant_ids_from_principal_should_succeed`,
`Get_tenant_ids_from_principal_with_invalid_tenant_claim_should_throw`,
`Get_tenant_ids_from_principal_without_tenant_claims_should_return_empty_result`,
`Get_claims_from_principal_should_succeed`, `Get_rights_from_principal_should_succeed`.
Registration: `test/Ballware.Shared.Authorization.Tests/ServiceCollectionExtensionsTest.cs` →
`AddBallwareMetaAuthorizationUtils_succeeds`.
