using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NJsonSchema;

namespace Ballware.Shared.Mcp.Endpoints.Internal;

internal static class ToolRegistryRequestHandlers
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
        
        var toolRegistry = requestContext.Services?.GetRequiredService<IToolRegistry>();

        if (toolRegistry == null)
        {
            throw new InvalidOperationException($"IToolRegistry not registered in service collection");
        }
        
        foreach (var tool in await toolRegistry.GetAllToolsAsync())
        {
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

        if (user == null || (!user.Identity?.IsAuthenticated ?? false))
        {
            throw new UnauthorizedAccessException();
        }
        
        var toolRegistry = requestContext.Services?.GetRequiredService<IToolRegistry>();
        
        if (toolRegistry == null)
        {
            throw new InvalidOperationException($"IToolRegistry not registered in service collection");
        }

        if (requestContext.Params is null)
        {
            throw new InvalidOperationException($"No parameters provided");
        }
        
        var tool = toolRegistry.GetToolByName(requestContext.Params.Name);
        
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
        
        return callToolResult;
    }
}