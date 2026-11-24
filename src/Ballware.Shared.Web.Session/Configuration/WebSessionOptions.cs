using System.ComponentModel.DataAnnotations;

namespace Ballware.Shared.Web.Session.Configuration;

public class WebSessionOptions
{
    public bool EnableSession { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string SessionCookieName { get; set; } = ".BallwareSession";

    [Required(AllowEmptyStrings = false)]
    public string SessionCacheKeyPrefix { get; set; } = "ballware:session:";

    [Range(1, int.MaxValue)]
    public int SessionCacheExpirationInMinutes { get; set; } = 60;

    public bool SuppressDefaultSessionCookie { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string DataProtectionPurpose { get; set; } = "Ballware.Web.Session.ClaimsPrincipal";
}
