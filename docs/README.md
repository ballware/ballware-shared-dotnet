# ballware Shared (.NET) — documentation

This folder documents **how the 13 `Ballware.Shared.*` libraries work**: public contracts, wiring
(`Add*`/`Use*`/`Map*` extensions), defaults, failure behavior, and the seams hosts are expected to
implement. Citations name the source file and the type/member — never line numbers, which drift.

> Scope note: this is the **library view**. Service deployment, host configuration values, and
> service-specific behavior (meta/generic/llm storage adapters, UI integration) are documented in the
> service repositories and are deliberately not duplicated here.

## Contents

| Page | Topic |
| --- | --- |
| [concepts/architecture-and-dependencies.md](concepts/architecture-and-dependencies.md) | Layers, verified `ProjectReference` graph, cross-cutting conventions |
| [concepts/data-model-and-tenancy.md](concepts/data-model-and-tenancy.md) | `IEntity`/`ITenantable`/`IAuditable`/`IEditable`, repository method set, result records, hook sequence |
| [concepts/authorization-and-rights-scripts.md](concepts/authorization-and-rights-scripts.md) | Right grammar, two-stage tenant→entity cascade, Jint script semantics, engine swap |
| [concepts/editing-api-and-import-jobs.md](concepts/editing-api-and-import-jobs.md) | The 11 editing routes, rights per route, import pipeline, export/exporturl/download |
| [concepts/mcp-tool-registry-and-endpoint.md](concepts/mcp-tool-registry-and-endpoint.md) | Registry lifecycle, `tools/list`/`tools/call` semantics, schema generation, RFC 9728 |
| [concepts/web-authentication-and-sessions.md](concepts/web-authentication-and-sessions.md) | Bearer vs cookie vs composition boundaries, session ticket, 401-vs-302, `/signon`, `PublicBaseUrl` |
| [packages/ballware-shared-data.md](packages/ballware-shared-data.md) | Persistence contracts (dependency-free) |
| [packages/ballware-shared-data-ef.md](packages/ballware-shared-data-ef.md) | EF base repositories, Mapster mapping, template points, audit stamping, tenancy enforcement |
| [packages/ballware-shared-authorization.md](packages/ballware-shared-authorization.md) | `IPrincipalUtils`, checker interfaces, metadata provider seam |
| [packages/ballware-shared-authorization-jint.md](packages/ballware-shared-authorization-jint.md) | Jint checkers, script variable surface, no-script defaults |
| [packages/ballware-shared-api.md](packages/ballware-shared-api.md) | File storage / job / export provider contracts, `JobStates`, export DTOs |
| [packages/ballware-shared-api-endpoints.md](packages/ballware-shared-api-endpoints.md) | `MapTenantableEditingApi`, bindings, handler factory, anonymous download |
| [packages/ballware-shared-api-jobs.md](packages/ballware-shared-api-jobs.md) | Quartz import jobs, `JobDataMap` keys, per-item authorization, state reporting |
| [packages/ballware-shared-mcp.md](packages/ballware-shared-mcp.md) | `Tool` model, `IToolRegistry`, static vs dynamic tools, hosted initializer |
| [packages/ballware-shared-mcp-endpoints.md](packages/ballware-shared-mcp-endpoints.md) | MCP HTTP transport, options, protected-resource middleware (RFC 9728) |
| [packages/ballware-shared-mcp-authorization.md](packages/ballware-shared-mcp-authorization.md) | Rights-cascade gate factory for tools |
| [packages/ballware-shared-authentication.md](packages/ballware-shared-authentication.md) | Userinfo enrichment options, caching/limits, hard-fail semantics |
| [packages/ballware-shared-web-session.md](packages/ballware-shared-web-session.md) | Distributed sessions, ticket storage, challenge differentiation |
| [packages/ballware-shared-web-oidc.md](packages/ballware-shared-web-oidc.md) | Browser auth composition, policies, `PublicBaseUrl`, `/signon`, redirect allow-list |
| [usage/host-composition.md](usage/host-composition.md) | Ordered wiring per application type (API service, MCP server, browser app) |
| [usage/extension-points.md](usage/extension-points.md) | Host-implemented seams: hooks, engines, providers, dynamic tools |

Package pages are the primary sources; concept pages explain cross-cutting mechanics once and link
to the package pages.

## Reading order

1. [concepts/architecture-and-dependencies.md](concepts/architecture-and-dependencies.md) — what the
   packages are and how they connect.
2. The package pages of the layer you consume (each page is self-contained: purpose → contract →
   wiring → behavior → tests).
3. The concept pages for the mechanisms that span packages (rights cascade, editing/import flow,
   MCP lifecycle, web auth).
4. [usage/host-composition.md](usage/host-composition.md) when assembling a host, and
   [usage/extension-points.md](usage/extension-points.md) when plugging in host-side code.

## Quick facts

- 13 library projects in `src/` (the repository root [README.md](../README.md) still says "12" —
  stale, to be corrected separately), 10 NUnit test projects in `test/`, all projects target
  `net10.0`.
- Implementation details are `internal` and exposed to the sibling test project via
  `InternalsVisibleTo` (`$(MSBuildProjectName).Tests` convention in the csproj files); `Data` and
  `Mcp.Authorization` have no internals to expose.
- Versions follow GitVersion continuous deployment (`GitVersion.yml`): `main` → patch,
  `develop`/`feature/*` → minor, `bugfix/*` → patch; `+semver: major|minor|patch` commit markers
  override.
- CI (`.github/workflows/publish-packages.yml`): on pushes to `main`/`develop` — `dotnet pack` into
  `nupkgs/`, push to GitHub Packages (`nuget.pkg.github.com/ballware`), `main` gets a `v{semver}` tag.
- `PackageReadmeFile` and `GenerateDocumentationFile` are currently **not** set in any csproj —
  packages ship without packaged README or XML docs (planned separate step).

## Refresh check

When a change touches the code, update the docs in the same change — by **file + symbol**, never by
line numbers:

- Changed or added a public signature (interface member, options class member, DI/Map extension)?
  Update the affected package page **and** every concept page that cites the same symbol (search the
  `src/...` paths in `docs/`).
- Changed defaults or failure behavior (options defaults, no-script defaults, status codes)? Update
  the package page's behavior section and re-verify against the named tests.
- Added/removed a project dependency? Update the package page's dependencies list and
  [concepts/architecture-and-dependencies.md](concepts/architecture-and-dependencies.md).
- Added a host-facing seam? Add a row to [usage/extension-points.md](usage/extension-points.md);
  new wiring order belongs in [usage/host-composition.md](usage/host-composition.md).
- Renamed a file or symbol? Update every citation referencing it (citations name file paths and
  member names only).
