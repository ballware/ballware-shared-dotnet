using System.Reflection;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Api.Endpoints.Bindings;

public class UserClaims
{
    public Dictionary<string, object> Value { get; private set; } = null!;
    
    public static ValueTask<UserClaims> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var principalUtils = context.RequestServices.GetRequiredService<IPrincipalUtils>();
        
        if (context.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("User is not authenticated.");
        }

        return ValueTask.FromResult(new UserClaims() { Value = principalUtils.GetUserClaims(context.User) });
    }
}