namespace Ballware.Shared.Api;

public interface IFileStorageProvider
{
    Task<Stream> TemporaryFileByIdAsync(Guid tenantId, Guid temporaryId);
    Task UploadTemporaryFileBehalfOfUserAsync(Guid tenantId, Guid userId, Guid temporaryId, string fileName, string contentType, Stream data);
    Task RemoveTemporaryFileByIdBehalfOfUserAsync(Guid tenantId, Guid userId, Guid temporaryId);
}