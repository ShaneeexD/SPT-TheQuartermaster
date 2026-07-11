using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using TheQuartermaster.Server.Models;

namespace TheQuartermaster.Server.Services;

[Injectable(InjectionType.Singleton)]
public class FirebaseAuthService(
    ISptLogger<FirebaseAuthService> logger,
    ConfigService configService
)
{
    private readonly HttpClient _httpClient = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public string? Uuid { get; private set; }
    public bool IsAuthenticated { get; private set; }

    private string? _idToken;
    private string? _refreshToken;
    private DateTime _expiresAt = DateTime.MinValue;

    public async Task InitialiseAsync()
    {
        if (!configService.Config.ModEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configService.Config.FirebaseApiKey) ||
            string.IsNullOrWhiteSpace(configService.Config.FirebaseProjectId))
        {
            logger.DebugWarning("[TheQuartermaster] Firebase public client config not set; anonymous auth unavailable.");
            return;
        }

        try
        {
            await GetIdTokenAsync();
            logger.DebugInfo($"[TheQuartermaster] Firebase anonymous auth initialised. UID: {Uuid}");
        }
        catch (Exception ex)
        {
            logger.Error($"[TheQuartermaster] Firebase anonymous auth failed: {ex.Message}", ex);
        }
    }

    public async Task<string> GetIdTokenAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_idToken) && DateTime.UtcNow < _expiresAt.AddMinutes(-5))
            {
                return _idToken;
            }

            if (!string.IsNullOrWhiteSpace(_refreshToken))
            {
                try
                {
                    await RefreshIdTokenAsync(cancellationToken);
                    return _idToken!;
                }
                catch (Exception ex)
                {
                    logger.DebugWarning($"[TheQuartermaster] Firebase token refresh failed, signing up again: {ex.Message}");
                }
            }

            await SignUpAnonymousAsync(cancellationToken);
            return _idToken!;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SignUpAnonymousAsync(CancellationToken cancellationToken)
    {
        var apiKey = configService.Config.FirebaseApiKey;
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={apiKey}";

        using var response = await _httpClient.PostAsJsonAsync(
            url,
            new { returnSecureToken = true },
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignUpResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Firebase sign-up response was empty.");

        _idToken = result.IdToken;
        _refreshToken = result.RefreshToken;
        Uuid = result.LocalId;
        _expiresAt = DateTime.UtcNow.AddSeconds(result.ExpiresIn);
        IsAuthenticated = true;
    }

    private async Task RefreshIdTokenAsync(CancellationToken cancellationToken)
    {
        var apiKey = configService.Config.FirebaseApiKey;
        var url = $"https://securetoken.googleapis.com/v1/token?key={apiKey}";

        using var response = await _httpClient.PostAsync(
            url,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken!
            }),
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RefreshResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Firebase refresh response was empty.");

        _idToken = result.IdToken;
        _refreshToken = result.RefreshToken;
        Uuid = result.UserId;
        _expiresAt = DateTime.UtcNow.AddSeconds(result.ExpiresIn);
        IsAuthenticated = true;
    }

    private sealed class SignUpResponse
    {
        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("localId")]
        public string LocalId { get; set; } = string.Empty;
    }

    private sealed class RefreshResponse
    {
        [JsonPropertyName("id_token")]
        public string IdToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = string.Empty;
    }
}
