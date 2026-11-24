using System.ComponentModel.DataAnnotations;

namespace Ballware.Shared.Authentication;

public class UserInfoEnrichmentOptions
{
    public bool Enabled { get; set; }

    public List<string> Scopes { get; set; } = [];

    [Required]
    public string ScopeClaimType { get; set; } = "scope";

    public string? Authority { get; set; }

    public string? Issuer { get; set; }

    public string? UserinfoEndpoint { get; set; }

    [Range(1, int.MaxValue)]
    public int CacheTtlSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int NegativeCacheTtlSeconds { get; set; } = 10;

    [Range(1, int.MaxValue)]
    public int TimeoutSeconds { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int MaxResponseBytes { get; set; } = 262144;

    [Range(1, int.MaxValue)]
    public int MaxClaims { get; set; } = 1000;
}
