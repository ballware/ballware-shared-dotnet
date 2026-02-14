using System.Collections.Immutable;
using System.Text;
using Ballware.Shared.Data.Persistables;
using Ballware.Shared.Data.Public;
using Ballware.Shared.Data.Repository;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Ballware.Shared.Data.Ef.Repository;

public class BaseRepository<TEditable, TPersistable> : IRepository<TEditable> where TEditable : class, IEditable where TPersistable : class, IEntity, new()
{
    protected IMapper Mapper { get; }
    protected IDbContext Context { get; }
    protected IRepositoryHook<TEditable, TPersistable>? Hook { get; }

    public BaseRepository(IMapper mapper, IDbContext dbContext, IRepositoryHook<TEditable, TPersistable>? hook)
    {
        Mapper = mapper;
        Context = dbContext;
        Hook = hook;
    }

    protected virtual IQueryable<TPersistable> ListQuery(IQueryable<TPersistable> query, string identifier,
        IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        if (queryParams.TryGetValue("id", out var idParam))
        {
            if (idParam is IEnumerable<string> idValues)
            {
                var idList = idValues.Select(Guid.Parse);
                
                query = query.Where(t => idList.Contains(t.Uuid));
            }
            else if (Guid.TryParse(idParam.ToString(), out var id))
            {
                query = query.Where(t => t.Uuid == id);
            }
        }

        return query;
    }

    protected virtual IQueryable<TPersistable> ByIdQuery(IQueryable<TPersistable> query, string identifier,
        IDictionary<string, object> claims, Guid id)
    {
        return query;
    }

    protected virtual Task<TPersistable> ProduceNewAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object>? queryParams)
    {
        return Task.FromResult(new TPersistable()
        {
            Uuid = Guid.NewGuid()
        });
    }

    protected virtual Task<TEditable> ExtendByIdAsync(string identifier, IDictionary<string, object> claims, TEditable value)
    {
        if (Hook != null)
        {
            return Task.FromResult(Hook.ExtendById(identifier, claims, value));
        }
        
        return Task.FromResult(value);
    }

    protected virtual async Task BeforeSaveAsync(Guid? userId, string identifier,
        IDictionary<string, object> claims, TEditable value, bool insert)
    {
        await Task.Run(() => Hook?.BeforeSave(userId, identifier, claims, value, insert));
    }

    protected virtual async Task AfterSaveAsync(Guid? userId, string identifier, IDictionary<string, object> claims,
        TEditable value, TPersistable persistable, bool insert)
    {
        await Task.Run(() => Hook?.AfterSave(userId, identifier, claims, value, persistable, insert));
    }
    
    protected virtual async Task<RemoveResult<TEditable>> RemovePreliminaryCheckAsync(Guid? userId, IDictionary<string, object> claims,
        IDictionary<string, object> removeParams, TEditable? removeValue)
    {
        var hookResult = await Task.Run(() => Hook?.RemovePreliminaryCheck(userId, claims, removeParams, removeValue));
        
        if (hookResult != null)
        {
            return hookResult.Value;
        }
        
        return new RemoveResult<TEditable>()
        {
            Result = true,
            Messages = [],
            Value = removeValue
        };
    }

    protected virtual async Task BeforeRemoveAsync(Guid? userId, IDictionary<string, object> claims,
        TPersistable persistable)
    {
        await Task.Run(() => Hook?.BeforeRemove(userId, claims, persistable));
    }

    public Task<IEnumerable<TEditable>> AllAsync(string identifier, IDictionary<string, object> claims)
    {
        return Task.Run(() => ListQuery(Context.Set<TPersistable>(), identifier, claims, ImmutableDictionary<string, object>.Empty)
            .AsEnumerable()
            .Select(Mapper.Map<TEditable>));
    }

    public Task<IEnumerable<TEditable>> QueryAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        return Task.Run(() => ListQuery(Context.Set<TPersistable>(), identifier, claims, queryParams).AsEnumerable().Select(Mapper.Map<TEditable>));
    }

    public Task<long> CountAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        return Task.Run(() =>
            ListQuery(Context.Set<TPersistable>(), identifier, claims, queryParams)
                .LongCount());
    }

    public async Task<TEditable?> ByIdAsync(string identifier, IDictionary<string, object> claims, Guid id)
    {
        var result = Mapper.Map<TEditable>(await ByIdQuery(Context.Set<TPersistable>().Where(t => t.Uuid == id), identifier,
            claims, id).FirstOrDefaultAsync());

        if (result != null)
        {
            return await ExtendByIdAsync(identifier, claims, result);    
        }

        return result;
    }

    public async Task<TEditable> NewAsync(string identifier, IDictionary<string, object> claims)
    {
        var instance = await ProduceNewAsync(identifier, claims, ImmutableDictionary<string, object>.Empty);

        return Mapper.Map<TEditable>(instance);
    }

    public async Task<TEditable> NewQueryAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        var instance = await ProduceNewAsync(identifier, claims, queryParams);

        return Mapper.Map<TEditable>(instance);
    }

    public virtual async Task SaveAsync(Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value)
    {
        var persistedItem = await Context.Set<TPersistable>()
            .FirstOrDefaultAsync(t => t.Uuid == value.Id);

        var insert = persistedItem == null;

        await BeforeSaveAsync(userId, identifier, claims, value, insert);

        if (persistedItem == null)
        {
            persistedItem = Mapper.Map<TPersistable>(value);

            if (persistedItem is IAuditable auditable)
            {
                auditable.CreatorId = userId;
                auditable.CreateStamp = DateTime.Now;
                auditable.LastChangerId = userId;
                auditable.LastChangeStamp = DateTime.Now;
            }

            Context.Set<TPersistable>().Add(persistedItem);
        }
        else
        {
            Mapper.Map(value, persistedItem);

            if (persistedItem is IAuditable auditable)
            {
                auditable.LastChangerId = userId;
                auditable.LastChangeStamp = DateTime.Now;
            }

            Context.Set<TPersistable>().Update(persistedItem);
        }

        await AfterSaveAsync(userId, identifier, claims, value, persistedItem, insert);

        await Context.SaveChangesAsync();
    }

    public virtual async Task<RemoveResult<TEditable>> RemoveAsync(Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams)
    {
        TPersistable? persistedItem = null;
        TEditable? editableItem = null;

        if (removeParams.TryGetValue("Id", out var idParam) && Guid.TryParse(idParam.ToString(), out Guid id))
        {
            persistedItem = await Context.Set<TPersistable>()
                .FirstOrDefaultAsync(t => t.Uuid == id);
            
            editableItem = persistedItem != null ? Mapper.Map<TEditable>(persistedItem) : null;
        }

        var result = await RemovePreliminaryCheckAsync(userId, claims, removeParams, editableItem);

        if (!result.Result)
        {
            return result;
        }

        if (persistedItem != null)
        {
            await BeforeRemoveAsync(userId, claims, persistedItem);

            Context.Set<TPersistable>().Remove(persistedItem);

            await Context.SaveChangesAsync();
        }

        return new RemoveResult<TEditable>()
        {
            Result = true,
            Messages = [],
            Value = editableItem
        };
    }

    public async Task ImportAsync(Guid? userId, string identifier, IDictionary<string, object> claims, Stream importStream,
        Func<TEditable, Task<bool>> authorized)
    {
        using var textReader = new StreamReader(importStream);

        var items = JsonConvert.DeserializeObject<IEnumerable<TEditable>>(await textReader.ReadToEndAsync());

        if (items == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (await authorized(item))
            {
                await SaveAsync(userId, identifier, claims, item);
            }
        }
    }

    public async Task<ExportResult> ExportAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        var items = await Task.WhenAll((await QueryAsync(identifier, claims, queryParams)).Select(e => ExtendByIdAsync(identifier, claims, e)));

        return new ExportResult()
        {
            FileName = $"{identifier}.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(items)),
            MediaType = "application/json",
        };
    }
}