namespace Ballware.Shared.Authorization;

public interface ITenantRightsChecker
{
    public Task<bool> HasRightAsync(ITenantAuthorizationMetadata tenant, string application, string entity, IDictionary<string, object> claims, string right);

}