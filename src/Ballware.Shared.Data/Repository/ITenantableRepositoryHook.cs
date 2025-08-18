namespace Ballware.Shared.Data.Repository;

public interface ITenantableRepositoryHook<TEditable, TPersistable> where TEditable : class
    where TPersistable : class
{
    TEditable ExtendById(Guid tenantId, string identifier, IDictionary<string, object> claims, TEditable value)
    {
        return value;
    }
    
    void BeforeSave(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value,
        bool insert) {}

    void AfterSave(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value,
        TPersistable persistable, bool insert) {}

    RemoveResult<TEditable> RemovePreliminaryCheck(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        IDictionary<string, object> removeParams, TEditable? removeValue)
    {
        return new RemoveResult<TEditable>()
        {
            Result = true,
            Value = removeValue
        };
    }

    void BeforeRemove(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        TPersistable persistable) {}
}