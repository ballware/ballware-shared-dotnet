# MCP tool registry & endpoint

One explanation of how tools are defined, registered, filtered, executed, and how the HTTP endpoint
bootstraps OAuth. Packages: [ballware-shared-mcp.md](../packages/ballware-shared-mcp.md),
[ballware-shared-mcp-endpoints.md](../packages/ballware-shared-mcp-endpoints.md),
[ballware-shared-mcp-authorization.md](../packages/ballware-shared-mcp-authorization.md).

## Registry lifecycle

```
startup:  AddBallwareMcpTools(configure)
            → singleton ToolRegistry
            → hosted ToolRegistryInitializer: one DI scope, configure(scope, registry) runs ONCE
per-request: ListTools/CallTool handlers
            → GetAllToolsAsync(requestScopedProvider, user)
              = static tools  +  Σ dynamic providers(provider, user)
```

(`src/Ballware.Shared.Mcp/ServiceCollectionExtensions.cs` → `AddBallwareMcpTools`;
`src/Ballware.Shared.Mcp/Internal/ToolRegistryInitializer.cs` → `StartAsync`;
`src/Ballware.Shared.Mcp/Internal/ToolRegistry.cs` → `BuildToolListAsync`)

- Static tools are keyed by name — later registration overwrites earlier
  (`ToolRegistry.RegisterStaticTool`).
- Dynamic providers run **per request** with the request's scoped `IServiceProvider` and
  `ClaimsPrincipal?`; they can vary the tool set per user (e.g. tenant-specific entities).
- `Tool.IsAuthorizedAsync` is the per-tool authorization gate (`src/Ballware.Shared.Mcp/Tool.cs`);
  the standard implementation is the rights-cascade factory
  `McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(application, entity, right)`
  (`src/Ballware.Shared.Mcp.Authorization/McpAuthorizationHandlerFactory.cs`), which denies when
  tenant or entity metadata is missing.

## `tools/list` filtering

Each registry tool is included only if `IsAuthorizedAsync` is absent or passes for the current user;
no user ⇒ gated tools excluded (`src/Ballware.Shared.Mcp.Endpoints/Internal/ToolRegistryRequestHandlers.cs`
→ `ListToolsAsync`; verified by `ListToolsAsync_MixedAuthorization_ReturnsOnlyAuthorizedTools`,
`ListToolsAsync_WithAuthorizedTool_AndNoUser_ExcludesTool`).

Input schema generation (NJsonSchema): object with `AllowAdditionalProperties = false`, each
`ToolParam` mapped `String→string`, `Number→number`, `Boolean→boolean` with its description;
`Required` params land in `required` (`ToolRegistryRequestHandlers.CreateInputSchemaFromParams`).
`Tool.OutputSchema` (raw JSON string) is parsed and attached as the protocol `outputSchema`.

## `tools/call` double-check

1. Tool resolved by name from the full effective set (static + dynamic per current user).
2. Arguments matched to declared params **case-insensitively**; JSON value kinds must match the
   declared `ToolParamType` (`Number` coerced to `double`); missing required argument or type
   mismatch ⇒ `InvalidOperationException`.
3. `IsAuthorizedAsync` is evaluated **again** (a tool invisible in `tools/list` still cannot be
   called by name); no/unauthorized user ⇒ `UnauthorizedAccessException`
   (`ToolRegistryRequestHandlers.CallToolAsync`; verified by
   `CallToolAsync_WithUnauthorizedUser_ThrowsUnauthorizedAccessException`,
   `CallToolAsync_WithMissingRequiredArgument_ThrowsInvalidOperationException`).
4. Result mapping: `StructuredContent` is returned as protocol structured content **and** mirrored
   into a text content block (explicit `ToolResult.Text` if present, else serialized JSON) for
   backward compatibility; text-only results produce a single text block
   (`CallToolAsync`; verified by `CallToolAsync_WithStructuredContent_ReturnsStructuredContentAndTextFallback`).

## Endpoint wiring & OAuth (RFC 9728)

`AddBallwareMcpEndpoint` registers the SDK MCP server with `ScopeRequests = true` and the two
registry handlers over HTTP transport; `MapBallwareUserMcpEndpoint` maps the MCP path behind a
JwtBearer-pinned policy requiring the space-split `scope` claim to contain `RequiredMcpScope`
(`src/Ballware.Shared.Mcp.Endpoints/ServiceCollectionExtensions.cs`,
`src/Ballware.Shared.Mcp.Endpoints/McpEndpoint.cs`).

`UseBallwareMcpOAuthProtectedResource(options, authorizationServerUri)` installs
`McpOAuthProtectedResourceMiddleware` **before** `UseAuthentication`/`UseAuthorization` (ordering
required so the metadata endpoint stays anonymous and 401s get decorated). The middleware:

- serves `GET /.well-known/oauth-protected-resource` → JSON with `resource` = `ResourceUri`,
  `authorization_servers` = `[authorizationServerUri]`, `scopes_supported` = `[RequiredMcpScope]`,
  `bearer_methods_supported` = `["header"]`, `Cache-Control: public, max-age=3600`
  (`src/Ballware.Shared.Mcp.Endpoints/Internal/OAuthProtectedResourceMetadata.cs`,
  `src/Ballware.Shared.Mcp.Endpoints/Internal/McpOAuthProtectedResourceMiddleware.cs`),
- on any 401 adds `WWW-Authenticate: Bearer resource_metadata="{scheme}://{host}/.well-known/oauth-protected-resource"`
  so MCP clients can discover the IdP and start the OAuth flow.

This is OAuth 2.0 **Protected Resource Metadata, RFC 9728**. Endpoint behavior verified by
`test/Ballware.Shared.Mcp.Endpoints.Tests/McpAuthorizationTest.cs`
(`WellKnownOAuthProtectedResource_ReturnsOk`, `..._IsAccessibleWithoutAuthentication`,
`UnauthenticatedRequest_ToMcpEndpoint_ReturnsWwwAuthenticateHeader`,
`UnauthenticatedRequest_WwwAuthenticateHeader_ResourceMetadataPointsToWellKnownEndpoint`).
