using System.Text.Json;

namespace Ballware.Shared.Mcp;

public class ToolResult
{
    public string? Text { get; set; }
    
    public JsonElement? StructuredContent { get; set; }

    public static ToolResult FromText(string text) => new() { Text = text };

    public static ToolResult FromStructuredContent(object content)
    {
        var jsonElement = JsonSerializer.SerializeToElement(content);
        
        return new ToolResult
        {
            StructuredContent = jsonElement
        };
    }
    
    public static ToolResult FromStructuredContent(object content, string text)
    {
        var jsonElement = JsonSerializer.SerializeToElement(content);
        
        return new ToolResult
        {
            Text = text,
            StructuredContent = jsonElement
        };
    }
}


