using System.Security.Claims;

namespace Ballware.Shared.Mcp;

public interface IToolRegistry
{
    void RegisterStaticTool(Tool tool);
    void RegisterDynamicToolProvider(Func<IServiceProvider, ClaimsPrincipal?, Task<IEnumerable<Tool>>> toolProvider);
    
    Task<IEnumerable<Tool>> GetAllToolsAsync(IServiceProvider serviceProvider, ClaimsPrincipal? user = null);
    
    Task<Tool?> GetToolByNameAsync(IServiceProvider serviceProvider, ClaimsPrincipal? user, string name);
}