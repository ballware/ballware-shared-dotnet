using System.Collections.Immutable;
using System.Text;
using AutoMapper;
using Ballware.Shared.Data.Persistables;
using Ballware.Shared.Data.Public;
using Ballware.Shared.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Ballware.Shared.Data.Ef.Repository;

public class TenantableBaseRepository<TEditable, TPersistable> : ITenantableRepository<TEditable> where TEditable : class, IEditable where TPersistable : class, IEntity, ITenantable, new()
{
    protected IMapper Mapper { get; }
    protected IDbContext Context { get; }
    protected ITenantableRepositoryHook<TEditable, TPersistable>? Hook { get; }

    public TenantableBaseRepository(IMapper mapper, IDbContext dbContext, ITenantableRepositoryHook<TEditable, TPersistable>? hook)
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
        IDictionary<string, object> claims, Guid tenantId, Guid id)
    {
        return query;
    }

    protected virtual Task<TPersistable> ProduceNewAsync(string identifier, IDictionary<string, object> claims, IDictionary<string, object>? queryParams, Guid tenantId)
    {
        return Task.FromResult(new TPersistable()
        {
            TenantId = tenantId,
            Uuid = Guid.NewGuid()
        });
    }

    protected virtual Task<TEditable> ExtendByIdAsync(string identifier, IDictionary<string, object> claims, Guid tenantId, TEditable value)
    {
        return Task.FromResult(value);
    }

    protected virtual async Task BeforeSaveAsync(Guid tenantId, Guid? userId, string identifier,
        IDictionary<string, object> claims, TEditable value, bool insert)
    {
        await Task.Run(() => Hook?.BeforeSave(tenantId, userId, identifier, claims, value, insert));
    }

    protected virtual async Task AfterSaveAsync(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims,
        TEditable value, TPersistable persistable, bool insert)
    {
        await Task.Run(() => Hook?.AfterSave(tenantId, userId, identifier, claims, value, persistable, insert));
    }
    
    protected virtual async Task<RemoveResult<TEditable>> RemovePreliminaryCheckAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        IDictionary<string, object> removeParams, TEditable? removeValue)
    {
        var hookResult = await Task.Run(() => Hook?.RemovePreliminaryCheck(tenantId, userId, claims, removeParams, removeValue));
        
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

    protected virtual async Task BeforeRemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims,
        TPersistable persistable)
    {
        await Task.Run(() => Hook?.BeforeRemove(tenantId, userId, claims, persistable));
    }

    public Task<IEnumerable<TEditable>> AllAsync(Guid tenantId, string identifier, IDictionary<string, object> claims)
    {
        return Task.Run(() => ListQuery(Context.Set<TPersistable>().Where(t => t.TenantId == tenantId), identifier, claims, ImmutableDictionary<string, object>.Empty)
            .AsEnumerable()
            .Select(Mapper.Map<TEditable>));
    }

    public Task<IEnumerable<TEditable>> QueryAsync(Guid tenantId, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        return Task.Run(() => ListQuery(Context.Set<TPersistable>().Where(t => t.TenantId == tenantId), identifier, claims, queryParams).AsEnumerable().Select(Mapper.Map<TEditable>));
    }

    public Task<long> CountAsync(Guid tenantId, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        return Task.Run(() =>
            ListQuery(Context.Set<TPersistable>().Where(t => t.TenantId == tenantId), identifier, claims, queryParams)
                .LongCount());
    }

    public async Task<TEditable?> ByIdAsync(Guid tenantId, string identifier, IDictionary<string, object> claims, Guid id)
    {
        var result = Mapper.Map<TEditable>(await ByIdQuery(Context.Set<TPersistable>().Where(t => t.TenantId == tenantId && t.Uuid == id), identifier,
            claims, tenantId, id).FirstOrDefaultAsync());

        if (result != null)
        {
            return await ExtendByIdAsync(identifier, claims, tenantId, result);    
        }

        return result;
    }

    public async Task<TEditable> NewAsync(Guid tenantId, string identifier, IDictionary<string, object> claims)
    {
        var instance = await ProduceNewAsync(identifier, claims, ImmutableDictionary<string, object>.Empty, tenantId);

        instance.TenantId = tenantId;

        return Mapper.Map<TEditable>(instance);
    }

    public async Task<TEditable> NewQueryAsync(Guid tenantId, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        var instance = await ProduceNewAsync(identifier, claims, queryParams, tenantId);

        instance.TenantId = tenantId;

        return Mapper.Map<TEditable>(instance);
    }

    public virtual async Task SaveAsync(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TEditable value)
    {
        var persistedItem = await Context.Set<TPersistable>()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Uuid == value.Id);

        var insert = persistedItem == null;

        await BeforeSaveAsync(tenantId, userId, identifier, claims, value, insert);

        if (persistedItem == null)
        {
            persistedItem = Mapper.Map<TPersistable>(value);
            persistedItem.TenantId = tenantId;

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

        await AfterSaveAsync(tenantId, userId, identifier, claims, value, persistedItem, insert);

        await Context.SaveChangesAsync();
    }

    public virtual async Task<RemoveResult<TEditable>> RemoveAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams)
    {
        TPersistable? persistedItem = null;
        TEditable? editableItem = null;

        if (removeParams.TryGetValue("Id", out var idParam) && Guid.TryParse(idParam.ToString(), out Guid id))
        {
            persistedItem = await Context.Set<TPersistable>()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Uuid == id);
            
            editableItem = persistedItem != null ? Mapper.Map<TEditable>(persistedItem) : null;
        }

        var result = await RemovePreliminaryCheckAsync(tenantId, userId, claims, removeParams, editableItem);

        if (!result.Result)
        {
            return result;
        }

        if (persistedItem != null)
        {
            await BeforeRemoveAsync(tenantId, userId, claims, persistedItem);

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

    public async Task ImportAsync(Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, Stream importStream,
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
                await SaveAsync(tenantId, userId, identifier, claims, item);
            }
        }
    }

    public async Task<ExportResult> ExportAsync(Guid tenantId, string identifier, IDictionary<string, object> claims, IDictionary<string, object> queryParams)
    {
        var items = await Task.WhenAll((await QueryAsync(tenantId, identifier, claims, queryParams)).Select(e => ExtendByIdAsync(identifier, claims, tenantId, e)));

        return new ExportResult()
        {
            FileName = $"{identifier}.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(items)),
            MediaType = "application/json",
        };
    }
}