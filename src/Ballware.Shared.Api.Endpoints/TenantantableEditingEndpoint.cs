using System.Diagnostics.CodeAnalysis;
using Ballware.Shared.Api.Public;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ballware.Shared.Api.Endpoints;

public static class TenantableEditingEndpoint
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Generic usage, ignore")]
    public static IEndpointRouteBuilder MapTenantableEditingApi<TEntity>(this IEndpointRouteBuilder app, 
        string basePath,
        string application,
        string entity,
        string apiTag,
        string apiOperationPrefix,
        string authorizationScope = "metaApi",
        string apiGroup = "meta" 
        ) where TEntity : class
    {
        app.MapGet(basePath + "/all", TenantableEndpointHandlerFactory.CreateAllHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces<IEnumerable<TEntity>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "All")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query all");
        
        app.MapGet(basePath + "/query", TenantableEndpointHandlerFactory.CreateQueryHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces<IEnumerable<TEntity>>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "Query")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query items by query identifier and params");
        
        app.MapGet(basePath + "/new", TenantableEndpointHandlerFactory.CreateNewHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces<TEntity>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "New")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query new item template");
        
        app.MapGet(basePath + "/byid", TenantableEndpointHandlerFactory.CreateByIdHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces<TEntity>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "ById")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query existing item");
        
        app.MapPost(basePath + "/save", TenantableEndpointHandlerFactory.CreateSaveHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "Save")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Save existing or new item");
        
        app.MapPost(basePath + "/savebatch", TenantableEndpointHandlerFactory.CreateSaveBatchHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "SaveBatch")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Save existing or new items in batch");
        
        app.MapDelete(basePath + "/remove/{id}", TenantableEndpointHandlerFactory.CreateRemoveHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces<TEntity>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "Remove")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Query existing tenant");
        
        app.MapPost(basePath + "/import", TenantableEndpointHandlerFactory.CreateImportHandler(application, entity))
            .RequireAuthorization(authorizationScope)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "Import")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Import from file");
        
        app.MapGet(basePath + "/export", TenantableEndpointHandlerFactory.CreateExportHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .Produces<string>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "Export")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Export by query");
        
        app.MapPost(basePath + "/exporturl", TenantableEndpointHandlerFactory.CreateExportUrlHandler<TEntity>(application, entity))
            .RequireAuthorization(authorizationScope)
            .DisableAntiforgery()
            .Accepts<IFormCollection>("application/x-www-form-urlencoded")
            .Produces<ExportUrlResult>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "ExportUrl")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Export to file by query");
        
        app.MapGet(basePath + "/download/{tenantId}/{id}", TenantableEndpointHandlerFactory.CreateDownloadExportHandler())
            .AllowAnonymous()
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound)
            .WithName(apiOperationPrefix + "Download")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Download exported");
        
        return app;
    }
}