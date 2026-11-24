using Ballware.Shared.Web.Session.Configuration;
using Microsoft.AspNetCore.Builder;

namespace Ballware.Shared.Web.Session.Tests;

public class WebApplicationExtensionsTests
{
    private static WebApplication CreateWebApplication(WebSessionOptions options)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Test"
        });


        builder.Services.AddBallwareWebSession(options);

        return builder.Build();
    }

    [Test]
    public void UseBallwareWebSession_RegistersMiddlewareAndReturnsApplication()
    {
        var app = CreateWebApplication(new WebSessionOptions { EnableSession = true });

        var result = app.UseBallwareWebSession();

        Assert.That(result, Is.SameAs(app));
    }

    [Test]
    public void UseBallwareWebSession_SkipsSessionMiddlewareWhenEnableSessionIsFalse()
    {
        var app = CreateWebApplication(new WebSessionOptions { EnableSession = false });

        var result = app.UseBallwareWebSession();

        Assert.That(result, Is.SameAs(app));
    }
}
