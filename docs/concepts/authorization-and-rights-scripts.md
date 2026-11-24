# Authorization & rights scripts

One explanation of the rights machinery; the contract interfaces live in
[ballware-shared-authorization.md](../packages/ballware-shared-authorization.md), the Jint engine in
[ballware-shared-authorization-jint.md](../packages/ballware-shared-authorization-jint.md).

## Right grammar

A right is addressed as the triple `(application, entity, right)`. At the **tenant stage** the Jint
checker joins it into the single string `<application>.<entity>.<right>` exposed to the script as
`right` (`src/Ballware.Shared.Authorization.Jint/Internal/JavascriptTenantRightsChecker.cs` →
`HasRightAsync`: `SetValue("right", $"{application}.{entity}.{right}")`). At the **entity stage** the
three components arrive separately (`application`, `entity`, `right`) plus `param` and `result`
(`JavascriptEntityRightsChecker.HasRightAsync`).

The principal-side counterpart is the claim configured as right claim
(`IPrincipalUtils.GetUserRights`, `src/Ballware.Shared.Authorization/IPrincipalUtils.cs`); stored
rights are compared lowercase (`ToLowerInvariant`) by the state helpers
(`JavascriptEntityRightsChecker.StateAllowedAsync`).

Callers choose the triple: editing endpoints pass the `application`/`entity` given to
`MapTenantableEditingApi` and the right name from the handler factory
(`src/Ballware.Shared.Api.Endpoints/TenantableEndpointHandlerFactory.cs`); import jobs hardcode
application `"meta"` with entity = Quartz job group
(`src/Ballware.Shared.Api.Jobs/TenantableImportJob.cs` → `Execute`); MCP handlers use their
configured triple (`src/Ballware.Shared.Mcp.Authorization/McpAuthorizationHandlerFactory.cs`).

## Two-stage pipeline

```
IAuthorizationMetadataProvider.MetadataForTenantByIdAsync(tenantId)
  → ITenantRightsChecker.HasRightAsync(tenant, application, entity, claims, right)   [stage 1]
IAuthorizationMetadataProvider.MetadataForEntityByTenantAndIdentifierAsync(tenantId, entity)
  → IEntityRightsChecker.HasRightAsync(tenantId, entity, claims, right, param, tenantResult)  [stage 2]
```

Stage 2 receives stage 1's boolean as `result` and the entity script may **override** it in either
direction: the entity checker initializes `result = tenantResult` and a present script's return value
replaces it (`JavascriptEntityRightsChecker.HasRightAsync`). Without scripts the cascade is
allow-everything (tenant default allow) and per-state it is deny (`StateAllowedAsync` default).

Defaults are test-verified in
`test/Ballware.Shared.Authorization.Jint.Tests/JavascriptTenantRightsCheckerTest.cs` /
`JavascriptEntityRightsCheckerTest.cs` (`..._ShouldReturnTrue_WhenRightsCheckScriptIsEmpty`,
`StateAllowedAsync_ShouldReturnFalse_WhenStateAllowedScriptIsEmpty`).

Canonical cascade runners (evidence of the pattern in production code):
`EditingEndpointBuilder.ExecuteAsync` (`src/Ballware.Shared.Api.Endpoints/EditingEndpointBuilder.cs`
— 404 on missing tenant/entity metadata, 401 on denial, batch loop short-circuits on first denial)
and `McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler`
(`src/Ballware.Shared.Mcp.Authorization/McpAuthorizationHandlerFactory.cs` — `false` on missing
metadata).

## Script variable surface (Jint)

| Script | Variables | Missing-script default | Evidence |
| --- | --- | --- | --- |
| Tenant `RightsCheckScript` | `right` (joined string), `userinfo` (claims as JSON object) | allow | `JavascriptTenantRightsChecker.HasRightAsync` |
| Entity `RightsCheckScript` | `application`, `entity`, `right`, `param` (any object), `result` (bool from tenant stage) | pass-through | `JavascriptEntityRightsChecker.HasRightAsync` |
| Entity `StateAllowedScript` | `state` (int), `hasRight(right)` (exact, case-insensitive), `hasAnyRight(prefix)` (prefix match), plus the principal's rights behind the helpers | deny | `JavascriptEntityRightsChecker.StateAllowedAsync` |

Scripts are function *bodies*: they must `return` a boolean (tenant wrapped into `rightsCheck()`,
entity into `extendedRightsCheck()`; state scripts are evaluated as-is). A non-boolean-parseable
result throws. A fresh Jint engine is built per evaluation — no cross-call state.

## Metadata & script storage

Rules travel as nullable script strings on the metadata interfaces
(`src/Ballware.Shared.Authorization/ITenantAuthorizationMetadata.cs`,
`IEntityAuthorizationMetadata.cs`) resolved per call via `IAuthorizationMetadataProvider`
(`src/Ballware.Shared.Authorization/IAuthorizationMetadataProvider.cs`). Which store backs them
(tenant metadata entities in a meta service, host-specific providers, …) is host territory —
hosts implement the provider; the shared packages never do.

`StateAllowedAsync(tenantId, metadata, id, currentState, rights)` is the state-machine gate:
hosts pass an entity id, its current state int, and the principal's rights; the script decides.

## Swapping the engine

The pipeline is defined entirely by `ITenantRightsChecker` / `IEntityRightsChecker`. Registering a
different implementation instead of `AddBallwareSharedJintRightsChecker()` replaces the language
without touching endpoints or jobs (see [usage/extension-points.md](../usage/extension-points.md)).
`AddBallwareSharedAuthorizationUtils` (claim mapping) is independent of the engine choice.
