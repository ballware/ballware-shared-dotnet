using System.Security.Claims;
using Ballware.Shared.Mcp.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Mcp.Endpoints.Tests;

[TestFixture]
public class ToolRegistryTest
{
    private ToolRegistry _sut = null!;
    private IServiceProvider _serviceProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _sut = new ToolRegistry();
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region RegisterTool / GetToolByName

    [Test]
    public async Task GetToolByName_WithNoToolsRegistered_ReturnsNull()
    {
        // Act
        var result = await _sut.GetToolByNameAsync(_serviceProvider, "nonexistent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RegisterTool_AndGetToolByName_ReturnsSameTool()
    {
        // Arrange
        var tool = CreateDummyTool("test-tool", "A test tool");

        // Act
        _sut.RegisterStaticTool(tool);
        var result = await _sut.GetToolByNameAsync(_serviceProvider, "test-tool");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.SameAs(tool));
    }

    [Test]
    public async Task GetToolByName_WithWrongName_ReturnsNull()
    {
        // Arrange
        _sut.RegisterStaticTool(CreateDummyTool("tool-a", "Tool A"));

        // Act
        var result = await _sut.GetToolByNameAsync(_serviceProvider, "tool-b");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RegisterTool_WithSameName_OverwritesPreviousTool()
    {
        // Arrange
        var toolV1 = CreateDummyTool("my-tool", "Version 1");
        var toolV2 = CreateDummyTool("my-tool", "Version 2");

        // Act
        _sut.RegisterStaticTool(toolV1);
        _sut.RegisterStaticTool(toolV2);
        var result = await _sut.GetToolByNameAsync(_serviceProvider, "my-tool");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Description, Is.EqualTo("Version 2"));
        Assert.That(result, Is.SameAs(toolV2));
    }

    #endregion

    #region GetAllToolsAsync

    [Test]
    public async Task GetAllToolsAsync_WithNoToolsRegistered_ReturnsEmptyCollection()
    {
        // Act
        var result = await _sut.GetAllToolsAsync(_serviceProvider);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task GetAllToolsAsync_WithSingleToolRegistered_ReturnsSingleTool()
    {
        // Arrange
        var tool = CreateDummyTool("single-tool", "A single tool");
        _sut.RegisterStaticTool(tool);

        // Act
        var result = (await _sut.GetAllToolsAsync(_serviceProvider)).ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("single-tool"));
    }

    [Test]
    public async Task GetAllToolsAsync_WithMultipleToolsRegistered_ReturnsAllTools()
    {
        // Arrange
        _sut.RegisterStaticTool(CreateDummyTool("tool-a", "Tool A"));
        _sut.RegisterStaticTool(CreateDummyTool("tool-b", "Tool B"));
        _sut.RegisterStaticTool(CreateDummyTool("tool-c", "Tool C"));

        // Act
        var result = (await _sut.GetAllToolsAsync(_serviceProvider)).ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        var names = result.Select(t => t.Name).ToList();
        Assert.That(names, Does.Contain("tool-a"));
        Assert.That(names, Does.Contain("tool-b"));
        Assert.That(names, Does.Contain("tool-c"));
    }

    [Test]
    public async Task GetAllToolsAsync_AfterOverwrite_ReturnsLatestVersion()
    {
        // Arrange
        _sut.RegisterStaticTool(CreateDummyTool("tool-a", "Version 1"));
        _sut.RegisterStaticTool(CreateDummyTool("tool-b", "Tool B"));
        _sut.RegisterStaticTool(CreateDummyTool("tool-a", "Version 2"));

        // Act
        var result = (await _sut.GetAllToolsAsync(_serviceProvider)).ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        var toolA = result.First(t => t.Name == "tool-a");
        Assert.That(toolA.Description, Is.EqualTo("Version 2"));
    }

    #endregion

    #region Tool with Params

    [Test]
    public async Task RegisterTool_WithParams_PreservesParams()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "parameterized-tool",
            Description = "Tool with parameters",
            Params =
            [
                new ToolParam { Name = "name", Description = "The name", Type = ToolParamType.String, Required = true },
                new ToolParam { Name = "count", Description = "The count", Type = ToolParamType.Number },
                new ToolParam { Name = "verbose", Description = "Verbose output", Type = ToolParamType.Boolean }
            ],
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };

        // Act
        _sut.RegisterStaticTool(tool);
        var result = await _sut.GetToolByNameAsync(_serviceProvider, "parameterized-tool");

        // Assert
        Assert.That(result, Is.Not.Null);
        var paramList = result!.Params.ToList();
        Assert.That(paramList, Has.Count.EqualTo(3));
        Assert.That(paramList[0].Name, Is.EqualTo("name"));
        Assert.That(paramList[0].Type, Is.EqualTo(ToolParamType.String));
        Assert.That(paramList[0].Required, Is.True);
        Assert.That(paramList[1].Type, Is.EqualTo(ToolParamType.Number));
        Assert.That(paramList[1].Required, Is.False);
        Assert.That(paramList[2].Type, Is.EqualTo(ToolParamType.Boolean));
    }

    #endregion

    #region Tool Execute

    [Test]
    public async Task RegisterTool_ExecuteAsync_IsInvocable()
    {
        // Arrange
        var executed = false;
        var tool = new Tool
        {
            Name = "executable-tool",
            Description = "Tool that can be executed",
            ExecuteAsync = (_, _, _) =>
            {
                executed = true;
                return Task.FromResult(ToolResult.FromText("executed"));
            }
        };

        _sut.RegisterStaticTool(tool);
        var resolved = await _sut.GetToolByNameAsync(_serviceProvider, "executable-tool");

        // Act
        var result = await resolved!.ExecuteAsync(null!, null, new Dictionary<string, object?>());

        // Assert
        Assert.That(executed, Is.True);
        Assert.That(result.Text, Is.EqualTo("executed"));
    }

    [Test]
    public async Task RegisterTool_WithAuthorization_IsAuthorizedAsyncIsInvocable()
    {
        // Arrange
        var tool = new Tool
        {
            Name = "authorized-tool",
            Description = "Tool with authorization",
            IsAuthorizedAsync = (_, user) =>
            {
                var hasRole = user.IsInRole("admin");
                return Task.FromResult(hasRole);
            },
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText("ok"))
        };

        _sut.RegisterStaticTool(tool);
        var resolved = await _sut.GetToolByNameAsync(_serviceProvider, "authorized-tool");

        var adminPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "admin")], "test"));
        var userPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "user")], "test"));

        // Act & Assert
        Assert.That(await resolved!.IsAuthorizedAsync!(null!, adminPrincipal), Is.True);
        Assert.That(await resolved.IsAuthorizedAsync!(null!, userPrincipal), Is.False);
    }

    #endregion

    #region ToolResult

    [Test]
    public void ToolResult_FromText_CreatesTextResult()
    {
        // Act
        var result = ToolResult.FromText("hello");

        // Assert
        Assert.That(result.Text, Is.EqualTo("hello"));
        Assert.That(result.StructuredContent, Is.Null);
    }

    [Test]
    public void ToolResult_FromStructuredContent_CreatesStructuredResult()
    {
        // Arrange
        var content = new { Name = "Test", Value = 42 };

        // Act
        var result = ToolResult.FromStructuredContent(content);

        // Assert
        Assert.That(result.StructuredContent, Is.Not.Null);
        Assert.That(result.Text, Is.Null);
    }

    [Test]
    public void ToolResult_FromStructuredContentWithText_CreatesBothTextAndStructured()
    {
        // Arrange
        var content = new { Name = "Test", Value = 42 };

        // Act
        var result = ToolResult.FromStructuredContent(content, "summary text");

        // Assert
        Assert.That(result.StructuredContent, Is.Not.Null);
        Assert.That(result.Text, Is.EqualTo("summary text"));
    }

    #endregion

    private static Tool CreateDummyTool(string name, string description) =>
        new()
        {
            Name = name,
            Description = description,
            ExecuteAsync = (_, _, _) => Task.FromResult(ToolResult.FromText($"Result from {name}"))
        };
}

