# Ballware.Shared.Mcp

## Purpose & dependencies

Transport-agnostic MCP tool model and registry: tool definitions with per-user authorization gates,
a registry supporting static tools plus per-request dynamic tool providers, and the startup wiring
that configures the registry once with full DI access.

Dependencies: Microsoft.Extensions.DependencyInjection.Abstractions,
Microsoft.Extensions.Hosting.Abstractions, Microsoft.Extensions.Logging.Abstractions — no ASP.NET,
no MCP SDK (`src/Ballware.Shared.Mcp/Ballware.Shared.Mcp.csproj`).

Lifecycle and request handling: [concepts/mcp-tool-registry-and-endpoint.md](../concepts/mcp-tool-registry-and-endpoint.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `Tool` | `required string Name`, `required string Description`, `IEnumerable<ToolParam> Params`, `string? OutputSchema` (JSON), optional `Func<IServiceProvider, ClaimsPrincipal, Task<bool>>? IsAuthorizedAsync`, `required Func<IServiceProvider, ClaimsPrincipal?, IDictionary<string, object?>, Task<ToolResult>> ExecuteAsync` | `src/Ballware.Shared.Mcp/Tool.cs` → `Tool` |
| `ToolParam` | `required ToolParamType Type`, `required string Name`, `required string Description`, `bool Required = false` | `src/Ballware.Shared.Mcp/ToolParam.cs` → `ToolParam` |
| `ToolParamType` | enum `String = 1`, `Number = 2`, `Boolean = 3` | `src/Ballware.Shared.Mcp/ToolParamType.cs` |
| `ToolResult` | `string? Text` and/or `JsonElement? StructuredContent`; factories `FromText`, `FromStructuredContent(content)`, `FromStructuredContent(content, text)` | `src/Ballware.Shared.Mcp/ToolResult.cs` → `ToolResult` |
| `IToolRegistry` | `RegisterStaticTool(Tool)`, `RegisterDynamicToolProvider(Func<IServiceProvider, ClaimsPrincipal?, Task<IEnumerable<Tool>>>)`, `GetAllToolsAsync(IServiceProvider, ClaimsPrincipal?)`, `GetToolByNameAsync(IServiceProvider, ClaimsPrincipal?, string)` | `src/Ballware.Shared.Mcp/IToolRegistry.cs` → `IToolRegistry` |

## Wiring

```csharp
services.AddBallwareMcpTools((serviceProvider, registry) =>
{
    registry.RegisterStaticTool(new Tool { /* ... */ });
    registry.RegisterDynamicToolProvider(async (sp, user) => /* per-user tools */);
});
```

`src/Ballware.Shared.Mcp/ServiceCollectionExtensions.cs` → `AddBallwareMcpTools`: registers the
registry as a **singleton** and a hosted service that, at startup, creates a fresh DI **scope** and
invokes the configure delegate **once** with `(scope.ServiceProvider, registry)`. Static registration
happens at startup with full DI access; dynamic providers run **per request** with the request's
scoped provider and the request's user.

## Behavior & limits

- Static tools are keyed by `Name`: registering the same name twice **overwrites** the earlier tool.
- `GetAllToolsAsync` / `GetToolByNameAsync` rebuild the effective tool set on every call
  (static tools + all dynamic providers invoked in order); dynamic providers see the per-request
  `IServiceProvider` and `ClaimsPrincipal?`, so tools can vary per user or DI state.
- `Tool.IsAuthorizedAsync` is only a *gate contract* here — who evaluates it (and when) is defined by
  the transport layer ([ballware-shared-mcp-endpoints.md](ballware-shared-mcp-endpoints.md)); the
  standard gate factory lives in [ballware-shared-mcp-authorization.md](ballware-shared-mcp-authorization.md).
- `ExecuteAsync` receives the caller's `ClaimsPrincipal` — tools resolve services (repositories,
  checkers) from the passed `IServiceProvider` themselves; the registry itself is not used inside
  execution.
- `OutputSchema` is a raw JSON string here; parsing and attaching it to the protocol happens in the
  endpoints package.

## Implementation details

*Implementation detail, not contract:* `ToolRegistry` (a dictionary of static tools plus a list of
providers rebuilt per query) and `ToolRegistryInitializer` (`IHostedService` that runs the configure
delegate once in a scope) are internal
(`src/Ballware.Shared.Mcp/Internal/ToolRegistry.cs`, `src/Ballware.Shared.Mcp/Internal/ToolRegistryInitializer.cs`).
Concurrency note: registration is expected during startup only; the internal collections are not
thread-safe for concurrent mutation while requests are in flight.

## Evidence

No sibling test project; the registry is covered through the transport tests via `InternalsVisibleTo`:
`test/Ballware.Shared.Mcp.Endpoints.Tests/ToolRegistryTest.cs` (`RegisterTool_AndGetToolByName_ReturnsSameTool`,
`RegisterTool_WithSameName_OverwritesPreviousTool`, `GetAllToolsAsync_AfterOverwrite_ReturnsLatestVersion`,
`GetAllToolsAsync_WithMultipleToolsRegistered_ReturnsAllTools`,
`GetToolByName_WithWrongName_ReturnsNull`, `RegisterTool_WithAuthorization_IsAuthorizedAsyncIsInvocable`).
