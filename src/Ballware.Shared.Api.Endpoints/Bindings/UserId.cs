using System.Reflection;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Api.Endpoints.Bindings;

public class UserId
{
    public Guid Value { get; private set; }
    
    public static ValueTask<UserId> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var principalUtils = context.RequestServices.GetRequiredService<IPrincipalUtils>();
        
        if (context.User.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("User is not authenticated.");
        }

        return ValueTask.FromResult(new UserId() { Value = principalUtils.GetUserId(context.User) });
    }
}