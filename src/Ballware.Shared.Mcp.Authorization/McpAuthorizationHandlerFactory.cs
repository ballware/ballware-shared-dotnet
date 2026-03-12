using System.Security.Claims;
using Ballware.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Mcp.Authorization;

public static class McpAuthorizationHandlerFactory
{
    public static Func<IServiceProvider, ClaimsPrincipal, Task<bool>> CreateStaticEntityRightAuthorizationHandler(string application, string entity, string right) 
    {
        return async (serviceProvider, principal) =>
        {
            var principalUtils = serviceProvider.GetRequiredService<IPrincipalUtils>();
            var tenantId = principalUtils.GetUserTenandId(principal);
            var authorizationMetadataProvider = serviceProvider.GetRequiredService<IAuthorizationMetadataProvider>();
            var tenantRightsChecker = serviceProvider.GetRequiredService<ITenantRightsChecker>();
            var entityRightsChecker = serviceProvider.GetRequiredService<IEntityRightsChecker>();

            var tenantMetadata = await authorizationMetadataProvider.MetadataForTenantByIdAsync(tenantId);
            var entityMetadata =
                await authorizationMetadataProvider.MetadataForEntityByTenantAndIdentifierAsync(tenantId, entity);
            var claims = principalUtils.GetUserClaims(principal);

            if (tenantMetadata == null)
            {
                return false;
            }

            if (entityMetadata == null)
            {
                return false;
            }

            var tenantAllowed = await tenantRightsChecker.HasRightAsync(tenantMetadata, application, entity, claims, right);
        
            return await entityRightsChecker.HasRightAsync(tenantId, entityMetadata, claims, right, null, tenantAllowed);    
        };
    }
}