using System.Security.Claims;

namespace Ballware.Shared.Authorization.Internal;

class DefaultPrincipalUtils : IPrincipalUtils
{
    private string TenantClaim { get; }
    private string UserIdClaim { get; }
    private string RightClaim { get; }

    public DefaultPrincipalUtils(string tenantClaim, string userIdClaim, string rightClaim)
    {
        TenantClaim = tenantClaim;
        UserIdClaim = userIdClaim;
        RightClaim = rightClaim;
    }

    public Guid GetUserId(ClaimsPrincipal principal)
    {
        var userIdClaimValue = principal.Claims.FirstOrDefault(c => c.Type.Equals(UserIdClaim));

        Guid.TryParse(userIdClaimValue?.Value, out var userId);

        return userId;
    }

    public Guid GetUserTenandId(ClaimsPrincipal principal)
    {
        var tenantClaimValue = principal.Claims.FirstOrDefault(c => c.Type.Equals(TenantClaim));

        Guid.TryParse(tenantClaimValue?.Value, out var tenantId);

        return tenantId;
    }

    public Dictionary<string, object> GetUserClaims(ClaimsPrincipal principal)
    {
        var userinfoTemp = new Dictionary<string, List<string>>();

        foreach (var cl in principal.Claims)
        {
            if (userinfoTemp.TryGetValue(cl.Type, out var existing))
            {
                existing.Add(cl.Value);
            }
            else
            {
                userinfoTemp.Add(cl.Type, [cl.Value]);
            }
        }

        var claims = userinfoTemp.Select(cl =>
        {
            if (cl.Value.Count > 1)
            {
                return new KeyValuePair<string, object>(cl.Key, cl.Value.ToArray());
            }
            else
            {
                return new KeyValuePair<string, object>(cl.Key, cl.Value[0]);
            }
        }).ToDictionary(elem => elem.Key, elem => elem.Value);

        return claims;
    }

    public IEnumerable<string> GetUserRights(ClaimsPrincipal principal)
    {
        return principal.Claims?.Where(cl => cl.Type == RightClaim).Select(cl => cl.Value) ?? [];
    }
}