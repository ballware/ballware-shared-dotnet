using System.ComponentModel.DataAnnotations;

namespace Ballware.Shared.Mcp.Endpoints.Configuration;

public class McpEndpointOptions
{
    public bool Enabled { get; set; } = false;
    
    [Required]
    public required string RequiredMcpScope { get; set; }
    
    /// <summary>
    /// The resource identifier URI for the MCP server.
    /// Used in the OAuth Protected Resource Metadata response (RFC 9470).
    /// </summary>
    [Required]
    public required string ResourceUri { get; set; }
}