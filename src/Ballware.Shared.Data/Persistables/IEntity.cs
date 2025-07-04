namespace Ballware.Shared.Data.Persistables;

public interface IEntity
{
    long? Id { get; set; }
    Guid Uuid { get; set; }
}

