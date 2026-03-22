using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NJsonSchema;

namespace Ballware.Shared.Mcp.Endpoints.Internal;

internal class ToolRegistryRequestHandlers
{
    private static JsonElement CreateInputSchemaFromParams(IEnumerable<ToolParam> toolParams)
    {
        var schema = new JsonSchema
        {
            Type = JsonObjectType.Object,
            AllowAdditionalProperties = false,
        };

        foreach (var toolParam in toolParams)
        {
            schema.Properties[toolParam.Name] = new JsonSchemaProperty
            {
                Type = toolParam.Type switch
                {
                    ToolParamType.String => JsonObjectType.String,
                    ToolParamType.Number => JsonObjectType.Number,
                    ToolParamType.Boolean => JsonObjectType.Boolean,
                    _ => throw new NotSupportedException($"Unsupported tool param type {toolParam.Type}")
                },
                Description = toolParam.Description
            };

            if (toolParam.Required)
            {
                schema.RequiredProperties.Add(toolParam.Name);
            }
        }

        var json = schema.ToJson();
        
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
    
    public static async ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> requestContext,
        CancellationToken ct)
    {
        var result = new ListToolsResult();
        
        if (requestContext.Services == null)
        {
            throw new ArgumentNullException(nameof(requestContext.Services));
        }
        
        var logger = requestContext.Services.GetService<ILogger<ToolRegistryRequestHandlers>>();
        
        var user = requestContext.User;
        var toolRegistry = requestContext.Services.GetRequiredService<IToolRegistry>();
        
        foreach (var tool in await toolRegistry.GetAllToolsAsync(requestContext.Services, user))
        {
            if (tool.IsAuthorizedAsync != null)
            {
                if (user == null || !(await tool.IsAuthorizedAsync(requestContext.Services, user)))                
                {
                    continue;
                }
            }
            
            var protocolTool = new ModelContextProtocol.Protocol.Tool()
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = CreateInputSchemaFromParams(tool.Params)
            };

            if (tool.OutputSchema != null)
            {
                protocolTool.OutputSchema = JsonSerializer.Deserialize<JsonElement>(tool.OutputSchema);
            }
            
            result.Tools.Add(protocolTool);
        }
        
        logger?.LogDebug("ListToolsAsync result: {result}", FormatForLog(result));

        return result;
    }

    public static async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> requestContext,
        CancellationToken ct)
    {
        IServiceProvider? serviceProvider = requestContext.Services;
        ClaimsPrincipal? user = requestContext.User;
        
        if (serviceProvider == null)
        {
            throw new ArgumentNullException(nameof(requestContext.Services));
        }
        
        var logger = serviceProvider.GetService<ILogger<ToolRegistryRequestHandlers>>();
        var toolRegistry = requestContext.Services?.GetRequiredService<IToolRegistry>();
        
        if (toolRegistry == null)
        {
            throw new InvalidOperationException($"IToolRegistry not registered in service collection");
        }

        if (requestContext.Params is null)
        {
            throw new InvalidOperationException($"No parameters provided");
        }

        if (requestContext.Services is null)
        {
            throw new InvalidOperationException($"No service provider provided");       
        }
        
        var tool = await toolRegistry.GetToolByNameAsync(requestContext.Services, user, requestContext.Params.Name);
        
        if (tool == null)
        {
            throw new InvalidOperationException($"Tool {requestContext.Params.Name} not found");
        }
        
        var arguments = new Dictionary<string, object?>();
        
        foreach (var toolParam in tool.Params)
        {
            if (requestContext.Params.Arguments == null)
            {
                throw new InvalidOperationException($"No arguments provided for tool {tool.Name}");
            }

            try
            {
                var argument = requestContext.Params.Arguments.First(arg =>
                    toolParam.Name.Equals(arg.Key, StringComparison.InvariantCultureIgnoreCase));

                if (argument.Value.ValueKind == JsonValueKind.Undefined)
                {
                    if (toolParam.Required)
                    {
                        throw new InvalidOperationException(
                            $"Required argument {toolParam.Name} not provided for tool {tool.Name}");
                    }

                    continue;
                }

                arguments[toolParam.Name] = argument.Value.ValueKind switch
                {
                    JsonValueKind.String when toolParam.Type == ToolParamType.String => argument.Value.GetString(),
                    JsonValueKind.Number when toolParam.Type == ToolParamType.Number => argument.Value.GetDouble(),
                    JsonValueKind.True when toolParam.Type == ToolParamType.Boolean => true,
                    JsonValueKind.False when toolParam.Type == ToolParamType.Boolean => false,
                    _ => throw new InvalidOperationException(
                        $"Argument {toolParam.Name} for tool {tool.Name} has invalid type")
                };
            }
            catch (InvalidOperationException)
            {   
                if (toolParam.Required)
                {
                    throw new InvalidOperationException(
                        $"Required argument {toolParam.Name} not provided for tool {tool.Name}");
                }
            }
        }

        if (tool.IsAuthorizedAsync != null) 
        {
            if (user == null || !(await tool.IsAuthorizedAsync(serviceProvider, user)))
            {
                throw new UnauthorizedAccessException($"User is not authorized to execute tool {tool.Name}");
            }
        }
        
        logger?.LogDebug("CallToolAsync: {tool} with arguments {arguments}", tool.Name, arguments);
        
        var result = await tool.ExecuteAsync(serviceProvider, user, arguments);

        var callToolResult = new CallToolResult();

        if (result.StructuredContent != null)
        {
            callToolResult.StructuredContent = result.StructuredContent.Value;
            
            // Content must always be provided for backward compatibility.
            // Use explicit text if available, otherwise serialize structured content as JSON text.
            callToolResult.Content =
            [
                new TextContentBlock()
                {
                    Text = result.Text ?? JsonSerializer.Serialize(result.StructuredContent.Value)
                }
            ];
        }
        else if (result.Text != null)
        {
            callToolResult.Content =
            [
                new TextContentBlock()
                {
                    Text = result.Text
                }
            ];
        }
        
        logger?.LogDebug("CallToolAsync {tool} result: {result}", tool.Name, FormatForLog(callToolResult));
        
        return callToolResult;
    }

    internal static string FormatForLog(CallToolResult result)
    {
        var parts = new List<string>();

        if (result.IsError == true)
        {
            parts.Add("IsError=true");
        }

        if (result.Content is { Count: > 0 })
        {
            var contentDescriptions = result.Content.Select<ContentBlock, string>(block => block switch
            {
                TextContentBlock text =>
                    $"Text({(text.Text?.Length > 200 ? text.Text[..200] + "…" : text.Text ?? "")})",
                ImageContentBlock image =>
                    $"Image(mimeType={image.MimeType}, bytes={image.DecodedData.Length})",
                AudioContentBlock audio =>
                    $"Audio(mimeType={audio.MimeType}, bytes={audio.DecodedData.Length})",
                EmbeddedResourceBlock resource =>
                    $"EmbeddedResource(type={resource.Resource?.GetType().Name ?? "unknown"})",
                _ => $"Unknown(type={block.Type})"
            });

            parts.Add($"Content=[{string.Join(", ", contentDescriptions)}]");
        }
        else
        {
            parts.Add("Content=[]");
        }

        if (result.StructuredContent.HasValue)
        {
            var raw = result.StructuredContent.Value.GetRawText();
            parts.Add($"StructuredContent={( raw.Length > 200 ? raw[..200] + "…" : raw )}");
        }

        return string.Join(", ", parts);
    }

    internal static string FormatForLog(ListToolsResult result)
    {
        if (result.Tools.Count == 0)
        {
            return "Tools=[]";
        }

        var toolNames = result.Tools.Select(t => t.Name);
        return $"Tools({result.Tools.Count})=[{string.Join(", ", toolNames)}]";
    }
}