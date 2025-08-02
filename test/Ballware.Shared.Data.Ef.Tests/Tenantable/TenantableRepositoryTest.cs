using System.Collections.Immutable;
using System.Text;
using AutoMapper;
using Ballware.Shared.Data.Ef.Repository;
using Ballware.Shared.Data.Persistables;
using Ballware.Shared.Data.Public;
using Ballware.Shared.Data.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Ballware.Shared.Data.Ef.Tests.Tenantable;

public class PublicEntity : IEditable
{
    public Guid Id { get; set; }
    
    public string? StringProperty { get; set; }
    public int IntProperty { get; set; }
}

public class PersistedEntity : IEntity, ITenantable, IAuditable
{
    public long? Id { get; set; }
    public Guid Uuid { get; set; }
    public Guid TenantId { get; set; }
    
    public string? StringProperty { get; set; }
    public int IntProperty { get; set; }
    public Guid? CreatorId { get; set; }
    public DateTime? CreateStamp { get; set; }
    public Guid? LastChangerId { get; set; }
    public DateTime? LastChangeStamp { get; set; }
}

class TestDbContext : DbContext, IDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<PersistedEntity>().HasKey(d => d.Id);
        modelBuilder.Entity<PersistedEntity>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
    }
}

class FakeRepositoryHook : ITenantableRepositoryHook<PublicEntity, PersistedEntity>
{
    
}

class DeclineRemoveRepository : TenantableBaseRepository<PublicEntity, PersistedEntity>
{
    public DeclineRemoveRepository(IMapper mapper, IDbContext context,
        ITenantableRepositoryHook<PublicEntity, PersistedEntity>? hook) : base(mapper, context, hook)
    {
    }

    protected override Task<RemoveResult<PublicEntity>> RemovePreliminaryCheckAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims, IDictionary<string, object> removeParams,
        PublicEntity? removeValue)
    {
        return Task.FromResult(new RemoveResult<PublicEntity>()
        {
           Result = false,
           Messages = ["Remove declined"]
        });
    }
}

public class TenantableRepositoryTest
{
    private IMapper Mapper { get; set; }
    private TestDbContext DbContext { get; set; }
    
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Mapper = new MapperConfiguration(cfg =>
        {
            cfg.CreateMap<PersistedEntity, PublicEntity>()
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Uuid));
            
            cfg.CreateMap<PublicEntity, PersistedEntity>()
                .ForMember(dest => dest.Id, opt => opt.Ignore())
                .ForMember(dest => dest.Uuid, opt => opt.MapFrom(src => src.Id));
            
        }).CreateMapper();
    }

    [SetUp]
    public void SetUp()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        DbContext = new TestDbContext(options);
        DbContext.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        DbContext.Dispose();
    }
    
    [Test]
    public async Task Save_and_remove_value_succeeds()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new TenantableBaseRepository<PublicEntity, PersistedEntity>(Mapper, DbContext, null);

        var expectedValue = await repository.NewQueryAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        expectedValue.StringProperty = "fake_string";
        expectedValue.IntProperty = 10;
        
        await repository.SaveAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, expectedValue);

        var actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.Multiple(() =>
        {
            Assert.That(actualValue, Is.Not.Null);
            Assert.That(actualValue?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualValue?.StringProperty, Is.EqualTo(expectedValue.StringProperty));
            Assert.That(actualValue?.IntProperty, Is.EqualTo(expectedValue.IntProperty));
        });

        actualValue.IntProperty = 22;
        
        await repository.SaveAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, actualValue);

        actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.Multiple(() =>
        {
            Assert.That(actualValue, Is.Not.Null);
            Assert.That(actualValue?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualValue?.StringProperty, Is.EqualTo(expectedValue.StringProperty));
            Assert.That(actualValue?.IntProperty, Is.EqualTo(22));
        });
        
        var removeParams = new Dictionary<string, object>([new KeyValuePair<string, object>("Id", expectedValue.Id)]);

        var removeResult = await repository.RemoveAsync(expectedTenantId, null, ImmutableDictionary<string, object>.Empty, removeParams);

        Assert.Multiple(() =>
        {
            Assert.That(removeResult.Result, Is.True);
            Assert.That(removeResult.Messages, Is.Empty);
            Assert.That(removeResult.Value, Is.Not.Null);
        });

        actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.That(actualValue, Is.Null);
    }
    
    [Test]
    public async Task Save_and_remove_value_declined()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new DeclineRemoveRepository(Mapper, DbContext, null);

        var expectedValue = await repository.NewQueryAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        expectedValue.StringProperty = "fake_string";
        expectedValue.IntProperty = 10;
        
        await repository.SaveAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, expectedValue);

        var actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.Multiple(() =>
        {
            Assert.That(actualValue, Is.Not.Null);
            Assert.That(actualValue?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualValue?.StringProperty, Is.EqualTo(expectedValue.StringProperty));
            Assert.That(actualValue?.IntProperty, Is.EqualTo(expectedValue.IntProperty));
        });

        var removeParams = new Dictionary<string, object>([new KeyValuePair<string, object>("Id", expectedValue.Id)]);

        var removeResult = await repository.RemoveAsync(expectedTenantId, null, ImmutableDictionary<string, object>.Empty, removeParams);

        Assert.Multiple(() =>
        {
            Assert.That(removeResult.Result, Is.False);
            Assert.That(removeResult.Messages, Is.EqualTo(["Remove declined"]));
        });

        actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.That(actualValue, Is.Not.Null);
    }
    
    [Test]
    public async Task Save_and_remove_value_with_hook_succeeds()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new TenantableBaseRepository<PublicEntity, PersistedEntity>(Mapper, DbContext, new FakeRepositoryHook());

        var expectedValue = await repository.NewQueryAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        expectedValue.StringProperty = "fake_string";
        expectedValue.IntProperty = 10;
        
        await repository.SaveAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, expectedValue);

        var actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.Multiple(() =>
        {
            Assert.That(actualValue, Is.Not.Null);
            Assert.That(actualValue?.Id, Is.EqualTo(expectedValue.Id));
            Assert.That(actualValue?.StringProperty, Is.EqualTo(expectedValue.StringProperty));
            Assert.That(actualValue?.IntProperty, Is.EqualTo(expectedValue.IntProperty));
        });

        var removeParams = new Dictionary<string, object>([new KeyValuePair<string, object>("Id", expectedValue.Id)]);

        var removeResult = await repository.RemoveAsync(expectedTenantId, null, ImmutableDictionary<string, object>.Empty, removeParams);

        Assert.Multiple(() =>
        {
            Assert.That(removeResult.Result, Is.True);
        });

        actualValue = await repository.ByIdAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, expectedValue.Id);

        Assert.That(actualValue, Is.Null);
    }

    [Test]
    public async Task Query_items_succeeds()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new TenantableBaseRepository<PublicEntity, PersistedEntity>(Mapper, DbContext, null);

        var fakeTenantIds = new[] { Guid.NewGuid(), Guid.NewGuid(), expectedTenantId, Guid.NewGuid() };
        
        foreach (var fakeTenant in fakeTenantIds)
        {
            for (var i = 0; i < 10; i++)
            {
                var fakeValue = await repository.NewAsync(fakeTenant, "primary", ImmutableDictionary<string, object>.Empty);

                fakeValue.StringProperty = $"fake_string_{fakeTenant.ToString()}_{i}";
                fakeValue.IntProperty = i;
                
                await repository.SaveAsync(fakeTenant, null, "primary", ImmutableDictionary<string, object>.Empty, fakeValue);
            }
        }

        var actualTenantItemsCount = await repository.CountAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        var actualTenantAllItems = await repository.AllAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty);
        var actualTenantQueryItems = await repository.QueryAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        Assert.Multiple(() =>
        {
            Assert.That(actualTenantItemsCount, Is.EqualTo(10));
            Assert.That(actualTenantAllItems.Count(), Is.EqualTo(10));
            Assert.That(actualTenantQueryItems.Count(), Is.EqualTo(10));
        });
    }

    [Test]
    public async Task Import_values_succeeds()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new TenantableBaseRepository<PublicEntity, PersistedEntity>(Mapper, DbContext, null);

        var importList = new List<PublicEntity>();

        for (var i = 0; i < 10; i++)
        {
            var fakeValue = await repository.NewAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty);

            fakeValue.StringProperty = $"fake_string_{expectedTenantId.ToString()}_{i}";
            fakeValue.IntProperty = i;

            importList.Add(fakeValue);
        }

        var importBinary = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(importList));

        using var importStream = new MemoryStream(importBinary);

        await repository.ImportAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, importStream, (_) => Task.FromResult(true));

        var actualTenantItemsCount = await repository.CountAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        var actualTenantAllItems = await repository.AllAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty);
        var actualTenantQueryItems = await repository.QueryAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        Assert.Multiple(() =>
        {
            Assert.That(actualTenantItemsCount, Is.EqualTo(10));
            Assert.That(actualTenantAllItems.Count(), Is.EqualTo(10));
            Assert.That(actualTenantQueryItems.Count(), Is.EqualTo(10));
        });
    }
    
    [Test]
    public async Task Import_empty_succeeds()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new TenantableBaseRepository<PublicEntity, PersistedEntity>(Mapper, DbContext, null);

        await repository.ImportAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, new MemoryStream(), (_) => Task.FromResult(true));

        var actualTenantItemsCount = await repository.CountAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        var actualTenantAllItems = await repository.AllAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty);
        var actualTenantQueryItems = await repository.QueryAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty, ImmutableDictionary<string, object>.Empty);
        
        Assert.Multiple(() =>
        {
            Assert.That(actualTenantItemsCount, Is.EqualTo(0));
            Assert.That(actualTenantAllItems.Count(), Is.EqualTo(0));
            Assert.That(actualTenantQueryItems.Count(), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Export_values_succeeds()
    {
        var expectedTenantId = Guid.NewGuid();
        
        var repository = new TenantableBaseRepository<PublicEntity, PersistedEntity>(Mapper, DbContext, null);

        var exportIdList = new List<Guid>();
        var exportItemList = new List<PublicEntity>();

        for (var i = 0; i < 10; i++)
        {
            var fakeValue = await repository.NewAsync(expectedTenantId, "primary", ImmutableDictionary<string, object>.Empty);

            fakeValue.StringProperty = $"fake_string_{expectedTenantId}_{i}";
            fakeValue.IntProperty = i;

            await repository.SaveAsync(expectedTenantId, null, "primary", ImmutableDictionary<string, object>.Empty, fakeValue);

            if (i % 2 == 0)
            {
                exportIdList.Add(fakeValue.Id);
                exportItemList.Add(fakeValue);
            }
        }

        var singleExportResult = await repository.ExportAsync(expectedTenantId, "exportjson", ImmutableDictionary<string, object>.Empty, 
            new Dictionary<string, object>(new [] { new KeyValuePair<string, object>("id", exportIdList[0]) }));

        var multipleExportResult = await repository.ExportAsync(expectedTenantId, "exportjson", ImmutableDictionary<string, object>.Empty, 
            new Dictionary<string, object>(new[] { new KeyValuePair<string, object>("id", exportIdList.Select(id => id.ToString()).ToArray()) }));
        
        Assert.Multiple(() =>
        {
            Assert.That(singleExportResult.FileName, Is.EqualTo("exportjson.json"));
            Assert.That(singleExportResult.MediaType, Is.EqualTo("application/json"));
            Assert.That(singleExportResult.Data, Is.Not.Null);
            
            Assert.That(multipleExportResult.FileName, Is.EqualTo("exportjson.json"));
            Assert.That(multipleExportResult.MediaType, Is.EqualTo("application/json"));
            Assert.That(multipleExportResult.Data, Is.Not.Null);

            using var singleInputStream = new MemoryStream(singleExportResult.Data);
            using var singleStreamReader = new StreamReader(singleInputStream);

            var actualSingleItems = JsonConvert.DeserializeObject<IEnumerable<PublicEntity>>(singleStreamReader.ReadToEnd())?.ToList();
            
            using var multipleInputStream = new MemoryStream(multipleExportResult.Data);
            using var multipleStreamReader = new StreamReader(multipleInputStream);

            var actualMultipleItems = JsonConvert.DeserializeObject<IEnumerable<PublicEntity>>(multipleStreamReader.ReadToEnd())?.ToList();

            Assert.That(actualSingleItems, Is.Not.Null);
            Assert.That(actualSingleItems?.Count, Is.EqualTo(1));
            Assert.That(actualSingleItems?.Select(item => item.Id), Is.EquivalentTo(exportItemList.Take(1).Select(item => item.Id)));
            
            Assert.That(actualMultipleItems, Is.Not.Null);
            Assert.That(actualMultipleItems?.Count, Is.EqualTo(5));
            Assert.That(actualMultipleItems?.Select(item => item.Id), Is.EquivalentTo(exportItemList.Select(item => item.Id)));
        });
    }
}