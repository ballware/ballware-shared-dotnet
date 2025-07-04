using Microsoft.EntityFrameworkCore;

namespace Ballware.Shared.Data.Ef;

public interface IDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}