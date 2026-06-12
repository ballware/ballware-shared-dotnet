namespace Ballware.Shared.Mcp;

public class ToolParam
{
    public required ToolParamType Type { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public bool Required { get; set; } = false;
}