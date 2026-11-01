using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatBallot.Web.Services.Spotify
{
    /// <summary>
    /// Holds a single app-wide Spotify access token obtained via the Client
    /// Credentials flow (grant_type=client_credentials). This flow needs no
    /// user login or consent and never expires in a way that requires human
    /// re-authorization -- it's just Spotify:ClientId/ClientSecret, and is
    /// scoped to public catalog access only (search), which is all
    /// listener-facing search needs. Registered as a singleton so every
    /// session shares the same token instead of each DJ bringing their own.
    /// </summary>
    public interface ISharedSpotifyTokenService
    {
        Task<string> GetAccessTokenAsync();
    }

    public class SharedSpotifyTokenService : ISharedSpotifyTokenService
    {
        private const string TokenEndpoint = "https://accounts.spotify.com/api/token";

        // Refresh this many seconds before the token's real expiry, so a
        // request never races a token that's about to go stale.
        private const int ExpiryBufferSeconds = 300;

        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SharedSpotifyTokenService> _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private string _accessToken;
        private DateTime _expiresAtUtc = DateTime.MinValue;

        public SharedSpotifyTokenService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<SharedSpotifyTokenService> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            if (IsCurrentTokenValid())
                return _accessToken;

            await _refreshLock.WaitAsync();
            try
            {
                // Another caller may have refreshed it while we waited.
                if (IsCurrentTokenValid())
                    return _accessToken;

                await RefreshTokenAsync();
                return _accessToken ?? string.Empty;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private bool IsCurrentTokenValid()
        {
            return !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _expiresAtUtc;
        }

        private async Task RefreshTokenAsync()
        {
            var clientId = _configuration["Spotify:ClientId"];
            var clientSecret = _configuration["Spotify:ClientSecret"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("Spotify:ClientId / Spotify:ClientSecret are not configured -- cannot obtain a shared Spotify token");
                return;
            }

            using var httpClient = _httpClientFactory.CreateClient();
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            var requestBody = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            try
            {
                var response = await httpClient.PostAsync(TokenEndpoint, requestBody);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Spotify client-credentials token request failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<ClientCredentialsResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (string.IsNullOrEmpty(tokenResponse?.AccessToken))
                {
                    _logger.LogError("Spotify client-credentials response did not include an access token");
                    return;
                }

                _accessToken = tokenResponse.AccessToken;
                _expiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, tokenResponse.ExpiresIn - ExpiryBufferSeconds));

                _logger.LogInformation("Refreshed shared Spotify token, valid for {ExpiresIn}s", tokenResponse.ExpiresIn);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing shared Spotify token");
            }
        }

        private class ClientCredentialsResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
