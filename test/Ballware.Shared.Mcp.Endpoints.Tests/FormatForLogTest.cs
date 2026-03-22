using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ballware.Shared.Mcp.Endpoints.Internal;
using ModelContextProtocol.Protocol;

namespace Ballware.Shared.Mcp.Endpoints.Tests;

[TestFixture]
[SuppressMessage("Usage", "MCP001:Type is for evaluation purposes only and is subject to change or removal in future updates.")]
public class FormatForLogTest
{
    #region IsError

    [Test]
    public void FormatForLog_WithIsError_ContainsIsErrorTrue()
    {
        var result = new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "Something went wrong" }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("IsError=true"));
    }

    [Test]
    public void FormatForLog_WithoutIsError_DoesNotContainIsError()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "OK" }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Not.Contain("IsError"));
    }

    #endregion

    #region Empty content

    [Test]
    public void FormatForLog_WithNullContent_ReturnsEmptyContentMarker()
    {
        var result = new CallToolResult();

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Content=[]"));
    }

    #endregion

    #region TextContentBlock

    [Test]
    public void FormatForLog_WithShortTextContent_ContainsFullText()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Hello World" }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Text(Hello World)"));
    }

    [Test]
    public void FormatForLog_WithNullTextContent_ContainsEmptyText()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = null! }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Text()"));
    }

    [Test]
    public void FormatForLog_WithLongTextContent_TruncatesAt200Chars()
    {
        var longText = new string('x', 250);
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = longText }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("…"));
        Assert.That(log, Does.Contain(new string('x', 200)));
        Assert.That(log, Does.Not.Contain(new string('x', 201)));
    }

    [Test]
    public void FormatForLog_WithTextExactly200Chars_DoesNotTruncate()
    {
        var text = new string('a', 200);
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Not.Contain("…"));
        Assert.That(log, Does.Contain(new string('a', 200)));
    }

    #endregion

    #region ImageContentBlock

    [Test]
    public void FormatForLog_WithImageContent_ContainsMimeTypeAndByteCount()
    {
        var bytes = new byte[1024];
        var result = new CallToolResult
        {
            Content = [ImageContentBlock.FromBytes(bytes, "image/png")]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Image(mimeType=image/png, bytes=1024)"));
    }

    #endregion

    #region AudioContentBlock

    [Test]
    public void FormatForLog_WithAudioContent_ContainsMimeTypeAndByteCount()
    {
        var bytes = new byte[2048];
        var result = new CallToolResult
        {
            Content = [AudioContentBlock.FromBytes(bytes, "audio/mpeg")]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Audio(mimeType=audio/mpeg, bytes=2048)"));
    }

    #endregion

    #region EmbeddedResourceBlock

    [Test]
    public void FormatForLog_WithEmbeddedTextResource_ContainsResourceType()
    {
        var result = new CallToolResult
        {
            Content =
            [
                new EmbeddedResourceBlock
                {
                    Resource = new TextResourceContents { Uri = "file://test.txt", Text = "content" }
                }
            ]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("EmbeddedResource(type=TextResourceContents)"));
    }

    [Test]
    public void FormatForLog_WithEmbeddedBlobResource_ContainsResourceType()
    {
        var result = new CallToolResult
        {
            Content =
            [
                new EmbeddedResourceBlock
                {
                    Resource = BlobResourceContents.FromBytes(new byte[32], "application/octet-stream", "file://test.bin")
                }
            ]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("EmbeddedResource(type=BlobResourceContents)"));
    }

    [Test]
    public void FormatForLog_WithEmbeddedNullResource_ContainsUnknown()
    {
        var result = new CallToolResult
        {
            Content = [new EmbeddedResourceBlock { Resource = null! }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("EmbeddedResource(type=unknown)"));
    }

    #endregion

    #region StructuredContent

    [Test]
    public void FormatForLog_WithStructuredContent_ContainsJsonRepresentation()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""key"":""value""}");
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "value" }],
            StructuredContent = json
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("StructuredContent="));
        Assert.That(log, Does.Contain(@"""key"""));
    }

    [Test]
    public void FormatForLog_WithLongStructuredContent_TruncatesAt200Chars()
    {
        var longValue = new string('z', 210);
        var json = JsonSerializer.Deserialize<JsonElement>($@"{{""data"":""{longValue}""}}");
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "text" }],
            StructuredContent = json
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("…"));
        var structuredIdx = log.IndexOf("StructuredContent=", StringComparison.Ordinal);
        Assert.That(structuredIdx, Is.GreaterThanOrEqualTo(0));
        var structuredPart = log[structuredIdx..];
        Assert.That(structuredPart.Length, Is.LessThan("StructuredContent=".Length + 201 + 2));
    }

    [Test]
    public void FormatForLog_WithoutStructuredContent_DoesNotContainStructuredContentKey()
    {
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "plain text" }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Not.Contain("StructuredContent="));
    }

    #endregion

    #region Multiple content blocks

    [Test]
    public void FormatForLog_WithMultipleContentBlocks_ListsAllBlocks()
    {
        var imageBytes = new byte[512];
        var result = new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = "caption" },
                ImageContentBlock.FromBytes(imageBytes, "image/jpeg")
            ]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Text(caption)"));
        Assert.That(log, Does.Contain("Image(mimeType=image/jpeg, bytes=512)"));
    }

    #endregion

    #region Combined scenarios

    [Test]
    public void FormatForLog_WithIsErrorAndText_ContainsBothParts()
    {
        var result = new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = "Tool execution failed" }]
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("IsError=true"));
        Assert.That(log, Does.Contain("Text(Tool execution failed)"));
    }

    [Test]
    public void FormatForLog_WithTextAndStructuredContent_ContainsBothParts()
    {
        var json = JsonSerializer.Deserialize<JsonElement>(@"{""count"":42}");
        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "42 items" }],
            StructuredContent = json
        };

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Text(42 items)"));
        Assert.That(log, Does.Contain("StructuredContent="));
        Assert.That(log, Does.Contain("42"));
    }

    #endregion

    #region ListToolsResult

    [Test]
    public void FormatForLog_ListToolsResult_WithNoTools_ReturnsEmptyMarker()
    {
        var result = new ListToolsResult();

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Is.EqualTo("Tools=[]"));
    }

    [Test]
    public void FormatForLog_ListToolsResult_WithSingleTool_ContainsToolName()
    {
        var result = new ListToolsResult();
        result.Tools.Add(new ModelContextProtocol.Protocol.Tool { Name = "my-tool", Description = "Does something" });

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("my-tool"));
        Assert.That(log, Does.Contain("Tools(1)"));
    }

    [Test]
    public void FormatForLog_ListToolsResult_WithMultipleTools_ContainsAllNamesAndCount()
    {
        var result = new ListToolsResult();
        result.Tools.Add(new ModelContextProtocol.Protocol.Tool { Name = "tool-a", Description = "A" });
        result.Tools.Add(new ModelContextProtocol.Protocol.Tool { Name = "tool-b", Description = "B" });
        result.Tools.Add(new ModelContextProtocol.Protocol.Tool { Name = "tool-c", Description = "C" });

        var log = ToolRegistryRequestHandlers.FormatForLog(result);

        Assert.That(log, Does.Contain("Tools(3)"));
        Assert.That(log, Does.Contain("tool-a"));
        Assert.That(log, Does.Contain("tool-b"));
        Assert.That(log, Does.Contain("tool-c"));
    }

    #endregion
}
