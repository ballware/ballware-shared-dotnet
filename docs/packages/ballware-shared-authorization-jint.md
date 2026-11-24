# Ballware.Shared.Authorization.Jint

## Purpose & dependencies

JavaScript rights engine implementation of the `Ballware.Shared.Authorization` checker interfaces,
evaluated with the embedded Jint interpreter. Tenant and entity permission rules are stored as
JavaScript snippets per tenant/entity and executed server-side.

Dependencies: project `Ballware.Shared.Authorization`; packages Jint,
Microsoft.Extensions.DependencyInjection.Abstractions
(`src/Ballware.Shared.Authorization.Jint/Ballware.Shared.Authorization.Jint.csproj`).

Overall pipeline and grammar: [concepts/authorization-and-rights-scripts.md](../concepts/authorization-and-rights-scripts.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `ServiceCollectionExtensions.AddBallwareSharedJintRightsChecker()` | Registers `ITenantRightsChecker` and `IEntityRightsChecker` singletons with the Jint-backed implementations | `src/Ballware.Shared.Authorization.Jint/ServiceCollectionExtensions.cs` → `AddBallwareSharedJintRightsChecker` |
| Tenant script contract | Script body sees `right` (pre-joined string `{application}.{entity}.{right}`) and `userinfo` (claims serialized to JSON, parsed in-script); must `return` a boolean | `src/Ballware.Shared.Authorization.Jint/Internal/JavascriptTenantRightsChecker.cs` → `HasRightAsync` |
| Entity script contract | Script body sees `application`, `entity`, `right`, `param` (object from the handler) and `result` (tenant stage outcome); its `return` **overrides** `result` | `src/Ballware.Shared.Authorization.Jint/Internal/JavascriptEntityRightsChecker.cs` → `HasRightAsync` |
| State script contract | Script is evaluated as-is with `state` (int), `hasRight(right)` (exact match, case-insensitively lowered argument, against the principal's rights) and `hasAnyRight(prefix)` (`StartsWith` prefix match) | `src/Ballware.Shared.Authorization.Jint/Internal/JavascriptEntityRightsChecker.cs` → `StateAllowedAsync` |

## Wiring

```csharp
services.AddBallwareSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim); // from Ballware.Shared.Authorization
services.AddBallwareSharedJintRightsChecker();
```

Pairing is conventional, not enforced: the Jint package supplies only the checkers. Hosts combine
them with their `IAuthorizationMetadataProvider` (e.g. ballware-generic-service's Startup calls
`AddBallwareSharedAuthorizationUtils` followed by `AddBallwareSharedJintRightsChecker`).

## Behavior & limits

Defaults without scripts (verified against the tests):

| Stage | No script | Evidence |
| --- | --- | --- |
| Tenant | **allow** (`HasRightAsync` returns `true`) | `JavascriptTenantRightsChecker.HasRightAsync` initialized `result = true`, skipped when `RightsCheckScript` is null/whitespace |
| Entity | **pass-through** (`result = tenantResult` unchanged) | `JavascriptEntityRightsChecker.HasRightAsync` initialized `result = tenantResult` |
| State (`StateAllowedAsync`) | **deny** (returns `false` when `StateAllowedScript` null/empty) | `JavascriptEntityRightsChecker.StateAllowedAsync` |

- Tenant script mechanics: the script text is injected into `function rightsCheck() { … }` and the
  wrapper is invoked; the completion value is read with `AsBoolean`. Entity script mechanics: the
  wrapper `function extendedRightsCheck() { … }` is evaluated and its return converted via
  `bool.Parse(...ToString())`. A script that does not return a boolean-parseable value throws.
- A **new Jint `Engine` is constructed per evaluation**; scripts cannot share state across calls,
  and no host facilities beyond the injected variables are exposed.
- `hasAnyRight` matches rights by prefix (`StartsWith`); the prefix/right argument is lowered with
  `ToLowerInvariant`, so stored rights are expected lowercase.
- Scripts are plain JavaScript stored in tenant/entity metadata — treat them as trusted operator
  configuration; there is no sandboxing beyond Jint's own interpreter limits.

## Implementation details

*Implementation detail, not contract:* `JavascriptTenantRightsChecker` and
`JavascriptEntityRightsChecker` are internal; swapping the engine is a contract-level extension
point — implement `ITenantRightsChecker` / `IEntityRightsChecker` and register those instead
(see [usage/extension-points.md](../usage/extension-points.md)).

## Evidence

`test/Ballware.Shared.Authorization.Jint.Tests/JavascriptTenantRightsCheckerTest.cs`:
`HasRightAsync_ShouldReturnTrue_WhenRightsCheckScriptIsEmpty` (tenant default allow),
`HasRightAsync_ShouldReturnTrue_WhenScriptReturnsTrue`,
`HasRightAsync_ShouldReturnTrue_WhenUserinfoContainsRight`.

`test/Ballware.Shared.Authorization.Jint.Tests/JavascriptEntityRightsCheckerTest.cs`:
`HasRightAsync_ShouldReturnTrue_WhenRightsCheckScriptIsEmpty` (entity pass-through),
`HasRightAsync_ShouldReturnTrue_WhenTenantResultIsTrue`,
`StateAllowedAsync_ShouldReturnFalse_WhenStateAllowedScriptIsEmpty` (state default deny),
`StateAllowedAsync_ShouldReturnTrue_WhenUserHasDedicatedRight`,
`StateAllowedAsync_ShouldReturnTrue_WhenUserHasAnyRight`.

Registration: `test/Ballware.Shared.Authorization.Jint.Tests/ServiceCollectionExtensionsTest.cs` →
`AddBallwareMetaJintRightsChecker_succeeds`.
