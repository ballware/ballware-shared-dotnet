using System.Security.Claims;
using Ballware.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Ballware.Shared.Mcp.Authorization.Tests;

[TestFixture]
public class McpAuthorizationHandlerTest
{
    private Mock<IServiceProvider> _serviceProviderMock;
    private Mock<IPrincipalUtils> _principalUtilsMock;
    private Mock<IAuthorizationMetadataProvider> _authorizationMetadataProviderMock;
    private Mock<ITenantRightsChecker> _tenantRightsCheckerMock;
    private Mock<IEntityRightsChecker> _entityRightsCheckerMock;
    private ClaimsPrincipal _principal;
    private Guid _tenantId;
    private string _application;
    private string _entity;
    private string _right;

    [SetUp]
    public void SetUp()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _principalUtilsMock = new Mock<IPrincipalUtils>();
        _authorizationMetadataProviderMock = new Mock<IAuthorizationMetadataProvider>();
        _tenantRightsCheckerMock = new Mock<ITenantRightsChecker>();
        _entityRightsCheckerMock = new Mock<IEntityRightsChecker>();
        _principal = new ClaimsPrincipal();
        _tenantId = Guid.NewGuid();
        _application = "TestApp";
        _entity = "TestEntity";
        _right = "TestRight";

        _serviceProviderMock.Setup(x => x.GetService(typeof(IPrincipalUtils))).Returns(_principalUtilsMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IAuthorizationMetadataProvider))).Returns(_authorizationMetadataProviderMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(ITenantRightsChecker))).Returns(_tenantRightsCheckerMock.Object);
        _serviceProviderMock.Setup(x => x.GetService(typeof(IEntityRightsChecker))).Returns(_entityRightsCheckerMock.Object);

        _principalUtilsMock.Setup(x => x.GetUserTenandId(_principal)).Returns(_tenantId);
        _principalUtilsMock.Setup(x => x.GetUserClaims(_principal)).Returns(new Dictionary<string, object>());
    }

    [Test]
    public async Task CreateStaticEntityRightAuthorizationHandler_Success()
    {
        // Arrange
        var tenantMetadataMock = new Mock<ITenantAuthorizationMetadata>();
        var entityMetadataMock = new Mock<IEntityAuthorizationMetadata>();

        _authorizationMetadataProviderMock.Setup(x => x.MetadataForTenantByIdAsync(_tenantId)).ReturnsAsync(tenantMetadataMock.Object);
        _authorizationMetadataProviderMock.Setup(x => x.MetadataForEntityByTenantAndIdentifierAsync(_tenantId, _entity)).ReturnsAsync(entityMetadataMock.Object);
        _tenantRightsCheckerMock.Setup(x => x.HasRightAsync(tenantMetadataMock.Object, _application, _entity, It.IsAny<Dictionary<string, object>>(), _right)).ReturnsAsync(true);
        _entityRightsCheckerMock.Setup(x => x.HasRightAsync(_tenantId, entityMetadataMock.Object, It.IsAny<Dictionary<string, object>>(), _right, null, true)).ReturnsAsync(true);

        var handler = McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(_application, _entity, _right);

        // Act
        var result = await handler(_serviceProviderMock.Object, _principal);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task CreateStaticEntityRightAuthorizationHandler_Fail_NoTenantMetadata()
    {
        // Arrange
        _authorizationMetadataProviderMock.Setup(x => x.MetadataForTenantByIdAsync(_tenantId)).ReturnsAsync((ITenantAuthorizationMetadata?)null);

        var handler = McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(_application, _entity, _right);

        // Act
        var result = await handler(_serviceProviderMock.Object, _principal);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateStaticEntityRightAuthorizationHandler_Fail_NoEntityMetadata()
    {
        // Arrange
        var tenantMetadataMock = new Mock<ITenantAuthorizationMetadata>();
        _authorizationMetadataProviderMock.Setup(x => x.MetadataForTenantByIdAsync(_tenantId)).ReturnsAsync(tenantMetadataMock.Object);
        _authorizationMetadataProviderMock.Setup(x => x.MetadataForEntityByTenantAndIdentifierAsync(_tenantId, _entity)).ReturnsAsync((IEntityAuthorizationMetadata?)null);

        var handler = McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(_application, _entity, _right);

        // Act
        var result = await handler(_serviceProviderMock.Object, _principal);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateStaticEntityRightAuthorizationHandler_Fail_NoRights()
    {
        // Arrange
        var tenantMetadataMock = new Mock<ITenantAuthorizationMetadata>();
        var entityMetadataMock = new Mock<IEntityAuthorizationMetadata>();

        _authorizationMetadataProviderMock.Setup(x => x.MetadataForTenantByIdAsync(_tenantId)).ReturnsAsync(tenantMetadataMock.Object);
        _authorizationMetadataProviderMock.Setup(x => x.MetadataForEntityByTenantAndIdentifierAsync(_tenantId, _entity)).ReturnsAsync(entityMetadataMock.Object);
        _tenantRightsCheckerMock.Setup(x => x.HasRightAsync(tenantMetadataMock.Object, _application, _entity, It.IsAny<Dictionary<string, object>>(), _right)).ReturnsAsync(false);
        _entityRightsCheckerMock.Setup(x => x.HasRightAsync(_tenantId, entityMetadataMock.Object, It.IsAny<Dictionary<string, object>>(), _right, null, false)).ReturnsAsync(false);

        var handler = McpAuthorizationHandlerFactory.CreateStaticEntityRightAuthorizationHandler(_application, _entity, _right);

        // Act
        var result = await handler(_serviceProviderMock.Object, _principal);

        // Assert
        Assert.That(result, Is.False);
    }
}

