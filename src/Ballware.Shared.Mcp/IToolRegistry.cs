namespace Ballware.Shared.Mcp;

public interface IToolRegistry
{
    void RegisterStaticTool(Tool tool);
    void RegisterDynamicToolProvider(Func<IServiceProvider, Task<IEnumerable<Tool>>> toolProvider);
    
    Task<IEnumerable<Tool>> GetAllToolsAsync(IServiceProvider serviceProvider);
    
    Task<Tool?> GetToolByNameAsync(IServiceProvider serviceProvider, string name);
}