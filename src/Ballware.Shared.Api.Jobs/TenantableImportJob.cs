using Ballware.Shared.Api;
using Ballware.Shared.Api.Public;
using Ballware.Shared.Authorization;
using Ballware.Shared.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Quartz;

namespace Ballware.Shared.Api.Jobs;

public class TenantableImportJob<TEntity, TRepository> 
    : IJob where TEntity : class where TRepository : ITenantableRepository<TEntity>
{
    private IServiceProvider ServiceProvider { get; }
    private IJobMetadataProvider JobProvider { get; }
    private IAuthorizationMetadataProvider AuthorizationMetadataProvider { get; }
    private ITenantRightsChecker TenantRightsChecker { get; }
    private IFileStorageProvider StorageProvider { get; }
    
    public TenantableImportJob(IServiceProvider serviceProvider, IJobMetadataProvider jobProvider, IAuthorizationMetadataProvider authorizationMetadataProvider, ITenantRightsChecker tenantRightsChecker, IFileStorageProvider storageProvider)
    {
        ServiceProvider = serviceProvider;
        JobProvider = jobProvider;
        AuthorizationMetadataProvider = authorizationMetadataProvider;
        TenantRightsChecker = tenantRightsChecker;
        StorageProvider = storageProvider;
    }
    
    public async Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.Trigger.JobKey;
        var tenantId = context.MergedJobDataMap.GetGuidValue("tenantId");
        var jobId = context.MergedJobDataMap.GetGuidValue("jobId");
        var userId = context.MergedJobDataMap.GetGuidValue("userId");
        context.MergedJobDataMap.TryGetString("identifier", out var identifier) ;
                         
        var claims = Utils.DropNullMember(Utils.NormalizeJsonMember(JsonConvert.DeserializeObject<Dictionary<string, object?>>(context.MergedJobDataMap.GetString("claims") ?? "{}")
                                                                    ?? new Dictionary<string, object?>()));
        context.MergedJobDataMap.TryGetGuidValue("file", out var temporaryId);
        
        var tenant = await AuthorizationMetadataProvider.MetadataForTenantByIdAsync(tenantId);
        var repository = ServiceProvider.GetRequiredService<TRepository>();
        
        try
        {
            if (identifier == null) 
            {
                throw new ArgumentException($"Identifier undefined");
            }

            if (temporaryId == Guid.Empty)
            {
                throw new ArgumentException($"File undefined");
            }
            
            if (tenant == null)
            {
                throw new ArgumentException($"Tenant {tenantId} unknown");
            }
            
            await JobProvider.UpdateJobAsync(tenantId, userId, jobId, JobStates.InProgress, string.Empty);
            
            var file = await StorageProvider.TemporaryFileByIdAsync(tenantId, temporaryId);

            await repository.ImportAsync(tenantId, userId, identifier, claims, file, async (item) =>
            {
                var tenantAuthorized = await TenantRightsChecker.HasRightAsync(tenant, "meta", jobKey.Group, claims, identifier);

                return tenantAuthorized;
            });

            await StorageProvider.RemoveTemporaryFileByIdBehalfOfUserAsync(tenantId, userId, temporaryId);
            await JobProvider.UpdateJobAsync(tenantId, userId, jobId, JobStates.Finished, string.Empty);
        }
        catch (Exception ex)
        {
            if (tenant != null)
            {
                await JobProvider.UpdateJobAsync(tenantId, userId, jobId, JobStates.Error, JsonConvert.SerializeObject(ex));    
            }
            
            // do you want the job to refire?
            throw new JobExecutionException(msg: ex.Message, refireImmediately: false, cause: ex);
        }
    }
}