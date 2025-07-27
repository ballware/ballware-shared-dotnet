namespace Ballware.Shared.Authorization;

public interface ITenantAuthorizationMetadata
{
    string? RightsCheckScript { get; }
}