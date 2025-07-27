using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Authorization.Jint.Tests;

[TestFixture]
public class ServiceCollectionExtensionsTest
{
    [Test]
    public void AddBallwareMetaJintRightsChecker_succeeds()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        services.AddLogging();
        services.AddBallwareMetaJintRightsChecker();
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Assert
        var tenantRightsChecker = serviceProvider.GetService<ITenantRightsChecker>();
        var entityRightsChecker = serviceProvider.GetService<IEntityRightsChecker>();
        
        Assert.Multiple(() =>
        {
            Assert.That(tenantRightsChecker, Is.Not.Null);
            Assert.That(entityRightsChecker, Is.Not.Null);
        });
    }
}