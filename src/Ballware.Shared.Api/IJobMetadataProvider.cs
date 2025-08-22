using Ballware.Shared.Api.Public;

namespace Ballware.Shared.Api;

public interface IJobMetadataProvider
{
    Task<Guid> CreateJobAsync(Guid tenantId, Guid userId, string scheduler, string identifier, string? options);
    Task UpdateJobAsync(Guid tenantId, Guid userId,
        Guid id, JobStates state, string? result);
}