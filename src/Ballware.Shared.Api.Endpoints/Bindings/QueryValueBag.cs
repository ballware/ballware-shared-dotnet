using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Ballware.Shared.Api.Endpoints.Bindings;

public class QueryValueBag
{
    public Dictionary<string, StringValues> Query { get; private set; } = new();

    public static ValueTask<QueryValueBag> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        var dict = context.Request.Query.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        return ValueTask.FromResult(new QueryValueBag { Query = dict });
    }
}