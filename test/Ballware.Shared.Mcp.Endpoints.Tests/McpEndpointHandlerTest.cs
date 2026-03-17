using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Ballware.Shared.Mcp.Endpoints.Internal;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace Ballware.Shared.Mcp.Endpoints.Tests;

[TestFixture]
[SuppressMessage("Usage", "MCP001:Type is for evaluation purposes only and is subject to change or removal in future updates.")]
public class McpEndpointHandlerTest
{
    private Mock<IToolRegistry> ToolRegistryMock { get; set; } = null!;
    private ServiceProvider ServiceProvider { get; set; } = null!;

    [SetUp]
    public void SetUp()
    {
        ToolRegistryMock = new Mock<IToolRegistry>();

        var services = new ServiceCollection();
        services.AddSingleton(ToolRegistryMock.Object);
        ServiceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        ServiceProvider.Dispose();
    }

    private RequestContext<TParams> CreateRequestContext<TParams>(
        ClaimsPrincipal? user = null,
        TParams? requestParams = default)
    {
        var mcpServerMock = new Mock<McpServer>();
        mcpServerMock.Setup(s => s.Services).Returns(ServiceProvider);

        var jsonRpcRequest = new JsonRpcRequest { Method = "test" };

        var context = new RequestContext<TParams>(mcpServerMock.Object, jsonRpcRequest)
        {
            Params = requestParams,
            Services = ServiceProvider
        };

        if (user != null)
        {
            context.User = user;
        }

        return context;
    }

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static Tool CreateDummyTool(string name, string description) =>
        new()
        {
            Name = name,
            Description = description,
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText($"Result from {name}"))
        };

    #region ListToolsAsync

    [Test]
    public async Task ListToolsAsync_WithNoTools_ReturnsEmptyList()
    {
        // Arrange
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(Enumerable.Empty<Tool>());

        var context = CreateRequestContext<ListToolsRequestParams>();

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Tools, Is.Empty);
    }

    [Test]
    public async Task ListToolsAsync_WithSingleTool_ReturnsTool()
    {
        // Arrange
        var tool = CreateDummyTool("echo", "Echoes input");
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var context = CreateRequestContext<ListToolsRequestParams>();

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Has.Count.EqualTo(1));
        Assert.That(result.Tools[0].Name, Is.EqualTo("echo"));
        Assert.That(result.Tools[0].Description, Is.EqualTo("Echoes input"));
    }

    [Test]
    public async Task ListToolsAsync_WithMultipleTools_ReturnsAllTools()
    {
        // Arrange
        var tools = new[]
        {
            CreateDummyTool("tool-a", "Tool A"),
            CreateDummyTool("tool-b", "Tool B"),
            CreateDummyTool("tool-c", "Tool C")
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(tools);

        var context = CreateRequestContext<ListToolsRequestParams>();

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Has.Count.EqualTo(3));
        var names = result.Tools.Select(t => t.Name).ToList();
        Assert.That(names, Is.EquivalentTo(new[] { "tool-a", "tool-b", "tool-c" }));
    }

    [Test]
    public async Task ListToolsAsync_WithToolParams_GeneratesInputSchema()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "search",
            Description = "Search tool",
            Params =
            [
                new ToolParam { Name = "query", Description = "Search query", Type = ToolParamType.String, Required = true },
                new ToolParam { Name = "limit", Description = "Max results", Type = ToolParamType.Number },
                new ToolParam { Name = "exact", Description = "Exact match", Type = ToolParamType.Boolean }
            ],
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var context = CreateRequestContext<ListToolsRequestParams>();

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Has.Count.EqualTo(1));
        var schemaJson = result.Tools[0].InputSchema.GetRawText();

        Assert.That(schemaJson, Does.Contain("query"));
        Assert.That(schemaJson, Does.Contain("limit"));
        Assert.That(schemaJson, Does.Contain("exact"));
        Assert.That(schemaJson, Does.Contain("string"));
        Assert.That(schemaJson, Does.Contain("number"));
        Assert.That(schemaJson, Does.Contain("boolean"));
    }

    [Test]
    public async Task ListToolsAsync_WithRequiredParam_IncludesRequiredInSchema()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "tool-with-required",
            Description = "Tool with required param",
            Params =
            [
                new ToolParam { Name = "required_param", Description = "Required", Type = ToolParamType.String, Required = true },
                new ToolParam { Name = "optional_param", Description = "Optional", Type = ToolParamType.String, Required = false }
            ],
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var context = CreateRequestContext<ListToolsRequestParams>();

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        var schemaJson = result.Tools[0].InputSchema.GetRawText();
        Assert.That(schemaJson, Does.Contain("required"));
        Assert.That(schemaJson, Does.Contain("required_param"));
    }

    [Test]
    public async Task ListToolsAsync_WithOutputSchema_IncludesOutputSchema()
    {
        // Arrange
        var outputSchema = JsonSerializer.Serialize(new { type = "object", properties = new { result = new { type = "string" } } });
        var tool = new Tool
        {
            Name = "structured-tool",
            Description = "Tool with output schema",
            OutputSchema = outputSchema,
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var context = CreateRequestContext<ListToolsRequestParams>();

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools[0].OutputSchema, Is.Not.Null);
        var outputSchemaJson = result.Tools[0].OutputSchema!.Value.GetRawText();
        Assert.That(outputSchemaJson, Does.Contain("result"));
    }

    #endregion

    #region ListToolsAsync - Authorization filtering

    [Test]
    public async Task ListToolsAsync_WithAuthorizedTool_AndAuthorizedUser_ReturnsTool()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "admin-tool",
            Description = "Admin only tool",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var user = CreateUser(new Claim(ClaimTypes.Role, "admin"));
        var context = CreateRequestContext<ListToolsRequestParams>(user: user);

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Has.Count.EqualTo(1));
        Assert.That(result.Tools[0].Name, Is.EqualTo("admin-tool"));
    }

    [Test]
    public async Task ListToolsAsync_WithAuthorizedTool_AndUnauthorizedUser_ExcludesTool()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "admin-tool",
            Description = "Admin only tool",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var user = CreateUser(new Claim(ClaimTypes.Role, "user"));
        var context = CreateRequestContext<ListToolsRequestParams>(user: user);

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Is.Empty);
    }

    [Test]
    public async Task ListToolsAsync_WithAuthorizedTool_AndNoUser_ExcludesTool()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "admin-tool",
            Description = "Admin only tool",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([tool]);

        var context = CreateRequestContext<ListToolsRequestParams>(user: null);

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Is.Empty);
    }

    [Test]
    public async Task ListToolsAsync_MixedAuthorization_ReturnsOnlyAuthorizedTools()
    {
        // Arrange
        var publicTool = CreateDummyTool("public-tool", "Public tool");
        var adminTool = new Tool
        {
            Name = "admin-tool",
            Description = "Admin only",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };
        var editorTool = new Tool
        {
            Name = "editor-tool",
            Description = "Editor only",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("editor")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };

        ToolRegistryMock.Setup(r => r.GetAllToolsAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync([publicTool, adminTool, editorTool]);

        var user = CreateUser(new Claim(ClaimTypes.Role, "editor"));
        var context = CreateRequestContext<ListToolsRequestParams>(user: user);

        // Act
        var result = await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Tools, Has.Count.EqualTo(2));
        var names = result.Tools.Select(t => t.Name).ToList();
        Assert.That(names, Does.Contain("public-tool"));
        Assert.That(names, Does.Contain("editor-tool"));
        Assert.That(names, Does.Not.Contain("admin-tool"));
    }

    #endregion

    #region ListToolsAsync - Error cases

    [Test]
    public void ListToolsAsync_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        var mcpServerMock = new Mock<McpServer>();
        mcpServerMock.Setup(s => s.Services).Returns((IServiceProvider?)null);

        var jsonRpcRequest = new JsonRpcRequest { Method = "tools/list" };
        var context = new RequestContext<ListToolsRequestParams>(mcpServerMock.Object, jsonRpcRequest)
        {
            Services = null
        };

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ToolRegistryRequestHandlers.ListToolsAsync(context, CancellationToken.None));
    }

    #endregion

    #region CallToolAsync - Text results

    [Test]
    public async Task CallToolAsync_WithTextResult_ReturnsTextContent()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "hello",
            Description = "Says hello",
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("Hello, World!"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "hello")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "hello",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        var result = await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.Content, Has.Count.EqualTo(1));
        Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
        Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo("Hello, World!"));
        Assert.That(result.StructuredContent, Is.Null);
    }

    [Test]
    public async Task CallToolAsync_WithStringParam_PassesArgumentCorrectly()
    {
        // Arrange
        string? receivedName = null;
        var tool = new Tool
        {
            Name = "greet",
            Description = "Greets a user",
            Params =
            [
                new ToolParam { Name = "name", Description = "User name", Type = ToolParamType.String, Required = true }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedName = args["name"] as string;
                return Task.FromResult(ToolResult.FromText($"Hello, {receivedName}!"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "greet")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "greet",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement("Alice")
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        var result = await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedName, Is.EqualTo("Alice"));
        Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo("Hello, Alice!"));
    }

    [Test]
    public async Task CallToolAsync_WithNumberParam_PassesArgumentCorrectly()
    {
        // Arrange
        double? receivedNumber = null;
        var tool = new Tool
        {
            Name = "double-it",
            Description = "Doubles a number",
            Params =
            [
                new ToolParam { Name = "value", Description = "Number to double", Type = ToolParamType.Number, Required = true }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedNumber = (double)args["value"]!;
                return Task.FromResult(ToolResult.FromText($"Result: {receivedNumber * 2}"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "double-it")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "double-it",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["value"] = JsonSerializer.SerializeToElement(21.0)
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedNumber, Is.EqualTo(21.0));
    }

    [Test]
    public async Task CallToolAsync_WithBooleanTrueParam_PassesArgumentCorrectly()
    {
        // Arrange
        bool? receivedFlag = null;
        var tool = new Tool
        {
            Name = "flag-tool",
            Description = "Tool with boolean flag",
            Params =
            [
                new ToolParam { Name = "verbose", Description = "Verbose output", Type = ToolParamType.Boolean, Required = true }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedFlag = (bool)args["verbose"]!;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "flag-tool")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "flag-tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["verbose"] = JsonSerializer.SerializeToElement(true)
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedFlag, Is.True);
    }

    [Test]
    public async Task CallToolAsync_WithBooleanFalseParam_PassesFalseCorrectly()
    {
        // Arrange
        bool? receivedFlag = null;
        var tool = new Tool
        {
            Name = "flag-tool",
            Description = "Tool with boolean flag",
            Params =
            [
                new ToolParam { Name = "verbose", Description = "Verbose output", Type = ToolParamType.Boolean, Required = true }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedFlag = (bool)args["verbose"]!;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "flag-tool")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "flag-tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["verbose"] = JsonSerializer.SerializeToElement(false)
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedFlag, Is.False);
    }

    [Test]
    public async Task CallToolAsync_WithMultipleParams_PassesAllArguments()
    {
        // Arrange
        IDictionary<string, object?>? receivedArgs = null;
        var tool = new Tool
        {
            Name = "multi-param",
            Description = "Multi parameter tool",
            Params =
            [
                new ToolParam { Name = "text", Description = "Text", Type = ToolParamType.String, Required = true },
                new ToolParam { Name = "count", Description = "Count", Type = ToolParamType.Number, Required = true },
                new ToolParam { Name = "flag", Description = "Flag", Type = ToolParamType.Boolean }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedArgs = args;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "multi-param")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "multi-param",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["text"] = JsonSerializer.SerializeToElement("hello"),
                ["count"] = JsonSerializer.SerializeToElement(5),
                ["flag"] = JsonSerializer.SerializeToElement(true)
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!["text"], Is.EqualTo("hello"));
        Assert.That(receivedArgs["count"], Is.EqualTo(5.0));
        Assert.That(receivedArgs["flag"], Is.EqualTo(true));
    }

    #endregion

    #region CallToolAsync - Structured results

    [Test]
    public async Task CallToolAsync_WithStructuredContent_ReturnsStructuredContentAndTextFallback()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "structured",
            Description = "Returns structured content",
            ExecuteAsync = (_, _, _) =>
                Task.FromResult(ToolResult.FromStructuredContent(new { Name = "Test", Value = 42 }))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "structured")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "structured",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        var result = await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.StructuredContent, Is.Not.Null);
        Assert.That(result.Content, Has.Count.EqualTo(1));
        var textContent = (TextContentBlock)result.Content[0];
        Assert.That(textContent.Text, Does.Contain("Test"));
        Assert.That(textContent.Text, Does.Contain("42"));
    }

    [Test]
    public async Task CallToolAsync_WithStructuredContentAndText_UsesExplicitText()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "structured-with-text",
            Description = "Returns structured content with text",
            ExecuteAsync = (_, _, _) =>
                Task.FromResult(ToolResult.FromStructuredContent(
                    new { Name = "Test", Value = 42 },
                    "Summary: Test=42"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "structured-with-text")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "structured-with-text",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        var result = await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(result.StructuredContent, Is.Not.Null);
        Assert.That(result.Content, Has.Count.EqualTo(1));
        var textContent = (TextContentBlock)result.Content[0];
        Assert.That(textContent.Text, Is.EqualTo("Summary: Test=42"));
    }

    #endregion

    #region CallToolAsync - Authorization

    [Test]
    public async Task CallToolAsync_WithAuthorizedUser_ExecutesTool()
    {
        // Arrange
        var executed = false;
        var tool = new Tool
        {
            Name = "admin-action",
            Description = "Admin action",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) =>
            {
                executed = true;
                return Task.FromResult(ToolResult.FromText("admin action executed"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "admin-action")).ReturnsAsync(tool);

        var user = CreateUser(new Claim(ClaimTypes.Role, "admin"));
        var callParams = new CallToolRequestParams
        {
            Name = "admin-action",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(user: user, requestParams: callParams);

        // Act
        var result = await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(executed, Is.True);
        Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo("admin action executed"));
    }

    [Test]
    public void CallToolAsync_WithUnauthorizedUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "admin-action",
            Description = "Admin action",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("should not reach"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "admin-action")).ReturnsAsync(tool);

        var user = CreateUser(new Claim(ClaimTypes.Role, "user"));
        var callParams = new CallToolRequestParams
        {
            Name = "admin-action",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(user: user, requestParams: callParams);

        // Act & Assert
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    [Test]
    public void CallToolAsync_WithAuthorizedTool_AndNoUser_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "admin-action",
            Description = "Admin action",
            IsAuthorizedAsync = (_, user) => Task.FromResult(user.IsInRole("admin")),
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("should not reach"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "admin-action")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "admin-action",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(user: null, requestParams: callParams);

        // Act & Assert
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    #endregion

    #region CallToolAsync - Error cases

    [Test]
    public void CallToolAsync_WithUnknownTool_ThrowsInvalidOperationException()
    {
        // Arrange
        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "unknown")).ReturnsAsync((Tool?)null);

        var callParams = new CallToolRequestParams
        {
            Name = "unknown",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    [Test]
    public void CallToolAsync_WithNullParams_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateRequestContext<CallToolRequestParams>(requestParams: null);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    [Test]
    public void CallToolAsync_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        var mcpServerMock = new Mock<McpServer>();
        mcpServerMock.Setup(s => s.Services).Returns((IServiceProvider?)null);

        var jsonRpcRequest = new JsonRpcRequest { Method = "tools/call" };
        var context = new RequestContext<CallToolRequestParams>(mcpServerMock.Object, jsonRpcRequest)
        {
            Services = null,
            Params = new CallToolRequestParams
            {
                Name = "some-tool",
                Arguments = new Dictionary<string, JsonElement>()
            }
        };

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    [Test]
    public void CallToolAsync_WithMissingRequiredArgument_ThrowsInvalidOperationException()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "requires-args",
            Description = "Tool requiring arguments",
            Params =
            [
                new ToolParam { Name = "required_param", Description = "Required", Type = ToolParamType.String, Required = true }
            ],
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "requires-args")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "requires-args",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    [Test]
    public void CallToolAsync_WithNullArguments_AndRequiredParam_ThrowsInvalidOperationException()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "requires-args",
            Description = "Tool requiring arguments",
            Params =
            [
                new ToolParam { Name = "required_param", Description = "Required", Type = ToolParamType.String, Required = true }
            ],
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "requires-args")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "requires-args",
            Arguments = null
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    [Test]
    public void CallToolAsync_WithWrongArgumentType_ThrowsInvalidOperationException()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "typed-tool",
            Description = "Tool with typed param",
            Params =
            [
                new ToolParam { Name = "count", Description = "A number", Type = ToolParamType.Number, Required = true }
            ],
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "typed-tool")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "typed-tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["count"] = JsonSerializer.SerializeToElement("not-a-number")
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None));
    }

    #endregion

    #region CallToolAsync - Optional parameters

    [Test]
    public async Task CallToolAsync_WithOptionalParamMissing_ExecutesWithoutOptionalParam()
    {
        // Arrange
        IDictionary<string, object?>? receivedArgs = null;
        var tool = new Tool
        {
            Name = "optional-tool",
            Description = "Tool with optional param",
            Params =
            [
                new ToolParam { Name = "required_param", Description = "Required", Type = ToolParamType.String, Required = true },
                new ToolParam { Name = "optional_param", Description = "Optional", Type = ToolParamType.String, Required = false }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedArgs = args;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "optional-tool")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "optional-tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["required_param"] = JsonSerializer.SerializeToElement("value")
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.ContainsKey("required_param"), Is.True);
        Assert.That(receivedArgs.ContainsKey("optional_param"), Is.False);
    }

    [Test]
    public async Task CallToolAsync_WithNoParams_ExecutesToolWithEmptyArguments()
    {
        // Arrange
        IDictionary<string, object?>? receivedArgs = null;
        var tool = new Tool
        {
            Name = "no-params",
            Description = "Tool without params",
            ExecuteAsync = (_, _, args) =>
            {
                receivedArgs = args;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "no-params")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "no-params",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs, Is.Empty);
    }

    #endregion

    #region CallToolAsync - User and ServiceProvider propagation

    [Test]
    public async Task CallToolAsync_PropagatesUserToToolExecution()
    {
        // Arrange
        ClaimsPrincipal? receivedUser = null;
        var tool = new Tool
        {
            Name = "user-aware",
            Description = "Tool that uses user context",
            ExecuteAsync = (_, user, _) =>
            {
                receivedUser = user;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "user-aware")).ReturnsAsync(tool);

        var expectedUser = CreateUser(
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Email, "test@example.com"));

        var callParams = new CallToolRequestParams
        {
            Name = "user-aware",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(user: expectedUser, requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedUser, Is.Not.Null);
        Assert.That(receivedUser!.Identity!.Name, Is.EqualTo("testuser"));
        Assert.That(receivedUser.FindFirst(ClaimTypes.Email)?.Value, Is.EqualTo("test@example.com"));
    }

    [Test]
    public async Task CallToolAsync_PropagatesServiceProviderToToolExecution()
    {
        // Arrange
        IServiceProvider? receivedProvider = null;
        var tool = new Tool
        {
            Name = "service-aware",
            Description = "Tool that uses service provider",
            ExecuteAsync = (sp, _, _) =>
            {
                receivedProvider = sp;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "service-aware")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "service-aware",
            Arguments = new Dictionary<string, JsonElement>()
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedProvider, Is.Not.Null);
        Assert.That(receivedProvider, Is.SameAs(ServiceProvider));
    }

    #endregion

    #region CallToolAsync - Case insensitive argument matching

    [Test]
    public async Task CallToolAsync_WithDifferentCaseArgument_MatchesParamCaseInsensitive()
    {
        // Arrange
        string? receivedValue = null;
        var tool = new Tool
        {
            Name = "case-tool",
            Description = "Tool testing case sensitivity",
            Params =
            [
                new ToolParam { Name = "MyParam", Description = "A param", Type = ToolParamType.String, Required = true }
            ],
            ExecuteAsync = (_, _, args) =>
            {
                receivedValue = args["MyParam"] as string;
                return Task.FromResult(ToolResult.FromText("ok"));
            }
        };

        ToolRegistryMock.Setup(r => r.GetToolByNameAsync(It.IsAny<IServiceProvider>(), It.IsAny<ClaimsPrincipal>(), "case-tool")).ReturnsAsync(tool);

        var callParams = new CallToolRequestParams
        {
            Name = "case-tool",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["myparam"] = JsonSerializer.SerializeToElement("test-value")
            }
        };
        var context = CreateRequestContext(requestParams: callParams);

        // Act
        await ToolRegistryRequestHandlers.CallToolAsync(context, CancellationToken.None);

        // Assert
        Assert.That(receivedValue, Is.EqualTo("test-value"));
    }

    #endregion
}

