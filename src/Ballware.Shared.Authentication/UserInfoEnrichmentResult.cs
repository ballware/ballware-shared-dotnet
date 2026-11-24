using System.Security.Claims;

namespace Ballware.Shared.Authentication;

public enum UserInfoEnrichmentStatus
{
    NotEnriched,
    Enriched,
    Failed
}

public class UserInfoEnrichmentResult
{
    public required UserInfoEnrichmentStatus Status { get; init; }

    public ClaimsPrincipal? Principal { get; init; }

    public static UserInfoEnrichmentResult NotEnriched()
    {
        return new UserInfoEnrichmentResult
        {
            Status = UserInfoEnrichmentStatus.NotEnriched
        };
    }

    public static UserInfoEnrichmentResult Failed()
    {
        return new UserInfoEnrichmentResult
        {
            Status = UserInfoEnrichmentStatus.Failed
        };
    }

    public static UserInfoEnrichmentResult Enriched(ClaimsPrincipal principal)
    {
        return new UserInfoEnrichmentResult
        {
            Status = UserInfoEnrichmentStatus.Enriched,
            Principal = principal
        };
    }
}
