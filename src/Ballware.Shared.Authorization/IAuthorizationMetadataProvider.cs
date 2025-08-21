namespace Ballware.Shared.Authorization;

public interface IAuthorizationMetadataProvider
{
    Task<ITenantAuthorizationMetadata?> MetadataForTenantByIdAsync(Guid tenantId);
    Task<IEntityAuthorizationMetadata?> MetadataForEntityByTenantAndIdentifierAsync(Guid tenantId, string entity);
}