using System.Security.Claims;

namespace Ballware.Shared.Mcp;

public class Tool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public IEnumerable<ToolParam> Params { get; set; } = [];
    public string? OutputSchema { get; set; }
    public required Func<IServiceProvider, ClaimsPrincipal, IDictionary<string, object?>, Task<ToolResult>> ExecuteAsync { get; set; }
}