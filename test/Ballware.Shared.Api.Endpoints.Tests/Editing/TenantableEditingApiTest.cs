using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Ballware.Shared.Api.Endpoints.Tests.Utils;
using Ballware.Shared.Api.Public;
using Ballware.Shared.Authorization;
using Ballware.Shared.Data.Public;
using Ballware.Shared.Data.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Quartz;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Ballware.Shared.Api.Endpoints.Tests.Editing;

public class FakeTenant : ITenantAuthorizationMetadata
{
    public Guid Id { get; set; }
    public string? RightsCheckScript { get; set; }
}

public class FakeEntity : IEntityAuthorizationMetadata
{
    public string Application { get; set; }
    public string Entity { get; set; }
    public string? RightsCheckScript { get; set; }
    public string? StateAllowedScript { get; set; }
}

public class TenantableFakeEntity : IEditable
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
}

[TestFixture]
public class TenantableEditingApiTest : ApiMappingBaseTest
{
    private string ExpectedApplication { get; } = "test";
    private string ExpectedEntity { get; } = "fakeentity";
    
    private Mock<ISchedulerFactory> SchedulerFactoryMock { get; set; } = null!;
    private Mock<IFileStorageProvider> StorageProviderMock { get; set; } = null!;
    private Mock<IPrincipalUtils> PrincipalUtilsMock { get; set; } = null!;
    private Mock<ITenantRightsChecker> TenantRightsCheckerMock { get; set; } = null!;
    private Mock<IAuthorizationMetadataProvider> AuthorizationMetaProviderMock { get; set; } = null!;
    private Mock<IEntityRightsChecker> EntityRightsCheckerMock { get; set; } = null!;
    private Mock<IJobMetadataProvider> JobProviderMock { get; set; } = null!;
    private Mock<IExportMetadataProvider> ExportProviderMock { get; set; } = null!;
    private Mock<ITenantableRepository<TenantableFakeEntity>> RepositoryMock { get; set; } = null!;
    
    private HttpClient Client { get; set; } = null!;
    
    [SetUp]
    public override async Task SetUp()
    {
        await base.SetUp();
        
        SchedulerFactoryMock = new Mock<ISchedulerFactory>();
        StorageProviderMock = new Mock<IFileStorageProvider>();
        PrincipalUtilsMock = new Mock<IPrincipalUtils>();
        TenantRightsCheckerMock = new Mock<ITenantRightsChecker>();
        AuthorizationMetaProviderMock = new Mock<IAuthorizationMetadataProvider>();
        EntityRightsCheckerMock = new Mock<IEntityRightsChecker>();
        JobProviderMock = new Mock<IJobMetadataProvider>();
        ExportProviderMock = new Mock<IExportMetadataProvider>();
        RepositoryMock = new Mock<ITenantableRepository<TenantableFakeEntity>>();
        
        Client = await CreateApplicationClientAsync("metaApi", services =>
        {
            services.AddSingleton(SchedulerFactoryMock.Object);
            services.AddSingleton(StorageProviderMock.Object);
            services.AddSingleton(PrincipalUtilsMock.Object);
            services.AddSingleton(TenantRightsCheckerMock.Object);
            services.AddSingleton(AuthorizationMetaProviderMock.Object);
            services.AddSingleton(EntityRightsCheckerMock.Object);
            services.AddSingleton(JobProviderMock.Object);
            services.AddSingleton(ExportProviderMock.Object);
            services.AddSingleton(RepositoryMock.Object);
            services.AddSingleton<EditingEndpointBuilderFactory>();
        }, app =>
        {
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapTenantableEditingApi<TenantableFakeEntity>("fakeentity", ExpectedApplication, ExpectedEntity, "FakeEntity", "FakeEntity");
            });
        });
    }
    
    [Test]
    public async Task HandleAll_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), "view", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.AllAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedList);
        
        // Act
        var response = await Client.GetAsync($"fakeentity/all?identifier=primary");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<IEnumerable<TenantableFakeEntity>>(await response.Content.ReadAsStringAsync());

        Assert.That(DeepComparer.AreListsEqual(expectedList, result, TestContext.WriteLine));
    }
    
    [Test]
    public async Task HandleAll_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());

        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        // Act
        var notFoundResponse = await Client.GetAsync($"fakeentity/all?identifier=primary");
        
        // Assert
        Assert.That(notFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleAll_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);

        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(false);
        
        // Act
        var unauthorizedResponse = await Client.GetAsync($"fakeentity/all?identifier=primary");
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleQuery_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "view", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.QueryAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedList)
            .Callback((Guid tenantId, string identifier, IDictionary<string, object> claims, IDictionary<string, object> query) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(query.Count, Is.EqualTo(3));
                    Assert.That(query["identifier"], Is.EqualTo("primary"));
                    Assert.That(query["param1"], Is.EqualTo("value1"));
                    Assert.That(query["param2"], Is.EqualTo("|value2|value3|"));
                });
            });
        
        // Act
        var response = await Client.GetAsync($"fakeentity/query?identifier=primary&param1=value1&param2=value2&param2=value3");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<IEnumerable<TenantableFakeEntity>>(await response.Content.ReadAsStringAsync());

        Assert.That(DeepComparer.AreListsEqual(expectedList, result, TestContext.WriteLine));
    }
    
    [Test]
    public async Task HandleQuery_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());

        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        // Act
        var notFoundResponse = await Client.GetAsync($"fakeentity/query?identifier=primary&param1=value1&param2=value2&param2=value3");
        
        // Assert
        Assert.That(notFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleQuery_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);

        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(false);
        
        // Act
        var unauthorizedResponse = await Client.GetAsync($"fakeentity/query?identifier=primary&param1=value1&param2=value2&param2=value3");
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleNew_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "add"))
            .ReturnsAsync(true);

        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "add", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.NewAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedEntry);
        
        // Act
        var response = await Client.GetAsync($"fakeentity/new?identifier=primary");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<TenantableFakeEntity>(await response.Content.ReadAsStringAsync());

        Assert.That(DeepComparer.AreEqual(expectedEntry, result, TestContext.WriteLine));
    }
    
    [Test]
    public async Task HandleNew_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());

        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "add"))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.NewAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedEntry);
        
        // Act
        var notFoundResponse = await Client.GetAsync($"fakeentity/new?identifier=primary");
        
        // Assert
        Assert.That(notFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleNew_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "add"))
            .ReturnsAsync(false);

        RepositoryMock
            .Setup(r => r.NewAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedEntry);
        
        // Act
        var unauthorizedResponse = await Client.GetAsync($"fakeentity/new?identifier=primary");
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleById_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };

        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), "view", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(expectedEntry);
        
        // Act
        var response = await Client.GetAsync($"fakeentity/byid?identifier=primary&id={expectedEntry.Id}");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<TenantableFakeEntity>(await response.Content.ReadAsStringAsync());

        Assert.That(DeepComparer.AreEqual(expectedEntry, result, TestContext.WriteLine));
    }
    
    [Test]
    public async Task HandleById_tenant_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };

        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(expectedEntry);
        
        // Act
        var tenantNotFoundResponse = await Client.GetAsync($"fakeentity/byid?identifier=primary&id={expectedEntry.Id}");
        
        // Assert
        Assert.That(tenantNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleById_record_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };

        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(null as TenantableFakeEntity);
        
        // Act
        var recordNotFoundResponse = await Client.GetAsync($"fakeentity/byid?identifier=primary&id={Guid.NewGuid()}");
        
        // Assert
        Assert.That(recordNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleById_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };

        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "view"))
            .ReturnsAsync(false);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(expectedEntry);
        
        // Act
        var unauthorizedResponse = await Client.GetAsync($"fakeentity/byid?identifier=primary&id={expectedEntry.Id}");
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleSave_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "edit"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "edit", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.SaveAsync(expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry))
            .Callback((Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TenantableFakeEntity entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(DeepComparer.AreEqual(expectedEntry, entry, TestContext.WriteLine), Is.True);    
                });
            });
        
        // Act
        var response = await Client.PostAsync($"fakeentity/save?identifier=primary", JsonContent.Create(expectedEntry));
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        RepositoryMock.Verify(r => r.SaveAsync(
            expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), It.IsAny<TenantableFakeEntity>()), Times.Once);
    }
    
    [Test]
    public async Task HandleSave_tenant_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "edit"))
            .ReturnsAsync(true);

        RepositoryMock
            .Setup(r => r.SaveAsync(expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry))
            .Callback((Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TenantableFakeEntity entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(DeepComparer.AreEqual(expectedEntry, entry, TestContext.WriteLine), Is.True);    
                });
            });
        
        // Act
        var tenantNotFoundResponse = await Client.PostAsync($"fakeentity/save?identifier=primary", JsonContent.Create(expectedEntry));
        
        // Assert
        Assert.That(tenantNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleSave_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "edit"))
            .ReturnsAsync(false);

        RepositoryMock
            .Setup(r => r.SaveAsync(expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry))
            .Callback((Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TenantableFakeEntity entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(DeepComparer.AreEqual(expectedEntry, entry, TestContext.WriteLine), Is.True);    
                });
            });
        
        // Act
        var unauthorizedResponse = await Client.PostAsync($"fakeentity/save?identifier=primary", JsonContent.Create(expectedEntry));
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task HandleSaveBatch_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntries = new List<TenantableFakeEntity>
        {
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            },
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "edit"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "edit", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.SaveAsync(expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntries[0]))
            .Callback((Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TenantableFakeEntity entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(DeepComparer.AreEqual(expectedEntries[0], entry, TestContext.WriteLine), Is.True);    
                });
            });
        
        RepositoryMock
            .Setup(r => r.SaveAsync(expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntries[1]))
            .Callback((Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TenantableFakeEntity entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(DeepComparer.AreEqual(expectedEntries[1], entry, TestContext.WriteLine), Is.True);    
                });
            });
        
        RepositoryMock
            .Setup(r => r.SaveAsync(expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntries[2]))
            .Callback((Guid tenantId, Guid? userId, string identifier, IDictionary<string, object> claims, TenantableFakeEntity entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(identifier, Is.EqualTo("primary"));
                    Assert.That(DeepComparer.AreEqual(expectedEntries[2], entry, TestContext.WriteLine), Is.True);    
                });
            });
        
        // Act
        var response = await Client.PostAsync($"fakeentity/savebatch?identifier=primary", JsonContent.Create(expectedEntries));
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        RepositoryMock.Verify(r => r.SaveAsync(
            expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), It.IsAny<TenantableFakeEntity>()), Times.Exactly(3));
    }
    
    [Test]
    public async Task HandleSaveBatch_tenant_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntries = new List<TenantableFakeEntity>
        {
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            },
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "edit"))
            .ReturnsAsync(true);
        
        // Act
        var tenantNotFoundResponse = await Client.PostAsync($"fakeentity/savebatch?identifier=primary", JsonContent.Create(expectedEntries));
        
        // Assert
        Assert.That(tenantNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        
        RepositoryMock.Verify(r => r.SaveAsync(
            expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), It.IsAny<TenantableFakeEntity>()), Times.Never);
    }
    
    [Test]
    public async Task HandleSaveBatch_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntries = new List<TenantableFakeEntity>
        {
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new TenantableFakeEntity()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            },
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "edit"))
            .ReturnsAsync(false);
        
        // Act
        var unauthorizedResponse = await Client.PostAsync($"fakeentity/savebatch?identifier=primary", JsonContent.Create(expectedEntries));
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
        
        RepositoryMock.Verify(r => r.SaveAsync(
            expectedTenantId, expectedUserId, "primary", It.IsAny<IDictionary<string, object>>(), It.IsAny<TenantableFakeEntity>()), Times.Never);
    }
    
    [Test]
    public async Task HandleRemove_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "delete"))
            .ReturnsAsync(true);

        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "delete", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(expectedEntry);
        
        RepositoryMock
            .Setup(r => r.RemoveAsync(expectedTenantId, expectedUserId, It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(new RemoveResult<TenantableFakeEntity>()
            {
                Result = true
            })
            .Callback((Guid tenantId, Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(removeParams, Contains.Key("Id"));
                    Assert.That(removeParams["Id"], Is.EqualTo(expectedEntry.Id));
                });
            });
        
        // Act
        var response = await Client.DeleteAsync($"fakeentity/remove/{expectedEntry.Id}");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        RepositoryMock.Verify(r => r.RemoveAsync(
            expectedTenantId, expectedUserId, It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()), Times.Once);
    }
    
    [Test]
    public async Task HandleRemove_tenant_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "delete"))
            .ReturnsAsync(true);

        RepositoryMock
            .Setup(r => r.RemoveAsync(expectedTenantId, expectedUserId, It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(new RemoveResult<TenantableFakeEntity>()
            {
                Result = true
            })
            .Callback((Guid tenantId, Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(removeParams, Contains.Key("Id"));
                    Assert.That(removeParams["Id"], Is.EqualTo(expectedEntry.Id));
                });
            });
        
        // Act
        var tenantNotFoundResponse = await Client.DeleteAsync($"fakeentity/remove/{expectedEntry.Id}");
        
        // Assert
        Assert.That(tenantNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleRemove_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "delete"))
            .ReturnsAsync(false);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "delete", It.IsAny<object>(), false))
            .ReturnsAsync(false);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(expectedEntry);

        RepositoryMock
            .Setup(r => r.RemoveAsync(expectedTenantId, expectedUserId, It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(new RemoveResult<TenantableFakeEntity>()
            {
                Result = true
            })
            .Callback((Guid tenantId, Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(removeParams, Contains.Key("Id"));
                    Assert.That(removeParams["Id"], Is.EqualTo(expectedEntry.Id));
                });
            });
        
        // Act
        var unauthorizedResponse = await Client.DeleteAsync($"fakeentity/remove/{expectedEntry.Id}");
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleRemove_preliminary_check_declined()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedEntry = new TenantableFakeEntity()
        {
            Id = Guid.NewGuid(),
            Name = "Name 1"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);

        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });

        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "delete"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "delete", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.ByIdAsync(expectedTenantId, "primary", It.IsAny<IDictionary<string, object>>(), expectedEntry.Id))
            .ReturnsAsync(expectedEntry);

        RepositoryMock
            .Setup(r => r.RemoveAsync(expectedTenantId, expectedUserId, It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(new RemoveResult<TenantableFakeEntity>()
            {
                Messages = ["An error occurred while trying to remove the entry."],
                Result = false
            })
            .Callback((Guid tenantId, Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(removeParams, Contains.Key("Id"));
                    Assert.That(removeParams["Id"], Is.EqualTo(expectedEntry.Id));
                });
            });
        
        // Act
        var badRequestResponse = await Client.DeleteAsync($"fakeentity/remove/{expectedEntry.Id}");
        
        // Assert
        Assert.That(badRequestResponse.StatusCode,Is.EqualTo(HttpStatusCode.BadRequest));
    }
    
    [Test]
    public async Task HandleExport_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };

        var expectedResult = new ExportResult()
        {
            FileName = "export.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expectedList)),
            MediaType = "application/json"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "export"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "export", It.IsAny<object>(), true))
            .ReturnsAsync(true);

        
        RepositoryMock
            .Setup(r => r.ExportAsync(expectedTenantId, "export", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedResult);
        
        // Act
        var response = await Client.GetAsync($"fakeentity/export?identifier=export{string.Join("", expectedList.Select(c => $"&Id={c.Id}"))}");
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<IEnumerable<TenantableFakeEntity>>(await response.Content.ReadAsStringAsync());

        Assert.That(DeepComparer.AreListsEqual(expectedList, result, TestContext.WriteLine));
    }
    
    [Test]
    public async Task HandleExport_tenant_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };

        var expectedResult = new ExportResult()
        {
            FileName = "export.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expectedList)),
            MediaType = "application/json"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "export"))
            .ReturnsAsync(true);
        
        RepositoryMock
            .Setup(r => r.ExportAsync(expectedTenantId, "export", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedResult);
        
        // Act
        var notFoundResponse = await Client.GetAsync($"fakeentity/export?identifier=export");
        
        // Assert
        Assert.That(notFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleExport_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };

        var expectedResult = new ExportResult()
        {
            FileName = "export.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expectedList)),
            MediaType = "application/json"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "export"))
            .ReturnsAsync(false);
        
        RepositoryMock
            .Setup(r => r.ExportAsync(expectedTenantId, "export", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedResult);
        
        // Act
        var unauthorizedResponse = await Client.GetAsync($"fakeentity/export?identifier=export");
        
        // Assert
        Assert.That(unauthorizedResponse.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleExportToUrl_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        var expectedExportId = Guid.NewGuid();

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };

        var expectedResult = new ExportResult()
        {
            FileName = "export.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expectedList)),
            MediaType = "application/json"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "export"))
            .ReturnsAsync(true);
        
        EntityRightsCheckerMock
            .Setup(c => c.HasRightAsync(expectedTenantId, It.IsAny<IEntityAuthorizationMetadata>(), It.IsAny<IDictionary<string, object>>(), 
                "export", It.IsAny<object>(), true))
            .ReturnsAsync(true);
        
        ExportProviderMock
            .Setup(r => r.CreateExportAsync(expectedTenantId, expectedUserId, It.IsAny<Public.Export>()))
            .Callback((Guid tenantId, Guid userId, Public.Export entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(entry.Entity, Is.EqualTo(expectedEntity));
                });
            })
            .ReturnsAsync(expectedExportId);
        
        RepositoryMock
            .Setup(r => r.ExportAsync(expectedTenantId, "export", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedResult);
        
        // Act
        var response = await Client.PostAsync($"fakeentity/exporturl?identifier=export", new FormUrlEncodedContent(
            expectedList.Select(item => new KeyValuePair<string, string>("Id", item.Id.ToString()))));
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
        
        var result = JsonSerializer.Deserialize<ExportUrlResult>(await response.Content.ReadAsStringAsync());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result?.TenantId, Is.EqualTo(expectedTenantId));
            Assert.That(result?.Id, Is.EqualTo(expectedExportId));
        });
        
        ExportProviderMock
            .Verify(r => r.CreateExportAsync(expectedTenantId, expectedUserId, It.IsAny<Public.Export>()), 
                Times.Once);
    }
    
    [Test]
    public async Task HandleExportToUrl_tenant_not_found()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        var expectedExportId = Guid.NewGuid();

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };

        var expectedResult = new ExportResult()
        {
            FileName = "export.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expectedList)),
            MediaType = "application/json"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(Guid.NewGuid());
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "export"))
            .ReturnsAsync(true);
        
        ExportProviderMock
            .Setup(r => r.CreateExportAsync(expectedTenantId, expectedUserId, It.IsAny<Public.Export>()))
            .Callback((Guid tenantId, Guid userId, Public.Export entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(entry.Entity, Is.EqualTo(expectedEntity));
                });
            })
            .ReturnsAsync(expectedExportId);
        
        RepositoryMock
            .Setup(r => r.ExportAsync(expectedTenantId, "export", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedResult);
        
        // Act
        var response = await Client.PostAsync($"fakeentity/exporturl?identifier=export", new FormUrlEncodedContent(
            expectedList.Select(item => new KeyValuePair<string, string>("Id", item.Id.ToString()))));
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
    }
    
    [Test]
    public async Task HandleExportToUrl_not_authorized()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        var expectedExportId = Guid.NewGuid();

        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };

        var expectedResult = new ExportResult()
        {
            FileName = "export.json",
            Data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(expectedList)),
            MediaType = "application/json"
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForEntityByTenantAndIdentifierAsync(expectedTenantId, expectedEntity))
            .ReturnsAsync(new FakeEntity()
            {
                Entity = expectedEntity
            });
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "export"))
            .ReturnsAsync(false);
        
        ExportProviderMock
            .Setup(r => r.CreateExportAsync(expectedTenantId, expectedUserId, It.IsAny<Public.Export>()))
            .Callback((Guid tenantId, Guid userId, Public.Export entry) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(entry.Entity, Is.EqualTo(expectedEntity));
                });
            })
            .ReturnsAsync(expectedExportId);
        
        RepositoryMock
            .Setup(r => r.ExportAsync(expectedTenantId, "export", It.IsAny<IDictionary<string, object>>(), It.IsAny<IDictionary<string, object>>()))
            .ReturnsAsync(expectedResult);
        
        // Act
        var response = await Client.PostAsync($"fakeentity/exporturl?identifier=export", new FormUrlEncodedContent(
            expectedList.Select(item => new KeyValuePair<string, string>("Id", item.Id.ToString()))));
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    
    [Test]
    public async Task HandleImport_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();
        var expectedTemporaryId = Guid.NewGuid();
        var expectedJobId = Guid.NewGuid();
        var expectedApplication = "test";
        var expectedEntity = "fakeentity";
        
        var expectedList = new List<TenantableFakeEntity>()
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 1"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 2"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Name 3"
            }
        };
        
        var fakeTenant = new FakeTenant()
        {
            Id = expectedTenantId,
        };
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserTenandId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedTenantId);
        
        PrincipalUtilsMock
            .Setup(p => p.GetUserId(It.IsAny<ClaimsPrincipal>()))
            .Returns(expectedUserId);
        
        AuthorizationMetaProviderMock
            .Setup(r => r.MetadataForTenantByIdAsync(expectedTenantId))
            .ReturnsAsync(fakeTenant);
        
        TenantRightsCheckerMock
            .Setup(c => c.HasRightAsync(fakeTenant, expectedApplication, expectedEntity,
                It.IsAny<Dictionary<string, object>>(), "import"))
            .ReturnsAsync(true);

        StorageProviderMock
            .Setup(s => s.UploadTemporaryFileBehalfOfUserAsync(expectedTenantId, expectedUserId, expectedTemporaryId, "import.json", "application/json",
                It.IsAny<Stream>()))
            .Callback((Guid tenantId, Guid userId, Guid temporaryId, string fileName, string mediaType, Stream stream) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(tenantId, Is.EqualTo(expectedTenantId));
                    Assert.That(userId, Is.EqualTo(expectedUserId));
                    Assert.That(temporaryId, Is.EqualTo(expectedTemporaryId));
                    Assert.That(fileName, Is.EqualTo("import.json"));
                    Assert.That(mediaType, Is.EqualTo("application/json"));
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();
                    var importedList = JsonSerializer.Deserialize<List<TenantableFakeEntity>>(content);
                    Assert.That(DeepComparer.AreListsEqual(expectedList, importedList, TestContext.WriteLine));
                });
            });

        JobProviderMock
            .Setup(r => r.CreateJobAsync(expectedTenantId, expectedUserId, "meta", "import", It.IsAny<string>()))
            .ReturnsAsync(expectedJobId);
        
        var schedulerMock = new Mock<IScheduler>();

        schedulerMock
            .Setup(s => s.TriggerJob(It.IsAny<JobKey>(), It.IsAny<JobDataMap>(), It.IsAny<CancellationToken>()))
            .Callback((JobKey jobKey, JobDataMap jobData, CancellationToken cancellationToken) =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(jobKey, Is.EqualTo(JobKey.Create("import", "fakeentity")));
                    Assert.That(cancellationToken, Is.EqualTo(CancellationToken.None));
                    Assert.That(jobData, Is.Not.Null);
                    Assert.That(jobData["tenantId"], Is.EqualTo(expectedTenantId));
                    Assert.That(jobData["userId"], Is.EqualTo(expectedUserId));
                    Assert.That(jobData["identifier"], Is.EqualTo("import"));
                });
            });
        
        SchedulerFactoryMock
            .Setup(s => s.GetScheduler(CancellationToken.None))
            .ReturnsAsync(schedulerMock.Object);
        
        // Act
        var payload = new MultipartFormDataContent();
        
        payload.Add(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(expectedList)))), "files", "import.json");
        
        var response = await Client.PostAsync($"fakeentity/import?identifier=import", payload);
        
        // Assert
        Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.Created));
    }
    
    [Test]
    public async Task HandleDownloadExport_succeeds()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var expectedExportId = Guid.NewGuid();
        var expectedMediaType = "application/json";
        var expectedFilePayload = Encoding.UTF8.GetBytes("{ \"key\": \"value\" }");
        
        ExportProviderMock
            .Setup(r => r.GetExportByIdAsync(expectedTenantId, expectedExportId))
            .ReturnsAsync(new Public.Export()
            {
                Id = expectedExportId,
                Application = ExpectedApplication,
                Entity = ExpectedEntity,
                ExpirationStamp = DateTime.Now.AddDays(1),
                MediaType = expectedMediaType,
                Query = "primary"
            });
        
        StorageProviderMock
            .Setup(s => s.TemporaryFileByIdAsync(expectedTenantId, expectedExportId))
            .ReturnsAsync(new MemoryStream(expectedFilePayload));
        
        // Act
        var response = await Client.GetAsync($"fakeentity/download/{expectedTenantId}/{expectedExportId}");
        
        // Assert
        Assert.MultipleAsync(async () =>
        {
            Assert.That(response.StatusCode,Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(expectedMediaType));
            Assert.That(response.Content.Headers.ContentDisposition, Is.Not.Null);
            Assert.That(response.Content.Headers.ContentDisposition!.FileName, Is.Not.Null);
            
            var payload = await response.Content.ReadAsByteArrayAsync();
            
            Assert.That(payload, Is.EqualTo(expectedFilePayload));
        });

        ExportProviderMock
            .Verify(r => r.GetExportByIdAsync(expectedTenantId, expectedExportId), 
                Times.Once);
        
        StorageProviderMock
            .Verify(r => r.TemporaryFileByIdAsync(expectedTenantId, expectedExportId), 
                Times.Once);
    }
    
    [Test]
    public async Task HandleDownloadExport_notFound()
    {
        // Arrange
        var expectedTenantId = Guid.NewGuid();
        var exportNotFoundExportId = Guid.NewGuid();
        var fileNotFoundExportId = Guid.NewGuid();
        
        ExportProviderMock
            .Setup(r => r.GetExportByIdAsync(expectedTenantId, fileNotFoundExportId))
            .ReturnsAsync(new Public.Export()
            {
                Id = fileNotFoundExportId,
                Application = ExpectedApplication,
                Entity = ExpectedEntity,
                ExpirationStamp = DateTime.Now.AddDays(1),
                MediaType = "application/json",
                Query = "primary"
            });
        
        // Act
        var exportNotFoundResponse = await Client.GetAsync($"fakeentity/download/{expectedTenantId}/{exportNotFoundExportId}");
        var fileNotFoundResponse = await Client.GetAsync($"fakeentity/download/{expectedTenantId}/{fileNotFoundExportId}");
        
        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exportNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(fileNotFoundResponse.StatusCode,Is.EqualTo(HttpStatusCode.NotFound));
        });

        ExportProviderMock
            .Verify(r => r.GetExportByIdAsync(expectedTenantId, exportNotFoundExportId), 
                Times.Once);
        
        ExportProviderMock
            .Verify(r => r.GetExportByIdAsync(expectedTenantId, fileNotFoundExportId), 
                Times.Once);
        
        StorageProviderMock
            .Verify(r => r.TemporaryFileByIdAsync(expectedTenantId, fileNotFoundExportId), 
                Times.Once);
    }
}