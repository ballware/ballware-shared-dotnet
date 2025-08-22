using Ballware.Shared.Authorization;

namespace Ballware.Shared.Api.Endpoints;

public class EditingEndpointBuilderFactory
{
    private IAuthorizationMetadataProvider AuthorizationMetadataProvider { get; }
    private ITenantRightsChecker TenantRightsChecker { get; }
    private IEntityRightsChecker EntityRightsChecker { get; }
    
    public EditingEndpointBuilderFactory(IAuthorizationMetadataProvider authorizationMetadataProvider,
        ITenantRightsChecker tenantRightsChecker,
        IEntityRightsChecker entityRightsChecker)
    {
        AuthorizationMetadataProvider = authorizationMetadataProvider;
        TenantRightsChecker = tenantRightsChecker;
        EntityRightsChecker = entityRightsChecker;
    }
    
    public EditingEndpointBuilder Create(Guid tenantId, string application, string entity)
    {
        return EditingEndpointBuilder.Create(AuthorizationMetadataProvider, TenantRightsChecker, EntityRightsChecker, tenantId, application, entity);
    }
}