namespace Ballware.Shared.Data.Repository;

public struct RemoveResult<TEditable> where TEditable : class
{
    public bool Result { get; init; }
    public IEnumerable<string> Messages { get; init; }
    public TEditable? Value { get; init; }
}