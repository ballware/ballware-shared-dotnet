using Ballware.Shared.Authorization.Jint.Internal;
using Moq;

namespace Ballware.Shared.Authorization.Jint.Tests;

[TestFixture]
public class JavascriptTenantRightsCheckerTest
{
    [Test]
    public void HasRightAsync_ShouldReturnTrue_WhenRightsCheckScriptIsEmpty()
    {
        // Arrange
        var tenantMock = new Mock<ITenantAuthorizationMetadata>();
        
        tenantMock
            .Setup(m => m.RightsCheckScript)
            .Returns((string?)null);
        
        // Act
        var rightsChecker = new JavascriptTenantRightsChecker();
        
        var result = rightsChecker.HasRightAsync(
            tenantMock.Object,
            "fakeapplication",
            "fakeentity",
            new Dictionary<string, object> { { "right", new string[] { "right1", "right2" } } },
            "edit"
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void HasRightAsync_ShouldReturnTrue_WhenScriptReturnsTrue()
    {
        // Arrange
        var tenantMock = new Mock<ITenantAuthorizationMetadata>();
        
        tenantMock
            .Setup(m => m.RightsCheckScript)
            .Returns("return true;");
        
        // Act
        var rightsChecker = new JavascriptTenantRightsChecker();
        
        var result = rightsChecker.HasRightAsync(
            tenantMock.Object,
            "fakeapplication",
            "fakeentity",
            new Dictionary<string, object> { { "right", new string[] { "right1", "right2" } } },
            "edit"
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
    
    [Test]
    public void HasRightAsync_ShouldReturnTrue_WhenUserinfoContainsRight()
    {
        // Arrange
        var tenantMock = new Mock<ITenantAuthorizationMetadata>();
        
        tenantMock
            .Setup(m => m.RightsCheckScript)
            .Returns("return userinfo.right.includes(right);");
        
        // Act
        var rightsChecker = new JavascriptTenantRightsChecker();
        
        var result = rightsChecker.HasRightAsync(
            tenantMock.Object,
            "fakeapplication",
            "fakeentity",
            new Dictionary<string, object> { { "right", new string[] { "right1", "fakeapplication.fakeentity.edit" } } },
            "edit"
        ).Result;
        
        // Assert
        Assert.That(result, Is.True);
    }
}