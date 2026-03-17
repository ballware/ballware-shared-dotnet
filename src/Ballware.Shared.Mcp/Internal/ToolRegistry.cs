using System.Security.Claims;

namespace Ballware.Shared.Mcp.Internal;

internal class ToolRegistry : IToolRegistry
{
    private Dictionary<string, Tool> StaticTools
    {
        get; 
    } = new();
    
    private List<Func<IServiceProvider, ClaimsPrincipal?, Task<IEnumerable<Tool>>>> DynamicToolProviders
    {
        get; 
    } = new();
    
    private async Task<Dictionary<string, Tool>> BuildToolListAsync(IServiceProvider serviceProvider, ClaimsPrincipal? user)
    {
        var result = new Dictionary<string, Tool>();

        foreach (var tool in StaticTools)
        {
            result[tool.Key] = tool.Value;
        }
        
        foreach (var provider in DynamicToolProviders)
        {
            var providerTools = await provider(serviceProvider, user);

            foreach (var tool in providerTools)
            {
                result[tool.Name] = tool;
            }
        }

        return result;
    }
    
    public void RegisterStaticTool(Tool tool)
    {
        StaticTools[tool.Name] = tool;
    }
    
    public void RegisterDynamicToolProvider(Func<IServiceProvider, ClaimsPrincipal?, Task<IEnumerable<Tool>>> provider)
    {
        DynamicToolProviders.Add(provider);
    }

    public async Task<IEnumerable<Tool>> GetAllToolsAsync(IServiceProvider serviceProvider, ClaimsPrincipal? user)
    {
        return (await BuildToolListAsync(serviceProvider, user)).Values;
    }

    public async Task<Tool?> GetToolByNameAsync(IServiceProvider serviceProvider, ClaimsPrincipal? user, string name)
    {
        var tools = await BuildToolListAsync(serviceProvider, user);
        
        return tools.GetValueOrDefault(name);
    }
}