namespace Ballware.Shared.Data.Repository;

public struct ExportResult
{
    public string FileName { get; init; }
    public string MediaType { get; init; }
    public byte[] Data { get; init; }
}