# Ballware.Shared.Mcp.Endpoints

## Purpose & dependencies

HTTP transport for the tool registry: bridges `Ballware.Shared.Mcp` to the official
`ModelContextProtocol.AspNetCore` SDK (streamable HTTP MCP server), enforces per-user tool
visibility/authorization, generates JSON schemas from tool parameters, and implements the OAuth 2.0
**protected resource** requirements (RFC 9728) so MCP clients can bootstrap the authorization flow.

Dependencies: project `Ballware.Shared.Mcp`; packages ModelContextProtocol.AspNetCore,
NJsonSchema, Microsoft.AspNetCore.Authentication.JwtBearer
(`src/Ballware.Shared.Mcp.Endpoints/Ballware.Shared.Mcp.Endpoints.csproj`).

Details: [concepts/mcp-tool-registry-and-endpoint.md](../concepts/mcp-tool-registry-and-endpoint.md).

## Public contract

| Member | Semantics | Evidence |
| --- | --- | --- |
| `McpEndpointOptions` | `bool Enabled = false`; `[Required] string RequiredMcpScope` (the `scope` claim value the bearer token must contain); `[Required] string ResourceUri` (resource identifier advertised in protected resource metadata) | `src/Ballware.Shared.Mcp.Endpoints/Configuration/McpEndpointOptions.cs` → `McpEndpointOptions` |
| `ServiceCollectionExtensions.AddBallwareMcpEndpoint` | When `Enabled`: `AddMcpServer` with `ScopeRequests = true`, custom `ListToolsHandler`/`CallToolHandler` wired to the registry, `WithHttpTransport()` | `src/Ballware.Shared.Mcp.Endpoints/ServiceCollectionExtensions.cs` → `AddBallwareMcpEndpoint` |
| `McpEndpoint.MapBallwareUserMcpEndpoint` | When `Enabled`: `app.MapMcp(basePath)` + `RequireAuthorization` pinned to the JwtBearer scheme with an assertion that the space-split `scope` claim contains `RequiredMcpScope` (ordinal comparison) | `src/Ballware.Shared.Mcp.Endpoints/McpEndpoint.cs` → `MapBallwareUserMcpEndpoint` |
| `McpEndpoint.UseBallwareMcpOAuthProtectedResource` | When `Enabled`: inserts the protected-resource middleware (options + external authorization-server URI) | `src/Ballware.Shared.Mcp.Endpoints/McpEndpoint.cs` → `UseBallwareMcpOAuthProtectedResource` |

Behavior inside the MCP handlers (`tools/list`, `tools/call`) — the practical contract for tool
authors — is specified in the concepts page linked above.

## Wiring

```csharp
var mcpOptions = new McpEndpointOptions
{
    Enabled = true,
    RequiredMcpScope = "mcp",        // host-chosen scope value
    ResourceUri = "https://example.org/generic/mcp" // canonical URL of the MCP endpoint
};

services.AddBallwareMcpTools(configure);   // from Ballware.Shared.Mcp
services.AddBallwareMcpEndpoint(mcpOptions);

// pipeline — the protected-resource middleware must run BEFORE authentication:
app.UseBallwareMcpOAuthProtectedResource(mcpOptions, authorizationServerUri);
app.MapBallwareUserMcpEndpoint("/generic/mcp", mcpOptions);
```

`UseBallwareMcpOAuthProtectedResource` documents this ordering explicitly: it must precede
`UseAuthentication`/`UseAuthorization` so the well-known endpoint stays reachable unauthenticated
and 401 responses get decorated (`src/Ballware.Shared.Mcp.Endpoints/McpEndpoint.cs` →
`UseBallwareMcpOAuthProtectedResource` doc comment).

## Behavior & limits

- `Enabled = false` (default) makes all three entry points **no-ops** — the MCP server simply does
  not exist; hosts may therefore bind options from configuration and call them unconditionally.
- `tools/list` includes a tool only if its `IsAuthorizedAsync` gate passes for the current user (no
  user ⇒ gated tools excluded); input schemas are generated from `Tool.Params` (object, no
  additional properties, `Required` → schema `required`), `OutputSchema` is attached when present.
- `tools/call` re-resolves the tool by name (dynamic providers included), matches argument names
  case-insensitively, validates JSON value kinds against declared `ToolParamType` (`Number` →
  `double`), throws for missing required arguments or type mismatches, and **re-checks**
  `IsAuthorizedAsync` before executing.
- Structured results always also carry a text content block (explicit `Text` if given, otherwise the
  serialized structured content) for backward compatibility with MCP clients
  (`src/Ballware.Shared.Mcp.Endpoints/Internal/ToolRegistryRequestHandlers.cs` → `CallToolAsync`).
- Protected resource metadata at `GET /.well-known/oauth-protected-resource` returns
  `resource` (= `ResourceUri`), `authorization_servers` (= the `authorizationServerUri` argument),
  `scopes_supported` (= `RequiredMcpScope`), `bearer_methods_supported` (`["header"]`), with
  `Cache-Control: public, max-age=3600`. Every 401 response passing through the middleware is
  decorated with `WWW-Authenticate: Bearer resource_metadata="{scheme}://{host}/.well-known/oauth-protected-resource"`.
- Request scoping: `ScopeRequests = true` means handlers see the per-request DI scope, so tools
  resolve scoped services (repositories, metadata providers) safely.

## Implementation details

*Implementation detail, not contract:* `ToolRegistryRequestHandlers` (the two MCP handler
functions, argument coercion, and `FormatForLog` debug formatting), `McpOAuthProtectedResourceMiddleware`,
and `OAuthProtectedResourceMetadata` are internal
(`src/Ballware.Shared.Mcp.Endpoints/Internal/`). Errors from tools surface as protocol errors from
the thrown exceptions (`InvalidOperationException`, `UnauthorizedAccessException`) — tools that want
controlled error payloads should return `ToolResult` text instead of throwing.

## Evidence

`test/Ballware.Shared.Mcp.Endpoints.Tests/McpEndpointHandlerTest.cs`
(`ListToolsAsync_*`, `CallToolAsync_*`, schema/param/structured-content cases),
`test/Ballware.Shared.Mcp.Endpoints.Tests/McpAuthorizationTest.cs`
(`UnauthenticatedRequest_ToMcpEndpoint_Returns401`,
`UnauthenticatedRequest_WwwAuthenticateHeader_ContainsResourceMetadataUri`,
`WellKnownOAuthProtectedResource_*` incl. `IsAccessibleWithoutAuthentication` and
`ReturnsCorrectCacheHeaders`),
`test/Ballware.Shared.Mcp.Endpoints.Tests/ToolRegistryTest.cs` (registry behavior seen through the
transport).
