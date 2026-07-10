using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;

namespace TheQuartermaster.Server.Services;

public class FirebaseAuthCredential(FirebaseAuthService firebaseAuthService) : ICredential
{
    public Task<string> GetAccessTokenForRequestAsync(string authUri = null!, CancellationToken cancellationToken = default)
    {
        return firebaseAuthService.GetIdTokenAsync(cancellationToken);
    }

    public void Initialize(ConfigurableHttpClient httpClient)
    {
        // No HTTP client initialization required for gRPC token access.
    }
}
