namespace Ballware.Shared.Data.Repository;

public interface IRepository<TEditable> where TEditable : class
{
    Task<IEnumerable<TEditable>> AllAsync(string identifier, IDictionary<string, object> claims);
    Task<IEnumerable<TEditable>> QueryAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams);

    Task<long> CountAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams);

    Task<TEditable?> ByIdAsync(string identifier, IDictionary<string, object> claims, Guid id);
    Task<TEditable> NewAsync(string identifier, IDictionary<string, object> claims);
    Task<TEditable> NewQueryAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams);

    Task SaveAsync(Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value);

    Task<RemoveResult<TEditable>> RemoveAsync(Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams);

    Task ImportAsync(Guid? userId,
        string identifier,
        IDictionary<string, object> claims,
        Stream importStream,
        Func<TEditable, Task<bool>> authorized);

    Task<ExportResult> ExportAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams);
}