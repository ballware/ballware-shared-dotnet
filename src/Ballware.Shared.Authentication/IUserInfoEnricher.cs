using System.Security.Claims;

namespace Ballware.Shared.Authentication;

public interface IUserInfoEnricher
{
    Task<UserInfoEnrichmentResult> EnrichAsync(
        ClaimsPrincipal principal,
        string accessToken,
        CancellationToken cancellationToken);
}
