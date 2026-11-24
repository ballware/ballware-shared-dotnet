# Ballware.Shared.Mcp.Authorization

## Purpose & dependencies

Small adapter between the MCP tool model and the rights engine: produces a ready-made
`Tool.IsAuthorizedAsync` gate that runs the standard tenant→entity rights cascade against the
calling principal.

Dependencies: project `Ballware.Shared.Authorization` **only** — this package deliberately does not
reference `Ballware.Shared.Mcp`; the produced delegate is assignable to `Tool.IsAuthorizedAsync`
because the signatures match structurally
(`src/Ballware.Shared.Mcp.Authorization/Ballware.Shared.Mcp.Authorization.csproj`).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(application, entity, right)` | Returns `Func<IServiceProvider, ClaimsPrincipal, Task<bool>>`: resolves `IPrincipalUtils`, `IAuthorizationMetadataProvider`, `ITenantRightsChecker`, `IEntityRightsChecker` from the given (scoped) provider, derives the tenant from the principal, then evaluates tenant stage → entity stage with `param = null` | `src/Ballware.Shared.Mcp.Authorization/McpAuthorizationHandlerFactory.cs` → `CreateStaticEntityRightAuthorizationHandler` |

## Wiring

```csharp
services.AddBallwareMcpTools((serviceProvider, registry) =>
{
    registry.RegisterStaticTool(new Tool
    {
        Name = "entity.query",
        Description = "...",
        IsAuthorizedAsync = McpAuthorizationHandlerFactory
            .CreateStaticEntityRightAuthorizationHandler("app", "myentity", "view"),
        ExecuteAsync = async (sp, user, args) => { /* ... */ }
    });
});
```

The factory itself takes no services — all four collaborators are resolved **at gate invocation
time** from the `IServiceProvider` passed by the transport, so registration order does not matter.

## Behavior & limits

- **Missing metadata denies**: `tenantMetadata == null` or `entityMetadata == null` short-circuits to
  `false` (this differs from the editing endpoints, which report 404 instead) — evidence:
  `CreateStaticEntityRightAuthorizationHandler_Fail_NoTenantMetadata`,
  `..._Fail_NoEntityMetadata`.
- Only the **tenant id from `GetUserTenandId`** (the single-valued accessor) is used; multi-tenant
  principals are checked against their first tenant claim value only.
- The `application`/`entity`/`right` triple is fixed per handler — "static" in the factory name
  means one handler instance gates one right; per-entity dynamic tools should build their own gates.
- The gate is evaluated twice per call flow when both listing and calling happen
  (`tools/list` filter + `tools/call` re-check) — gates should be idempotent and reasonably cheap.
- Entity stage receives `param = null`: entity scripts that branch on the item cannot discriminate
  at MCP gating time.

## Implementation details

The factory is a single static method returning a closure; there are no internal types.

## Evidence

`test/Ballware.Shared.Mcp.Authorization.Tests/McpAuthorizationHandlerTest.cs`:
`CreateStaticEntityRightAuthorizationHandler_Success`,
`CreateStaticEntityRightAuthorizationHandler_Fail_NoTenantMetadata`,
`CreateStaticEntityRightAuthorizationHandler_Fail_NoEntityMetadata`,
`CreateStaticEntityRightAuthorizationHandler_Fail_NoRights`.
