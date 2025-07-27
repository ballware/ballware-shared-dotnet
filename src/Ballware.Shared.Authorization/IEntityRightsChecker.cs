using Ballware.Shared.Authorization;

namespace Ballware.Shared.Authorization;

public interface IEntityRightsChecker
{
    public Task<bool> HasRightAsync(Guid tenantId, IEntityAuthorizationMetadata authorizationMetadata, IDictionary<string, object> claims, string right, object? param, bool tenantResult);

    public Task<bool> StateAllowedAsync(Guid tenantId, IEntityAuthorizationMetadata authorizationMetadata, Guid id, int currentState,
        IEnumerable<string> rights);

}