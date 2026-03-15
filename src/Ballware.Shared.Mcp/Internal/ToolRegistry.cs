namespace Ballware.Shared.Mcp.Internal;

internal class ToolRegistry : IToolRegistry
{
    private Dictionary<string, Tool> StaticTools
    {
        get; 
    } = new();
    
    private Dictionary<string, Tool> CachedTools
    {
        get; 
    } = new();
    
    private List<Func<IServiceProvider, Task<IEnumerable<Tool>>>> DynamicToolProviders
    {
        get; 
    } = new();
    
    private async Task FillCacheAsync(IServiceProvider serviceProvider)
    {
        CachedTools.Clear();

        foreach (var tool in StaticTools)
        {
            CachedTools[tool.Key] = tool.Value;
        }
        
        foreach (var provider in DynamicToolProviders)
        {
            var providerTools = await provider(serviceProvider);

            foreach (var tool in providerTools)
            {
                CachedTools[tool.Name] = tool;
            }
        }
    }
    
    public void RegisterStaticTool(Tool tool)
    {
        StaticTools[tool.Name] = tool;
    }
    
    public void RegisterDynamicToolProvider(Func<IServiceProvider, Task<IEnumerable<Tool>>> provider)
    {
        DynamicToolProviders.Add(provider);
    }

    public async Task<IEnumerable<Tool>> GetAllToolsAsync(IServiceProvider serviceProvider)
    {
        await FillCacheAsync(serviceProvider);
        
        return CachedTools.Values;
    }

    public async Task<Tool?> GetToolByNameAsync(IServiceProvider serviceProvider, string name)
    {
        if (CachedTools.Count == 0)
        {
            await FillCacheAsync(serviceProvider);
        }
        
        return CachedTools.GetValueOrDefault(name);
    }
}