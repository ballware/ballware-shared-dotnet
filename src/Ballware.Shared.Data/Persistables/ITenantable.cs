namespace Ballware.Shared.Data.Persistables;

public interface ITenantable
{
    Guid TenantId { get; set; }
}