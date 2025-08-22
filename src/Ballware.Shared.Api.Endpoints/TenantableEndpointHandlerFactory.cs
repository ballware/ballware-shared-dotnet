using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Ballware.Shared.Api.Endpoints.Bindings;
using Ballware.Shared.Api.Public;
using Ballware.Shared.Data.Repository;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using MimeTypes;
using Newtonsoft.Json;
using Quartz;

namespace Ballware.Shared.Api.Endpoints;

public static class TenantableEndpointHandlerFactory
{
    private static readonly string DefaultQuery = "primary";

    private static readonly string RightView = "view";
    private static readonly string RightAdd = "add";
    private static readonly string RightEdit = "edit";
    private static readonly string RightDelete = "delete";
    
    public delegate Task<IResult> HandleAllDelegate<TEntity>(UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, string identifier) where TEntity : class;

    public delegate Task<IResult> HandleQueryDelegate<TEntity>(UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, string identifier, QueryValueBag query) where TEntity : class;
    
    public delegate Task<IResult> HandleNewDelegate<TEntity>(UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, string identifier) where TEntity : class;
    
    public delegate Task<IResult> HandleByIdDelegate<TEntity>(UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, string identifier, Guid id) where TEntity : class;
    
    public delegate Task<IResult> HandleSaveDelegate<TEntity>(UserId currentUserId, UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, string identifier, TEntity value) where TEntity : class;
    
    public delegate Task<IResult> HandleSaveBatchDelegate<TEntity>(UserId currentUserId, UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, string identifier, List<TEntity> values) where TEntity : class;
    
    public delegate Task<IResult> HandleRemoveDelegate<TEntity>(UserId currentUserId, UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, ITenantableRepository<TEntity> repository, Guid id) where TEntity : class;

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "DI injection needed")]
    public delegate Task<IResult> HandleImportDelegate(ISchedulerFactory schedulerFactory,
        UserId currentUserId, UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, IJobMetadataProvider jobMetadataProvider,
        IFileStorageProvider storageProvider, string identifier,
        IFormFileCollection files);

    public delegate Task<IResult> HandleExportDelegate<TEntity>(
        UserTenantId tenantId, UserClaims claims, EditingEndpointBuilderFactory endpointFactory, 
        ITenantableRepository<TEntity> repository, string identifier, HttpRequest request) 
        where TEntity : class;
    
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "DI injection needed")]
    public delegate Task<IResult> HandleExportUrlDelegate<TEntity>(
        UserId currentUserId, UserTenantId tenantId, UserClaims claims,
        EditingEndpointBuilderFactory endpointFactory, 
        IExportMetadataProvider exportMetadataProvider, ITenantableRepository<TEntity> repository, IFileStorageProvider storageProvider, 
        string identifier, HttpRequest request)
        where TEntity : class;
    
    public delegate Task<IResult> HandleDownloadExportDelegate(IExportMetadataProvider exportMetadataProvider, 
        IFileStorageProvider storageProvider, Guid tenantId, Guid id);
    
    public static HandleAllDelegate<TEntity> CreateAllHandler<TEntity>(string application, string entity) where TEntity : class 
    {
        return async (tenantId, claims, endpointFactory, repository, identifier) =>
        {
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(RightView, ImmutableDictionary<string, object>.Empty)
                .ExecuteAsync(async () => Results.Ok(await repository.AllAsync(tenantId.Value, identifier, claims.Value)));
        };
    }
    
    public static HandleQueryDelegate<TEntity> CreateQueryHandler<TEntity>(string application, string entity) where TEntity : class 
    {
        return async (tenantId, claims, endpointFactory, repository, identifier, query) =>
        {
            var queryParams = GetQueryParams(query.Query);
            
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(RightView, queryParams)
                .ExecuteAsync(async () => Results.Ok(await repository.QueryAsync(tenantId.Value, identifier, claims.Value, queryParams)));
        };
    }

    public static HandleNewDelegate<TEntity> CreateNewHandler<TEntity>(string application, string entity) where TEntity : class
    {
        return async (tenantId, claims, endpointFactory,
            repository, identifier) =>
        {
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(RightAdd, ImmutableDictionary<string, object>.Empty)
                .ExecuteAsync(async () => Results.Ok(await repository.NewAsync(tenantId.Value, identifier, claims.Value)));
        };
    }

    public static HandleByIdDelegate<TEntity> CreateByIdHandler<TEntity>(string application, string entity) where TEntity : class
    {
        return async (tenantId, claims, endpointFactory,
            repository, identifier, id) =>
        {
            var entry = await repository.ByIdAsync(tenantId.Value, identifier, claims.Value, id);

            if (entry == null)
            {
                return Results.NotFound();
            }

            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(RightView, entry)
                .ExecuteAsync(() => Task.FromResult(Results.Ok(entry)));
        };
    }
    
    public static HandleSaveDelegate<TEntity> CreateSaveHandler<TEntity>(string application, string entity) where TEntity : class
    {
        return async (currentUserId, tenantId, claims, endpointFactory,
            repository, identifier, value) =>
        {
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(
                    identifier == DefaultQuery ? RightEdit : identifier, value)
                .ExecuteAsync(async () =>
                {
                    await repository.SaveAsync(tenantId.Value, currentUserId.Value, identifier, claims.Value, value);

                    return Results.Ok();
                });
        };
    }
    
    public static HandleSaveBatchDelegate<TEntity> CreateSaveBatchHandler<TEntity>(string application, string entity) where TEntity : class
    {
        return async (currentUserId, tenantId, claims, endpointFactory,
            repository, identifier, values) =>
        {
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithBatchTenantAndEntityRightCheck(
                    identifier == DefaultQuery ? RightEdit : identifier, values)
                .ExecuteAsync(async () =>
                {
                    foreach (var value in values)
                    {
                        await repository.SaveAsync(tenantId.Value, currentUserId.Value, identifier, claims.Value, value);    
                    }

                    return Results.Ok();
                });
        };
    }
    
    public static HandleRemoveDelegate<TEntity> CreateRemoveHandler<TEntity>(string application, string entity) where TEntity : class
    {
        return async (currentUserId, tenantId, claims, endpointFactory,
            repository, id) =>
        {
            var entry = await repository.ByIdAsync(tenantId.Value, DefaultQuery, claims.Value, id);

            if (entry == null)
            {
                return Results.NotFound();
            }
            
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(
                    RightDelete, entry)
                .ExecuteAsync(async () =>
                {
                    var removeResult = await repository.RemoveAsync(tenantId.Value, currentUserId.Value, claims.Value, ImmutableDictionary.CreateRange(new []
                    {
                        new KeyValuePair<string, object>("Id", id),
                    }));

                    if (!removeResult.Result)
                    {
                        return Results.BadRequest(new Exception(string.Join("\r\n", removeResult.Messages)));
                    }

                    return Results.Ok();
                });
        };
    }
    
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "DI injection needed")]
    public static HandleImportDelegate CreateImportHandler(string application, string entity)
    {
        return async (schedulerFactory, currentUserId, tenantId, claims, endpointFactory, jobMetadataProvider, storageAdapter, identifier, files) =>
        {
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .ExecuteAsync(async () =>
                {
                    foreach (var file in files)
                    {
                        var temporaryId = Guid.NewGuid();
                        
                        var jobData = new JobDataMap();

                        jobData["tenantId"] = tenantId.Value;
                        jobData["userId"] = currentUserId.Value;
                        jobData["identifier"] = identifier;
                        jobData["claims"] = JsonConvert.SerializeObject(claims.Value);
                        jobData["file"] = temporaryId;

                        await storageAdapter.UploadTemporaryFileBehalfOfUserAsync(tenantId.Value, currentUserId.Value, temporaryId, file.FileName, file.ContentType, file.OpenReadStream());
                        
                        var jobId = await jobMetadataProvider.CreateJobAsync(tenantId.Value, currentUserId.Value, "document", "import", JsonConvert.SerializeObject(jobData));

                        jobData["jobId"] = jobId;

                        await (await schedulerFactory.GetScheduler()).TriggerJob(JobKey.Create("import", entity), jobData);
                    }

                    return Results.Created();
                });
        };        
    }
    
    public static HandleExportDelegate<TEntity> CreateExportHandler<TEntity>(string application, string entity) where TEntity : class
    {
        return async (tenantId, claims, endpointFactory,
            repository, identifier, request) =>
        {
            var query = request.Query;
            
            var queryParams = new Dictionary<string, object>();

            foreach (var queryEntry in query)
            {
                queryParams.Add(queryEntry.Key, queryEntry.Value);
            }
            
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(identifier, queryParams)
                .ExecuteAsync(async () =>
                {
                    var export = await repository.ExportAsync(tenantId.Value, identifier, claims.Value, queryParams);
        
                    return Results.Content(Encoding.UTF8.GetString(export.Data), export.MediaType);
                });
        }; 
    }
    
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "DI injection needed")]
    public static HandleExportUrlDelegate<TEntity> CreateExportUrlHandler<TEntity>(string application, string entity)
        where TEntity : class
    {
        return async (currentUserId, tenantId, claims, endpointFactory, exportMetadataProvider, repository, storageAdapter, identifier, request) =>
        {
            var query = await request.ReadFormAsync();
        
            var queryParams = new Dictionary<string, object>();

            foreach (var queryEntry in query)
            {
                queryParams.Add(queryEntry.Key, queryEntry.Value);
            }
            
            return await endpointFactory.Create(tenantId.Value, application, entity)
                .WithClaims(claims.Value)
                .WithTenantAndEntityRightCheck(identifier, queryParams)
                .ExecuteAsync(async () =>
                {
                    var export = await repository.ExportAsync(tenantId.Value, identifier, claims.Value, queryParams);

                    var exportEntry = new Export();

                    exportEntry.Entity = entity;
                    exportEntry.Query = identifier;
                    exportEntry.MediaType = export.MediaType;
                    exportEntry.ExpirationStamp = DateTime.Now.AddDays(1);

                    exportEntry.Id = await exportMetadataProvider.CreateExportAsync(tenantId.Value, currentUserId.Value, exportEntry);
                    
                    await storageAdapter.UploadTemporaryFileBehalfOfUserAsync(tenantId.Value, currentUserId.Value, exportEntry.Id, $"{exportEntry.Id}{MimeTypeMap.GetExtension(export.MediaType)}", export.MediaType, new MemoryStream(export.Data));
        
                    return Results.Ok(new ExportUrlResult()
                    {
                        TenantId = tenantId.Value,
                        Id = exportEntry.Id 
                    });
                });
        };
    }

    public static HandleDownloadExportDelegate CreateDownloadExportHandler()
    {
        return async (exportMetadataProvider, storageAdapter, tenantId, id) =>
        {
            var export = await exportMetadataProvider.GetExportByIdAsync(tenantId, id);

            if (export == null || export.ExpirationStamp <= DateTime.Now)
            {
                return Results.NotFound("Export not found or expired.");
            }

            var fileContent = await storageAdapter.TemporaryFileByIdAsync(tenantId, export.Id);

            if (fileContent == null)
            {
                return Results.NotFound("File not existing.");
            }

            return Results.File(fileContent, export.MediaType, $"{export.Query}_{DateTime.Now:yyyyMMdd_HHmmss}{MimeTypeMap.GetExtension(export.MediaType)}");
        };
    }
    
    private static Dictionary<string, object> GetQueryParams(IDictionary<string, StringValues> query)
    {
        var queryParams = new Dictionary<string, object>();

        foreach (var queryEntry in query)
        {
            if (queryEntry.Value.Count > 1)
            {
                queryParams.Add(queryEntry.Key, $"|{string.Join('|', queryEntry.Value.ToArray())}|");
            }
            else if (queryEntry.Value.Count == 1)
            {
                queryParams.Add(queryEntry.Key, queryEntry.Value.ToString());
            }
        }

        return queryParams;
    }
}