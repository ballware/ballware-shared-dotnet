using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Shared.Authentication;

public static class JwtBearerEventsExtensions
{
    public static JwtBearerEvents AttachUserInfoEnrichment(this JwtBearerEvents events)
    {
        var previousOnTokenValidated = events.OnTokenValidated;

        events.OnTokenValidated = async context =>
        {
            if (previousOnTokenValidated != null)
            {
                await previousOnTokenValidated(context);
            }

            if (context.Principal == null || context.Result?.Failure != null)
            {
                return;
            }

            var enricher = context.HttpContext.RequestServices.GetService<IUserInfoEnricher>();

            if (enricher == null)
            {
                return;
            }

            var authorizationHeader = context.Request.Headers.Authorization.ToString();
            var accessToken = authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorizationHeader["Bearer ".Length..].Trim()
                : string.Empty;

            var result = await enricher.EnrichAsync(
                context.Principal,
                accessToken,
                context.HttpContext.RequestAborted);

            switch (result.Status)
            {
                case UserInfoEnrichmentStatus.NotEnriched:
                    return;
                case UserInfoEnrichmentStatus.Failed:
                    context.Fail(new InvalidOperationException("Userinfo enrichment failed."));
                    return;
                case UserInfoEnrichmentStatus.Enriched:
                    if (result.Principal != null)
                    {
                        context.Principal = result.Principal;
                    }

                    return;
            }
        };

        return events;
    }
}
