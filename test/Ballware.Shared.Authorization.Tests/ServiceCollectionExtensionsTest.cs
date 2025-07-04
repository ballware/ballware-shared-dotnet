using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Authorization.Tests;

[TestFixture]
public class ServiceCollectionExtensionsTest
{
    [Test]
    public void AddBallwareMetaAuthorizationUtils_succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        services.AddLogging();
        services.AddBallwareSharedAuthorizationUtils("tenant", "sub", "right");
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var principalUtils = serviceProvider.GetService<IPrincipalUtils>();
        
        Assert.Multiple(() =>
        {
            Assert.That(principalUtils, Is.Not.Null);
        });
    }
}