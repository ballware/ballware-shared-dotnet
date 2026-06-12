using System.Security.Claims;
using Ballware.Shared.Authorization.Internal;

namespace Ballware.Shared.Authorization.Tests;

[TestFixture]
public class DefaultPrincipalUtilsTest
{
    [Test]
    public void Get_userid_from_principal_should_succeed()
    {
        var principalClaims = new List<Claim>();

        var expectedUserId = Guid.NewGuid();

        principalClaims.Add(new Claim("tenant", Guid.NewGuid().ToString()));
        principalClaims.Add(new Claim("non_relevant_claim_1", "fake value"));
        principalClaims.Add(new Claim("sub", expectedUserId.ToString()));
        principalClaims.Add(new Claim("non_relevant_claim_2", "fake value"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));

        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var actualUserId = subject.GetUserId(principal);

        Assert.That(actualUserId, Is.EqualTo(expectedUserId));
    }

    [Test]
    public void Get_tenant_from_principal_should_succeed()
    {
        var principalClaims = new List<Claim>();

        var expectedTenantId = Guid.NewGuid();

        principalClaims.Add(new Claim("tenant", expectedTenantId.ToString()));
        principalClaims.Add(new Claim("non_relevant_claim_1", "fake value"));
        principalClaims.Add(new Claim("sub", Guid.NewGuid().ToString()));
        principalClaims.Add(new Claim("non_relevant_claim_2", "fake value"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));

        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var actualTenantId = subject.GetUserTenandId(principal);

        Assert.That(actualTenantId, Is.EqualTo(expectedTenantId));
    }

    [Test]
    public void Get_tenant_ids_from_principal_should_succeed()
    {
        var expectedTenantIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var principalClaims = new List<Claim>
        {
            new("tenant", expectedTenantIds[0].ToString()),
            new("non_relevant_claim", Guid.NewGuid().ToString()),
            new("tenant", expectedTenantIds[1].ToString()),
            new("tenant", expectedTenantIds[2].ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));
        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var actualTenantIds = subject.GetUserTenantIds(principal);

        Assert.That(actualTenantIds, Is.EqualTo(expectedTenantIds));
    }

    [Test]
    public void Get_tenant_ids_from_principal_without_tenant_claims_should_return_empty_result()
    {
        var principalClaims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("non_relevant_claim", Guid.NewGuid().ToString())
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));
        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var actualTenantIds = subject.GetUserTenantIds(principal);

        Assert.That(actualTenantIds, Is.Empty);
    }

    [Test]
    public void Get_tenant_ids_from_principal_with_invalid_tenant_claim_should_throw()
    {
        var principalClaims = new List<Claim>
        {
            new("tenant", Guid.NewGuid().ToString()),
            new("tenant", "invalid tenant id")
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));
        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var exception = Assert.Throws<ArgumentException>(
            () => subject.GetUserTenantIds(principal).ToList());

        Assert.Multiple(() =>
        {
            Assert.That(exception?.ParamName, Is.EqualTo("TenantClaim"));
            Assert.That(exception?.Message, Does.Contain("invalid tenant id"));
        });
    }

    [Test]
    public void Get_claims_from_principal_should_succeed()
    {
        var principalClaims = new List<Claim>();

        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();

        principalClaims.Add(new Claim("tenant", expectedTenantId.ToString()));
        principalClaims.Add(new Claim("single_claim", "single value"));
        principalClaims.Add(new Claim("sub", expectedUserId.ToString()));
        principalClaims.Add(new Claim("list_claim", "list value 1"));
        principalClaims.Add(new Claim("list_claim", "list value 2"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));

        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var actualClaims = subject.GetUserClaims(principal);

        Assert.Multiple(() =>
        {
            Assert.That(actualClaims.Count, Is.EqualTo(4));
            Assert.That(actualClaims["tenant"], Is.EqualTo(expectedTenantId.ToString()));
            Assert.That(actualClaims["sub"], Is.EqualTo(expectedUserId.ToString()));
            Assert.That(actualClaims["single_claim"], Is.EqualTo("single value"));
            Assert.That(actualClaims["list_claim"], Is.EquivalentTo(new string[] { "list value 1", "list value 2" }));
        });
    }

    [Test]
    public void Get_rights_from_principal_should_succeed()
    {
        var principalClaims = new List<Claim>();

        var expectedTenantId = Guid.NewGuid();
        var expectedUserId = Guid.NewGuid();

        principalClaims.Add(new Claim("tenant", expectedTenantId.ToString()));
        principalClaims.Add(new Claim("single_claim", "single value"));
        principalClaims.Add(new Claim("sub", expectedUserId.ToString()));
        principalClaims.Add(new Claim("right", "entity.read"));
        principalClaims.Add(new Claim("right", "entity.write"));
        principalClaims.Add(new Claim("right", "entity.delete"));
        principalClaims.Add(new Claim("list_claim", "list value 1"));
        principalClaims.Add(new Claim("list_claim", "list value 2"));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(principalClaims));

        var subject = new DefaultPrincipalUtils("tenant", "sub", "right");

        var actualRights = subject.GetUserRights(principal)?.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(actualRights, Is.Not.Null);
            Assert.That(actualRights?.Count, Is.EqualTo(3));
            Assert.That(actualRights, Is.EquivalentTo(["entity.read", "entity.write", "entity.delete"]));
        });
    }
}
