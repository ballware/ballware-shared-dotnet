# docs/-Ordner für ballware-shared-dotnet

## Ziel

Ein neuer `docs/`-Ordner erklärt die **Funktionsweise** der 12 `Ballware.Shared.*`-Bibliotheken (tatsächlich 13 `src/`-Projekte) detailliert und belegbar. `ballware-docs` soll daraus später paketweise zitieren können, so dass heutige "not verified"-Lücken (MCP-Plumbing, Rights-Kaskade, Session/OIDC) aus der Quelle selbst belegt sind.

## Verbindliche Entscheidungen

| # | Entscheidung | Konsequenz |
|---|---|---|
| D1 | Hybrid-Struktur wie `ballware-meta-service`, aber paketadressierbar | `docs/README.md` + `docs/concepts/` + `docs/packages/` + `docs/usage/` |
| D2 | Belegbasis = **Datei + Typ/Methodenname, keine Zeilennummern** | zitiert wird `src/Ballware.Shared.Mcp/Tool.cs` → `Tool.IsAuthorizedAsync`; Zeilennummern sind verboten (driften) |
| D3 | `docs/usage/` mit realen Wiring-Beispielen | Snippets werden aus bestehenden Konsumenten abgeleitet, nicht erfunden |
| D4 | Sprache **Englisch** | konsistent zu README, meta-service-Docs und `ballware-docs` (`defaultLocale: 'en'`) |
| D5 | **Kein** Provenienz-Stempel, kein `manifest.json` | keine Versions-/Datumsangaben in den Seiten |
| D6 | Drift-Kontrolle manuell | "Refresh check" section in `docs/README.md` |
| D7 | README.md bekommt **nur** einen Link-Abschnitt | kein Content-Move, keine Zahlenkorrektur in README |
| D8 | Vertrag primeiro, Interna als Beleg | interne Typen werden benannt, aber als *implementation detail, not contract* markiert |
| D9 | RFC-Fehler wird **mit behoben** | 3 Quellkommentare `RFC 9470` → `RFC 9728`; Doku nutzt durchgängig 9728 |
| D10 | Plain Markdown, **kein** MDX/Frontmatter | analog `ballware-meta-service/docs`; Portal ergänzt eigene Frontmatter |

## Dateibaum

```
docs/
├── README.md                                   # Index, Lesereihenfolge, Refresh-Checkliste
├── concepts/
│   ├── architecture-and-dependencies.md        # Schichten, Paket-Abhängigkeitsgraph, Konventionen
│   ├── data-model-and-tenancy.md               # IEntity/ITenantable/IAuditable/IEditable, Hooks
│   ├── authorization-and-rights-scripts.md     # 2-Stufen-Kaskade, Right-Grammatik, Jint-Semantik
│   ├── editing-api-and-import-jobs.md          # 11 Routen, Bindings, Job-Lifecycle
│   ├── mcp-tool-registry-and-endpoint.md       # Tool-Modell, statisch/dynamisch, RFC 9728
│   └── web-authentication-and-sessions.md      # UserInfo-Enrichment, Session, Browser-OIDC
├── packages/
│   ├── ballware-shared-data.md
│   ├── ballware-shared-data-ef.md
│   ├── ballware-shared-authorization.md
│   ├── ballware-shared-authorization-jint.md
│   ├── ballware-shared-api.md
│   ├── ballware-shared-api-endpoints.md
│   ├── ballware-shared-api-jobs.md
│   ├── ballware-shared-mcp.md
│   ├── ballware-shared-mcp-endpoints.md
│   ├── ballware-shared-mcp-authorization.md
│   ├── ballware-shared-authentication.md
│   ├── ballware-shared-web-session.md
│   └── ballware-shared-web-oidc.md
└── usage/
    ├── host-composition.md                     # Add*/Map*-Reihenfolge je Schicht
    └── extension-points.md                     # Hooks, eigene Rights-Engine, eigene Provider
```

Namen: `kebab-case`, Ableitung aus dem Paket-ID (`Ballware.Shared.Data.Ef` → `ballware-shared-data-ef.md`). Diese Abbildung ist die Andockstelle für `ballware-docs`.

## Arbeitspakete

### 1. Code-Basis lesen (Pflicht vor jedem Schreibschritt)

Für jede Seite die öffentlichen Verträge **und** die zugehörigen Tests lesen. Keine Aussage aus dem globalen README übernehmen, ohne sie im Code zu bestätigen. Zu prüfende Punkte:

- `Ballware.Shared.Data`: Methodensatz von `IRepository`/`ITenantableRepository`, Default-Interface-Methoden in `IRepositoryHook`/`ITenantableRepositoryHook`.
- `Ballware.Shared.Data.Ef`: `BaseRepository`/`TenantableBaseRepository`, Mapster-Mapping, `IDbContext`-Fassade, Template-Points `ListQuery`/`ByIdQuery`/`ProduceNewAsync`, Audit-Stamping, `ImportAsync`-Autorisierung, `ExportAsync`-JSON.
- `Ballware.Shared.Authorization(.Jint)`: `IPrincipalUtils`, `ITenantRightsChecker`, `IEntityRightsChecker`, `StateAllowedAsync`; Jint-Variablen und **Fehlverhalten ohne Script** (Tenant: allow / Entity: pass-through / State: deny) gegen `test/Ballware.Shared.Authorization.Jint.Tests` verifizieren.
- `Ballware.Shared.Api(.Endpoints/.Jobs)`: `MapTenantableEditingApi`-Signatur inkl. Pflicht-Parameter `apiTag`/`apiOperationPrefix` und Defaults `authorizationScope = "metaApi"`, `apiGroup = "meta"`; Bindings `UserId`/`UserTenantId`/`UserClaims`/`UserRights`/`QueryValueBag`; `EditingEndpointBuilder`-Rechte (`view`/`add`/`edit`/`delete`); `download/{tenantId}/{id}` ist `.AllowAnonymous()`; `ImportJob`/`TenantableImportJob` lesen `JobDataMap`-Schlüssel und melden `JobStates`.
- `Ballware.Shared.Mcp(.Endpoints/.Authorization)`: `Tool`/`ToolParam`/`ToolParamType`/`ToolResult`, `IToolRegistry` (statisch + dynamischer Provider mit `ClaimsPrincipal`), `ToolRegistryInitializer` (Hosted Service, einmalige Konfiguration), Argumentvalidierung und `OutputSchema`-Erzeugung, `McpEndpointOptions` (`Enabled`, `RequiredMcpScope`, `ResourceUri`), Protected-Resource-Middleware und `WWW-Authenticate`-Dekorierung, `McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler`.
- `Ballware.Shared.Authentication`: `UserInfoEnrichmentOptions` (Scopes, Authority/Issuer/UserinfoEndpoint, TTLs, `MaxResponseBytes`, `MaxClaims`), `UserInfoEnrichmentStatus`, Hard-Fail bei Enrichment-Fehler (belegt über `AttachUserInfoEnrichment_FailsAuthenticationOnFailedResult`), Schutz vorhandener JWT-Claims.
- `Ballware.Shared.Web.Session`: `WebSessionOptions` (Cookie `.BallwareSession`, `SessionCacheKeyPrefix`, TTL 60 min, `SuppressDefaultSessionCookie`, `DataProtectionPurpose`), `SessionAuthenticationSchemes.DistributedSession`, `SessionPrincipalProvider` (TicketSerializer + Data Protection), `ChallengeDifferentiationMiddleware.DefaultTransportPathPrefixes` = `/_blazor`, `/DXX`, `/dxDR`.
- `Ballware.Shared.Web.Oidc`: `BrowserAuthorizationOptions` (inkl. `PublicBaseUrl`, `SignOnAllowedHosts`, `SignOnAllowedRedirectPathPrefixes` — Wurzel `/` immer erlaubt), `ConfigurationException`, `RegisterBallwarePublicRedirectUriHandling`, `RegisterBallwareWebSessionTokenHandling`, `MapBallwareSignOnEndpoint` (`GET /signon/{idToken}?redirect=…`).

### 2. `docs/packages/` schreiben (13 Seiten)

Je Seite dasselbe Raster, 60–120 Zeilen:

1. Purpose + Abhängigkeiten (welche `Ballware.Shared.*`- und Dritt-Pakete; keine Dependencies bei `Data`/`Api`/`Authorization`/`Mcp`).
2. Public contract: Tabelle Member → Semantik → Beleg (`src/.../Datei.cs` → Symbol).
3. DI-/Mapping-Extension(s) mit exakten Signaturen (`AddBallwareSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim)`, `AddBallwareMcpTools(configure)`, `AddBallwareMcpEndpoint(options)`, `MapBallwareUserMcpEndpoint(basePath, options)`, `AddBallwareWebSession(options)`, `AddBallwareBrowserAuthorization(...)`).
4. Verhalten/Grenzen (Defaults, Fehlerverhalten, Thread-/Request-Scoping).
5. Implementation details (intern, explizit markiert): `ToolRegistry`, `ToolRegistryInitializer`, `TenantableEndpointHandlerFactory`, `EditingEndpointBuilderFactory`, `SessionPrincipalProvider`, `SessionPrincipalAuthenticationHandler`, `McpOAuthProtectedResourceMiddleware`, `DefaultPrincipalUtils`.
6. Evidence: Test-Projekt, das das Verhalten abdeckt; bei `Data`/`Api`/`Mcp` ohne eigenes Test-Projekt die Tests der Nachbarschicht benennen, die den Vertrag prüfen.

### 3. `docs/concepts/` schreiben (6 Seiten)

Übergreifende Mechanik, einmal erklärt, von den Paket-Seiten per Relativlink referenziert (keine Duplication):

- **architecture-and-dependencies.md**: Schichtenmodell + Mermaid-Abhängigkeitsgraph `Data → Data.Ef`, `Authorization → Authorization.Jint`, `Api → Api.Endpoints → Api.Jobs`, `Mcp → Mcp.Endpoints`/`Mcp.Authorization`, `Web.Session → Web.Oidc`, `Authentication` quer; Konventionen: `Guid tenantId` überall, Dual Identity `Id`/`Uuid` ↔ `IEditable.Id`, Auth als Daten (`identifier` + `claims`) statt Injection.
- **data-model-and-tenancy.md**: Interface-Hierarchie, `RemoveResult<T>`, `ExportResult`, Hook-Sequenz bei Save/Remove.
- **authorization-and-rights-scripts.md**: Right-Grammatik `<application>.<entity>.<right>`, Two-Stage-Pipeline inkl. Override-Regel des Entity-Scripts, `StateAllowedScript` mit `hasRight`/`hasAnyRight`, Script-Metadaten-Provider, Austauschbarkeit der Engine über die Checker-Interfaces.
- **editing-api-and-import-jobs.md**: 11-Routen-Tabelle (Route, Handler-Fabrik, Rechte, Produces-Status), Import-Pfad `IFileStorageProvider` → `IJobMetadataProvider` → Quartz → `JobStates`, Export/ExportUrl/Download inkl. anonymer Download-Route.
- **mcp-tool-registry-and-endpoint.md**: Registry-Lebenszyklus (Startup-Initialisierung, per-Request-Auflösung dynamischer Provider), `tools/list`-Filterung nach `IsAuthorizedAsync`, `tools/call`-Doppelprüfung, Schema-Generierung, **RFC 9728** Protected Resource Metadata + `WWW-Authenticate: Bearer resource_metadata="…"`.
- **web-authentication-and-sessions.md**: Grenzen `Authentication` (Bearer-/UserInfo-Pfad) vs. `Web.Session` (Cookie-/Ticket-Pfad) vs. `Web.Oidc` (Composition); Session-Ticket-Aufbau, 401 statt 302 für Transportpfade, `/signon/{idToken}`-Flow, `PublicBaseUrl`-Regel für `redirect_uri`.

### 4. `docs/usage/` schreiben (2 Seiten)

- **host-composition.md**: bestellte Add-/Use-/Map-Reihenfolge pro Anwendungs-Typ (API-Service, MCP-Server, Blazor-/Browser-App) als lauffähige Snippets. Belegquellen für echte Nutzung: `ballware-meta-service/src/Ballware.Meta.Service/Startup.cs` (`AttachUserInfoEnrichment`, `AddBallwareSharedUserInfoEnrichment`, `AddBallwareMcpTools`), `ballware-generic-service/src/Ballware.Generic.Service/Startup.cs` (`AddBallwareSharedAuthorizationUtils`, `AddBallwareSharedJintRightsChecker`, `AddBallwareMcpEndpoint`, `MapBallwareUserMcpEndpoint("/generic/mcp", …)`), `ballware-llm-service/src/Ballware.Llm.App/Startup.cs` (`AddBallwareBrowserAuthorization`, `MapBallwareSignOnEndpoint`). Die Service-Repos werden **namentlich** zitiert, ihr Code wird nicht kopiert; die Snippets zeigen nur die Shared-API.
- **extension-points.md**: eigene `IRepositoryHook`, eigene Rights-Engine (Checker-Interfaces implementieren), dynamische Tool-Provider, eigene `IFileStorageProvider`/`IJobMetadataProvider`/`IExportMetadataProvider`, Override der Transport-Pfad-Liste.

### 5. `docs/README.md` schreiben

Inhalt: Zweck + Scope-Abgrenzung (Backend-/Library-Sicht, keine Service-Konfiguration), Tabellen-Index aller Seiten mit Topic-Spalte (wie `ballware-meta-service/docs/README.md`), "Quick facts" (net10.0, 13 src-/10 test-Projekte, `InternalsVisibleTo`-Konvention, GitVersion-Branchregeln, CI packt nach `nupkgs/` und push auf GitHub Packages, `PackageReadmeFile`/`GenerateDocumentationFile` sind aktuell **nicht** gesetzt), Lesereihenfolge (concepts → packages → usage), **Refresh check** (bei Änderung einer öffentlichen Signatur/Options-Klasse die betroffene Paket-Seite + zugehörige Concept-Seite nachziehen; keine Zeilennummern nachziehen).

### 6. README.md und Quellkommentare anpassen

- `README.md`: Abschnitt `## Documentation` direkt nach `## Repository Layout` mit Links auf `docs/README.md`, `docs/concepts/`, `docs/packages/`, `docs/usage/`. Keine weiteren README-Änderungen.
- RFC-Fix (nur Kommentare, kein Verhalten): `src/Ballware.Shared.Mcp.Endpoints/Configuration/McpEndpointOptions.cs`, `src/Ballware.Shared.Mcp.Endpoints/Internal/McpOAuthProtectedResourceMiddleware.cs`, `src/Ballware.Shared.Mcp.Endpoints/Internal/OAuthProtectedResourceMetadata.cs` — `RFC 9470` → `RFC 9728` (OAuth 2.0 Protected Resource Metadata).

## Reihenfolge

1. Schritt 1 (Code lesen) → 2. `docs/packages/` → 3. `docs/concepts/` → 4. `docs/usage/` → 5. `docs/README.md` → 6. README-Abschnitt + RFC-Kommentarfix → 7. Validierung.

Begründung: Die Paket-Seiten sind die Primärquellen; die Concept-Seiten referenzieren sie, nicht umgekehrt.

## Risiken / Fallstricke

- **README-Zahl ist falsch**: global README nennt "12 library projects"; `src/` enthält 13 `csproj` und `ballware-shared.sln` listet 13 + 10 Tests + 2 Solution-Folders. `docs/README.md` muss 13 sagen; das globale README bleibt pro D7 unverändert (Konflikt bewusst in Kauf, später separat korrigieren).
- **Keine Zeilennummern** (D2) — auch nicht versehentlich aus meta-service-Doku abschreiben.
- **Keine Cross-Repo-Links**: `../ballware-meta-service/...` funktioniert im Portal nicht; Referenzen auf andere Repos als Klartext (`ballware-generic-service/src/Ballware.Generic.Service/Startup.cs`) schreiben.
- **Keine Geheimwerte**: Options-Klassen nur mit Namen/Defaults; keine Authority-URLs, Clients oder Secrets aus Nachbar-Repos übernehmen.
- **Übernahme-Grenze zum Portal**: `docs/` beschreibt Verträge und Mechanik der Bibliotheken, **nicht** das Deployment der Services — sonst entsteht die Duplikation, die `ballware-docs` als Drift-Risiko führt.
- **Unbestätigte README-Aussagen** (z. B. "missing script defaults to allow/deny", "expiring download URLs") müssen im Code verifiziert werden; ist eine Aussage nicht belegbar, in der Seite explizit als offen kennzeichnen statt als Fakt schreiben.

## Validierung

```bash
dotnet build ballware-shared.sln
dotnet test ballware-shared.sln          # Kommentar-Fix darf nichts ändern; 10 Test-Projekte grün
```

1. Zitat-Integrität: alle in `docs/**` zitierten Pfade existieren
   (`rg -o --no-filename 'src/[A-Za-z0-9./_-]+\.cs' docs | sort -u` und jede Datei gegen `ls` prüfen).
2. Link-Integrität: jede relative Markdown-Link-Ziel-Datei existiert (inspection der `](…)`-Ziele in `docs/`); keine Links nach `docs/` hinaus außer `../README.md` und `../LICENSE`.
3. Vollständigkeit: 13 Paket-Seiten decken exakt die 13 `src/`-Projekte ab; jede öffentliche DI-/Map-Extension (16 `public static … (this …)`-Methoden) taucht in genau einer Paket-Seite und in `usage/host-composition.md` auf.
4. Belegkontrolle: jede Concept-Aussage hat eine Quellenzeile; Jint-Defaults und Anonymous-Download sind gegen die Test-Projekte abgeglichen.
5. Inhaltliche Abnahme durch den Autor: ein fremder Implementierer kann anhand `docs/usage/host-composition.md` einen MCP-Endpunkt und eine Editing-API ohne Rückfrage anlegen.

## Out of scope

- Änderung der globalen README-Inhalte außer dem Link-Abschnitt (D7).
- `PackageReadmeFile`/`GenerateDocumentationFile`/XML-Doku-Generierung in den csproj (späterer, separater Schritt).
- Portal-Arbeit in `ballware-docs` (Aufnahme in eine Docusaurus-Instanz, `_category_.json`, `i18n/de`) — dortiger eigener Auftrag.
- Korrektur der README-Projektzahl, Bereinigung anderer README-Ungenauigkeiten.