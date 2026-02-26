namespace Ballware.Shared.Mcp;

public interface IToolRegistry
{
    void RegisterTool(Tool tool);
    
    Task<IEnumerable<Tool>> GetAllToolsAsync();
    
    Tool? GetToolByName(string name);
}