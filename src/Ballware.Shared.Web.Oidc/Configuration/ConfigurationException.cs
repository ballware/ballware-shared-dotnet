namespace Ballware.Shared.Web.Oidc.Configuration;

public class ConfigurationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
