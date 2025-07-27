using System.Text.Json;
using Ballware.Shared.Authorization.Jint.Internal;
using Moq;

namespace Ballware.Shared.Authorization.Jint.Tests;

[TestFixture]
public class JavascriptEntityRightsCheckerTest
{
    [Test]
    public void HasRightAsync_ShouldReturnTrue_WhenRightsCheckScriptIsEmpty()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        
        // Arrange
        var entitymetadataMock = new Mock<IEntityAuthorizationMetadata>();
        
        entitymetadataMock
            .Setup(m => m.RightsCheckScript)
            .Returns(null as string);
        
        // Act
        var rightsChecker = new JavascriptEntityRightsChecker();
        
        var result = rightsChecker.HasRightAsync(
            expectedTenantId,
            entitymetadataMock.Object,
            new Dictionary<string, object> { { "right", new string[] { "add", "custom" } } },
            "edit",
            new { Id = Guid.NewGuid() },
            true
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void HasRightAsync_ShouldReturnTrue_WhenTenantResultIsTrue()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        
        // Arrange
        var rightsCheckScript = "return result;";
        
        var entitymetadataMock = new Mock<IEntityAuthorizationMetadata>();
        entitymetadataMock
            .Setup(m => m.RightsCheckScript)
            .Returns(rightsCheckScript);
        
        // Act
        var rightsChecker = new JavascriptEntityRightsChecker();
        
        var result = rightsChecker.HasRightAsync(
            expectedTenantId,
            entitymetadataMock.Object,
            new Dictionary<string, object> { { "right", new string[] { "view", "add" } } },
            "edit",
            new { Id = Guid.NewGuid() },
            true
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void HasRightAsync_ShouldReturnTrue_WhenUserinfoContainsRight()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        
        // Arrange
        var rightsCheckScript = "return userinfo.right.includes(right);";
        
        var entitymetadataMock = new Mock<IEntityAuthorizationMetadata>();
        entitymetadataMock
            .Setup(m => m.RightsCheckScript)
            .Returns(rightsCheckScript);
        
        // Act
        var rightsChecker = new JavascriptEntityRightsChecker();
        
        var result = rightsChecker.HasRightAsync(
            expectedTenantId,
            entitymetadataMock.Object,
            new Dictionary<string, object> { { "right", new string[] { "add", "edit" } } },
            "edit",
            new { Id = Guid.NewGuid() },
            true
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void StateAllowedAsync_ShouldReturnFalse_WhenStateAllowedScriptIsEmpty()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedId = Guid.NewGuid();
        var expectedCurrentState = 1;

        var entityMetadataMock = new Mock<IEntityAuthorizationMetadata>();
        
        entityMetadataMock.Setup(m => m.StateAllowedScript)
            .Returns(null as string);
        
        // Act
        var rightsChecker = new JavascriptEntityRightsChecker();
        
        var result = rightsChecker.StateAllowedAsync(
            expectedTenantId,
            entityMetadataMock.Object,
            expectedId,
            expectedCurrentState,
            ["edit", "view"]
        ).Result;
        
        // Assert
        Assert.That(result, Is.False);
    }
    
    [Test]
    public void StateAllowedAsync_ShouldReturnTrue_WhenUserHasDedicatedRight()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedId = Guid.NewGuid();
        var expectedCurrentState = 1;

        var stateAllowedScript = "return hasRight('entity.edit');";

        var entityMetadataMock = new Mock<IEntityAuthorizationMetadata>();
        
        entityMetadataMock.Setup(m => m.StateAllowedScript)
            .Returns(stateAllowedScript);
        
        // Act
        var rightsChecker = new JavascriptEntityRightsChecker();
        
        var result = rightsChecker.StateAllowedAsync(
            expectedTenantId,
            entityMetadataMock.Object,
            expectedId,
            expectedCurrentState,
            ["entity.edit", "entity.view"]
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void StateAllowedAsync_ShouldReturnTrue_WhenUserHasAnyRight()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedId = Guid.NewGuid();
        var expectedCurrentState = 1;

        var stateAllowedScript = "return hasAnyRight('entity');";

        var entityMetadataMock = new Mock<IEntityAuthorizationMetadata>();
        
        entityMetadataMock.Setup(m => m.StateAllowedScript)
            .Returns(stateAllowedScript);

        // Act
        var rightsChecker = new JavascriptEntityRightsChecker();
        
        var result = rightsChecker.StateAllowedAsync(
            expectedTenantId,
            entityMetadataMock.Object,
            expectedId,
            expectedCurrentState,
            ["entity.edit", "entity.view"]
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
}