namespace Ballware.Shared.Data.Repository;

public interface IRepositoryHook<TEditable, TPersistable> where TEditable : class
    where TPersistable : class
{
    TEditable ExtendById(string identifier, IDictionary<string, object> claims, TEditable value)
    {
        return value;
    }
    
    void BeforeSave(Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value,
        bool insert) {}

    void AfterSave(Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value,
        TPersistable persistable, bool insert) {}

    RemoveResult<TEditable> RemovePreliminaryCheck(Guid? userId, IDictionary<string, object> claims,
        IDictionary<string, object> removeParams, TEditable? removeValue)
    {
        return new RemoveResult<TEditable>()
        {
            Result = true,
            Value = removeValue
        };
    }

    void BeforeRemove(Guid? userId, IDictionary<string, object> claims,
        TPersistable persistable) {}
}