using System.Collections.Immutable;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Http;

namespace Ballware.Shared.Api.Endpoints;

public class EditingEndpointBuilder
{
    private string Application { get; }
    private string Entity { get; }
    private Guid? TenantId { get; }
    private IDictionary<string, object> Claims { get; set; } = ImmutableDictionary<string, object>.Empty;
    private ITenantRightsChecker TenantRightsChecker { get; }
    private IEntityRightsChecker EntityRightsChecker { get; }
    private IAuthorizationMetadataProvider ApiAuthorizationMetadataProvider { get; }
    private string? Right { get; set; }
    private bool CheckTenantRight { get; set; }
    
    private bool CheckEntityRight { get; set; }
    private object? EntityRightParam { get; set; }
    
    private bool CheckBatchEntityRight { get; set; }
    private IEnumerable<object>? BatchEntityRightParams { get; set; }
    
    private EditingEndpointBuilder(IAuthorizationMetadataProvider authorizationMetadataProvider, ITenantRightsChecker tenantRightsChecker, IEntityRightsChecker entityRightsChecker, Guid tenantId, string application, string entity)
    {
        ApiAuthorizationMetadataProvider = authorizationMetadataProvider;
        TenantRightsChecker = tenantRightsChecker;
        EntityRightsChecker = entityRightsChecker;
        TenantId = tenantId;
        Application = application;
        Entity = entity;
    }

    public static EditingEndpointBuilder Create(IAuthorizationMetadataProvider authorizationMetadataProvider, ITenantRightsChecker tenantRightsChecker, IEntityRightsChecker entityRightsChecker, Guid tenantId, string application, string entity)
    {
        return new EditingEndpointBuilder(authorizationMetadataProvider, tenantRightsChecker, entityRightsChecker, tenantId, application, entity);
    }
    
    public EditingEndpointBuilder WithClaims(IDictionary<string, object> claims)
    {
        Claims = claims;
        return this;
    }
    
    public EditingEndpointBuilder WithTenantAndEntityRightCheck(string right, object param)
    {
        Right = right;
        EntityRightParam = param;
        CheckTenantRight = true;
        CheckEntityRight = true;
        
        return this;
    }
    
    public EditingEndpointBuilder WithBatchTenantAndEntityRightCheck(string right, IEnumerable<object> batch)
    {
        Right = right;
        BatchEntityRightParams = batch;
        CheckTenantRight = true;
        CheckBatchEntityRight = true;
        
        return this;
    }
    
    public async Task<IResult> ExecuteAsync(Func<Task<IResult>> executor)
    {
        if (!string.IsNullOrEmpty(Right) && TenantId != null && CheckTenantRight)
        {
            var tenant = await ApiAuthorizationMetadataProvider.MetadataForTenantByIdAsync(TenantId.Value);

            if (tenant == null)
            {
                return Results.NotFound($"Tenant {TenantId} not found");
            }
        
            var authorized = await TenantRightsChecker.HasRightAsync(tenant, Application, Entity, Claims, Right);

            if (CheckEntityRight)
            {
                var entity = await ApiAuthorizationMetadataProvider.MetadataForEntityByTenantAndIdentifierAsync(TenantId.Value, Entity);
                
                if (entity == null)
                {
                    return Results.NotFound($"Entity {Entity} not found for tenant {TenantId}");
                }
                
                authorized = await EntityRightsChecker.HasRightAsync(TenantId.Value, entity, Claims, Right, EntityRightParam, authorized);
            }

            if (CheckBatchEntityRight && BatchEntityRightParams != null)
            {
                var entity = await ApiAuthorizationMetadataProvider.MetadataForEntityByTenantAndIdentifierAsync(TenantId.Value, Entity);
                
                if (entity == null)
                {
                    return Results.NotFound($"Entity {Entity} not found for tenant {TenantId}");
                }
                
                foreach (var batchEntityRightParam in BatchEntityRightParams)
                {
                    authorized = await EntityRightsChecker.HasRightAsync(TenantId.Value, entity, Claims, Right, batchEntityRightParam, authorized);
                    
                    if (!authorized)
                    {
                        break;
                    }
                }
            }
            
            if (!authorized)
            {
                return Results.Unauthorized();
            }
        }
        
        return await executor();
    }
}