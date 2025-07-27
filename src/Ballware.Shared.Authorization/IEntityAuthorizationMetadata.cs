namespace Ballware.Shared.Authorization;

public interface IEntityAuthorizationMetadata
{
    string Application { get; }
    string Entity { get; }
    string? RightsCheckScript { get; }
    string? StateAllowedScript { get; }
}