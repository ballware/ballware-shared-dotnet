using Ballware.Shared.Api.Public;

namespace Ballware.Shared.Api;

public interface IExportMetadataProvider
{
    Task<Guid> CreateExportAsync(Guid tenantId, Guid userId, Export payload);
    Task<Export?> GetExportByIdAsync(Guid tenantId, Guid exportId);
}