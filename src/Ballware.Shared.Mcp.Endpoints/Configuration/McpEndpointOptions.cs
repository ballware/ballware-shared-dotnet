using System.ComponentModel.DataAnnotations;

namespace Ballware.Shared.Mcp.Endpoints.Configuration;

public class McpEndpointOptions
{
    public bool Enabled { get; set; } = false;
    
    [Required]
    public required string RequiredMcpScope { get; set; }
}