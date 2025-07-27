using System.Text.Json;
using Jint;

namespace Ballware.Shared.Authorization.Jint.Internal;

class JavascriptEntityRightsChecker : IEntityRightsChecker
{
    public async Task<bool> HasRightAsync(Guid tenantId, IEntityAuthorizationMetadata authorizationMetadata, IDictionary<string, object> claims, string right, object? param,
        bool tenantResult)
    {
        var result = tenantResult;
        var rightsScript = authorizationMetadata.RightsCheckScript;

        if (!string.IsNullOrWhiteSpace(rightsScript))
        {
            var userinfo = JsonSerializer.Serialize(claims);

            result = bool.Parse(new Engine()
                .SetValue("application", authorizationMetadata.Application)
                .SetValue("entity", authorizationMetadata.Entity)
                .SetValue("right", right)
                .SetValue("param", param)
                .SetValue("result", tenantResult)
                .Evaluate($"var userinfo = JSON.parse('{userinfo}'); function extendedRightsCheck() {{ {rightsScript} }} return extendedRightsCheck();")
                .ToString());
        }

        return await Task.FromResult(result);
    }
    
    public async Task<bool> StateAllowedAsync(Guid tenantId, IEntityAuthorizationMetadata authorizationMetadata, Guid id, int currentState, IEnumerable<string> rights)
    {
        if (!string.IsNullOrEmpty(authorizationMetadata.StateAllowedScript))
        {
            var result = bool.Parse(new Engine()
                .SetValue("state", currentState)
                .SetValue("hasRight", new Func<string, bool>((right) => { return rights?.Contains(right.ToLowerInvariant()) ?? false; }))
                .SetValue("hasAnyRight", new Func<string, bool>((right) => { return rights?.Any(r => r.StartsWith(right.ToLowerInvariant())) ?? false; }))
                .Evaluate(authorizationMetadata.StateAllowedScript)
                .ToString());
            
            return await Task.FromResult(result);
        }
        
        return await Task.FromResult(false);
    }
}