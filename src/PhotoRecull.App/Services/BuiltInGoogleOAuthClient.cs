using System.Reflection;
using Google.Apis.Auth.OAuth2;

namespace PhotoRecull.App.Services;

internal static class BuiltInGoogleOAuthClient
{
    public static ClientSecrets? TryGet()
    {
        var metadata = typeof(BuiltInGoogleOAuthClient).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);

        return metadata.TryGetValue("PhotoRecull.GoogleClientId", out var clientId) && !string.IsNullOrEmpty(clientId)
            && metadata.TryGetValue("PhotoRecull.GoogleClientSecret", out var clientSecret) && !string.IsNullOrEmpty(clientSecret)
            ? new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret }
            : null;
    }
}
