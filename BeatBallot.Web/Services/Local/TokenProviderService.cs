using SpotifyAPI.Web;
using SpotifyAPI.Web.Http;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeatBallot.Web.Services.Local
{
    public interface ITokenProvider
    {
        Task<string> GetAccessTokenAsync();
        Task<bool> IsTokenValidAsync();
        Task<bool> RefreshTokenAsync();
        Task ClearTokensAsync();
        Task StoreTokensAsync(string accessToken, string refreshToken, int expiresIn);

        string GetAuthorizationUrl(string state, string redirectUri);

        Task StoreStateAsync(string state);

        Task<string> GetStoredStateAsync();

        Task<bool> IsAuthenticatedAsync();
    }


    public class TokenProvider : ITokenProvider
    {
        private readonly Blazored.LocalStorage.ILocalStorageService _localStorage;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

        public TokenProvider(
            Blazored.LocalStorage.ILocalStorageService localStorage,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _localStorage = localStorage;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var accessToken = await _localStorage.GetItemAsync<string>("sat");
                if (string.IsNullOrEmpty(accessToken))
                    return null;

                // Check if token is valid
                if (await IsTokenValidAsync())
                    return accessToken;

                // Token is expired, try to refresh
                if (await RefreshTokenAsync())
                {
                    // Get the new access token after refresh
                    return await _localStorage.GetItemAsync<string>("sat");
                }

                // Refresh failed, clear invalid tokens
                await ClearTokensAsync();
                return null;
            }
            catch (Exception ex) 
            {
                return null;
            }
        }

        public async Task StoreTokensAsync(string accessToken, string refreshToken, int expiresIn)
        {
            try
            {
                var createdAt = DateTime.UtcNow;

                await _localStorage.SetItemAsync("sat", accessToken);
                await _localStorage.SetItemAsync("srt", refreshToken);
                await _localStorage.SetItemAsync("satexp", expiresIn);
                await _localStorage.SetItemAsync("satcr", createdAt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error storing tokens: {ex.Message}");
                throw;
            }
        }

        public async Task ClearTokensAsync()
        {
            try
            {
                await _localStorage.RemoveItemAsync("sat");
                await _localStorage.RemoveItemAsync("srt");
                await _localStorage.RemoveItemAsync("satexp");
                await _localStorage.RemoveItemAsync("satcr");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing tokens: {ex.Message}");
            }
        }



        public async Task<bool> RefreshTokenAsync()
        {
            // Use semaphore to prevent multiple concurrent refresh attempts
            await _refreshSemaphore.WaitAsync();
            try
            {
                var refreshToken = await _localStorage.GetItemAsync<string>("srt");
                if (string.IsNullOrEmpty(refreshToken))
                    return false;

                var clientId = _configuration["Spotify:ClientId"];
                var clientSecret = _configuration["Spotify:ClientSecret"];

                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                {
                    Console.WriteLine("Spotify client credentials not configured");
                    return false;
                }

                using var httpClient = _httpClientFactory.CreateClient();

                // Prepare the refresh token request
                var tokenEndpoint = "https://accounts.spotify.com/api/token";
                var requestBody = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", refreshToken)
                });

                // Add Authorization header with client credentials
                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

                var response = await httpClient.PostAsync(tokenEndpoint, requestBody);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Token refresh failed: {response.StatusCode} - {errorContent}");
                    return false;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<SpotifyTokenResponse>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (tokenResponse?.AccessToken == null)
                {
                    Console.WriteLine("Invalid token response from Spotify");
                    return false;
                }

                // Store the new tokens
                await StoreTokensAsync(
                    tokenResponse.AccessToken,
                    tokenResponse.RefreshToken ?? refreshToken, // Use new refresh token if provided, otherwise keep the old one
                    tokenResponse.ExpiresIn
                );

                Console.WriteLine("Access token refreshed successfully");
                return true;
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Network error during token refresh: {ex.Message}");
                return false;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON parsing error during token refresh: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error during token refresh: {ex.Message}");
                return false;
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }


        public async Task<bool> IsAuthenticatedAsync()
        {
            try
            {

                if (await IsTokenValidAsync())
                {

                }


                var accessToken = await _localStorage.GetItemAsync<string>("sat");
                if (string.IsNullOrEmpty(accessToken))
                    return false;

                // Check if token is expired
                var expiresIn = await _localStorage.GetItemAsync<int>("satexp");
                var createdAt = await _localStorage.GetItemAsync<DateTime>("satcr");

                if (DateTime.UtcNow > createdAt.AddSeconds(expiresIn))
                {
                    // Token is expired
                    await ClearTokensAsync();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }


        public string GetAuthorizationUrl(string state, string redirectUri)
        {
            var clientId = _configuration["Spotify:ClientId"];
            var scope =
                "user-read-private user-read-email user-modify-playback-state user-read-playback-state user-read-currently-playing playlist-read-private playlist-modify-public";

            return $"https://accounts.spotify.com/authorize?" +
                   $"response_type=code&" +
                   $"client_id={clientId}&" +
                   $"scope={Uri.EscapeDataString(scope)}&" +
                   $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                   $"state={state}";
        }

        public async Task StoreStateAsync(string state)
        {
            await _localStorage.SetItemAsStringAsync("spotify_state", state);
        }

        public async Task<string> GetStoredStateAsync()
        {
            return await _localStorage.GetItemAsync<string>("spotify_state");
        }

        public class SpotifyTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("scope")]
            public string Scope { get; set; }

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; }

        }

        public async Task<bool> IsTokenValidAsync()
        {
            try
            {
                var expiresIn = await _localStorage.GetItemAsync<int>("satexp");
                var createdAt = await _localStorage.GetItemAsync<DateTime>("satcr");

                if (expiresIn == 0 || createdAt == default)
                    return false;

                // Add a buffer of 5 minutes before expiration to ensure we refresh in time
                var expirationTime = createdAt.AddSeconds(expiresIn).AddMinutes(-5);
                return DateTime.UtcNow < expirationTime;
            }
            catch
            {
                return false;
            }
        }


    }
}
