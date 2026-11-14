using System.ComponentModel.DataAnnotations;

namespace Ballware.Shared.Web.Oidc.Configuration;

public class BrowserAuthorizationOptions
{
    [Required]
    public required string Authority { get; set; }

    [Required]
    public required string Audience { get; set; }

    [Required]
    public required string ClientId { get; set; }

    public string? Issuer { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    // Scope-Wert im "scope"-Claim, den die Benutzer-Policy verlangt, z. B. "mlApi" oder "documentApi".
    [Required(AllowEmptyStrings = false)]
    public required string RequiredUserScope { get; set; }

    // Name der Benutzer-Policy; fällt auf RequiredUserScope zurück, wenn nicht gesetzt.
    public string? UserScopePolicyName { get; set; }

    // Name der Service-Policy (Scope-Wert identisch zum Policy-Namen, z. B. "serviceApi");
    // wird nur registriert, wenn gesetzt.
    public string? ServiceScopePolicyName { get; set; }

    // Öffentliche Hosts, von denen /signon-{Redirects} akzeptiert werden (Reverse-Proxy-Hosts).
    public string[] SignOnAllowedHosts { get; set; } = [];

    // Pfadpräfixe, auf die /signon weiterleiten darf (Segment-Präfixe, z. B. "/chat");
    // die exakte Wurzel "/" ist immer erlaubt.
    public string[] SignOnAllowedRedirectPathPrefixes { get; set; } = [];

    // Öffentliche Basis-URL, unter der die App vom Browser erreicht wird, z. B.
    // https://llm.ballware.de bzw. http://localhost:3003. Der redirect_uri der
    // OIDC-Challenge wird deterministisch daraus abgeleitet (Basis-URL + CallbackPath) und muss
    // in genau dieser Form als gültige Redirect-URI für ClientId beim IdP registriert sein.
    // Wird nichts konfiguriert, bleibt es bei der Herleitung aus Scheme/Host des eingehenden
    // Requests (Anzeige der verwendeten URL im Log bzw. bei Challenge-Fehlern).
    public string? PublicBaseUrl { get; set; }
}
