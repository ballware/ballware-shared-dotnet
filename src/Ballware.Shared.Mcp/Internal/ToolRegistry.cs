namespace Ballware.Shared.Mcp.Internal;

internal class ToolRegistry : IToolRegistry
{
    private Dictionary<string, Tool> Tools
    {
        get; 
    } = new();
    
    public void RegisterTool(Tool tool)
    {
        Tools[tool.Name] = tool;
    }

    public Task<IEnumerable<Tool>> GetAllToolsAsync()
    {
        var tools = Tools.Values.ToList();
        
        return Task.FromResult(tools.AsEnumerable());
    }

    public Tool? GetToolByName(string name)
    {
        return Tools.GetValueOrDefault(name);
    }
}