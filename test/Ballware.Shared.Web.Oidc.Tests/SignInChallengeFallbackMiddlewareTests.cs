using Ballware.Shared.Web.Oidc.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Ballware.Shared.Web.Oidc.Tests;

public class SignInChallengeFallbackMiddlewareTests
{
    private class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }
    }

    private static SignInChallengeFallbackMiddleware CreateMiddleware(RequestDelegate next) =>
        new(
            next,
            Options.Create(new BrowserAuthorizationOptions
            {
                Authority = "https://idp.example/realms/ballware",
                Audience = "metaApi",
                ClientId = "llm-app",
                RequiredUserScope = "mlApi"
            }),
            NullLogger<SignInChallengeFallbackMiddleware>.Instance);

    [Test]
    public async Task InvokeAsync_Returns401AndKeepsChallengeExceptionFromEscaping()
    {
        var middleware = CreateMiddleware(_ => throw new OpenIdConnectProtocolException("challenge failed"));

        var context = new DefaultHttpContext();
        context.Items[OpenIdConnectPublicRedirectExtensions.EffectiveRedirectUriItemKey] = "https://llm.example/signin-oidc";

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
    }

    [Test]
    public async Task InvokeAsync_RethrowsWhenResponseHasAlreadyStarted()
    {
        RequestDelegate next = _ => throw new OpenIdConnectProtocolException("late failure");

        var middleware = CreateMiddleware(next);
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        await Assert.ThatAsync(() => middleware.InvokeAsync(context), Throws.TypeOf<OpenIdConnectProtocolException>());
    }

    [Test]
    public async Task InvokeAsync_RethrowsUnrelatedExceptions()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("unrelated"));
        var context = new DefaultHttpContext();

        await Assert.ThatAsync(() => middleware.InvokeAsync(context), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task InvokeAsync_PassesThroughOnSuccessfulRequest()
    {
        var nextCalled = false;

        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.True);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        });
    }
}
