using Microsoft.AspNetCore.Http;

namespace Ballware.Shared.Web.Oidc.Tests;

public class SignOnRedirectSafetyTests
{
    private static HttpRequest Request(string host, int? port = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = port.HasValue ? new HostString(host, port.Value) : new HostString(host);

        return context.Request;
    }

    [Test]
    public void IsSafeRedirect_AllowsRelativePathWithConfiguredPrefix()
    {
        Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/chat/global", Request("llm.example"), [], ["/chat"]), Is.True);
    }

    [Test]
    public void IsSafeRedirect_RejectsRelativePathOutsideConfiguredPrefixes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/admin", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/chatglobal", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/chat/global", Request("llm.example"), [], []), Is.False);
        });
    }

    [Test]
    public void IsSafeRedirect_AllowsRootRedirectWithoutConfiguredPrefixes()
    {
        Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/", Request("llm.example"), [], []), Is.True);
    }

    [Test]
    public void IsSafeRedirect_RootPrefixOnlyAllowsExactRootNotOtherPaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/", Request("llm.example"), [], ["/"]), Is.True);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/whatever/deep", Request("llm.example"), [], ["/"]), Is.False);
        });
    }

    [Test]
    public void IsSafeRedirect_RejectsTraversalAndProtocolRelativePaths()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/chat/../admin", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("//evil.example/chat", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/\\evil.example/chat", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect(null, Request("llm.example"), [], ["/chat"]), Is.False);
        });
    }

    [Test]
    public void IsSafeRedirect_RejectsPercentEncodedDetours()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("%2F%2Fevil.example/chat", Request("llm.example"), [], ["/chat"]), Is.False);
            Assert.That(SignOnEndpointExtensions.IsSafeRedirect("/chat/%2E%2E/admin", Request("llm.example"), [], ["/chat"]), Is.False);
        });
    }

    [Test]
    public void IsSafeRedirect_RejectsNonHttpSchemeRedirects()
    {
        Assert.That(SignOnEndpointExtensions.IsSafeRedirect("javascript:alert(1)", Request("llm.example"), [], ["/chat"]), Is.False);
    }

    [Test]
    public void IsSafeRedirect_AllowsAbsoluteRedirectOnSameHost()
    {
        Assert.That(
            SignOnEndpointExtensions.IsSafeRedirect("https://llm.example/chat/global", Request("llm.example"), [], ["/chat"]),
            Is.True);
    }

    [Test]
    public void IsSafeRedirect_RejectsSameHostWithMismatchedNonDefaultPort()
    {
        Assert.That(
            SignOnEndpointExtensions.IsSafeRedirect("http://localhost:5001/chat", Request("localhost", 5000), [], ["/chat"]),
            Is.False);
    }

    [Test]
    public void IsSafeRedirect_AllowsConfiguredSignOnHostIgnoringPort()
    {
        Assert.That(
            SignOnEndpointExtensions.IsSafeRedirect(
                "https://reporting.ballware.de/chat/global",
                Request("llm-internal", 8080),
                ["https://reporting.ballware.de"],
                ["/chat"]),
            Is.True);
    }

    [Test]
    public void IsSafeRedirect_RejectsUnknownAbsoluteHost()
    {
        Assert.That(
            SignOnEndpointExtensions.IsSafeRedirect("https://evil.example/chat", Request("llm.example"), ["llm.ballware.de"], ["/chat"]),
            Is.False);
    }

    [TestCase("https://reporting.ballware.de/path", "reporting.ballware.de")]
    [TestCase("localhost:8080", "localhost")]
    [TestCase("host.example?query#fragment", "host.example")]
    [TestCase(" plain.host ", "plain.host")]
    public void TrimToHost_TrimsSchemePortAndPath(string value, string expected)
    {
        Assert.That(SignOnEndpointExtensions.TrimToHost(value), Is.EqualTo(expected));
    }
}
