// Services/SpotifyAuthService.cs

namespace BeatBallot.Web.Services
{
    public interface ISpotifyAuthService
    {
        Task<bool> IsAuthenticatedAsync();
        Task<string?> GetAccessTokenAsync();
        Task LogoutAsync();
        Task<bool> RefreshTokenIfNeededAsync();
        string GetAuthorizationUrl(string state, string redirectUri);
        Task StoreStateAsync(string state);
        Task<string?> GetStoredStateAsync();
    }

    public class SpotifyAuthService(
        Blazored.LocalStorage.ILocalStorageService _localStorage,
        IConfiguration configuration,
        ILogger<SpotifyAuthService> _logger)
        : ISpotifyAuthService
    {
        public async Task<bool> IsAuthenticatedAsync()
        {
            try
            {
                var accessToken = await _localStorage.GetItemAsync<string>("sat");
                if (string.IsNullOrEmpty(accessToken))
                    return false;

                // Check if token is expired
                var expiresIn = await _localStorage.GetItemAsync<int>("satexp");
                var createdAt = await _localStorage.GetItemAsync<DateTime>("satcr");

                if (DateTime.UtcNow > createdAt.AddSeconds(expiresIn))
                {
                    // Token is expired
                    await LogoutAsync();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking authentication status");
                return false;
            }
        }


        public async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                var accessToken = await _localStorage.GetItemAsStringAsync("sat");

                _logger.LogInformation($"Retrieved token from storage: {(string.IsNullOrEmpty(accessToken) ? "NULL/EMPTY" : "EXISTS")}");

                if (string.IsNullOrEmpty(accessToken))
                {
                    _logger.LogWarning("No access token found in local storage");
                    return null;
                }

                if (!await IsTokenValidAsync())
                {
                    _logger.LogWarning("Access token has expired");
                    await ClearExpiredTokenAsync();
                    return null;
                }

                return accessToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving access token");
                return null;
            }
        }

        public async Task<bool> IsTokenValidAsync()
        {
            try
            {
                var expiresIn = await _localStorage.GetItemAsync<int>("satexp");
                var createdAt = await _localStorage.GetItemAsync<DateTime>("satcr");

                _logger.LogInformation($"Token expires in: {expiresIn} seconds, created at: {createdAt}");

                if (createdAt == default || expiresIn <= 0)
                {
                    _logger.LogWarning("Invalid token expiration data");
                    return false;
                }

                var isValid = DateTime.UtcNow < createdAt.AddSeconds(expiresIn);
                _logger.LogInformation($"Token is {(isValid ? "valid" : "expired")}");

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking token validity");
                return false;
            }
        }

        private async Task ClearExpiredTokenAsync()
        {
            try
            {
                await _localStorage.RemoveItemAsync("sat");
                await _localStorage.RemoveItemAsync("satexp");
                await _localStorage.RemoveItemAsync("satcr");
                _logger.LogInformation("Cleared expired token from storage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing expired token");
            }
        }

        public async Task LogoutAsync()
        {
            await _localStorage.RemoveItemAsync("sat");
            await _localStorage.RemoveItemAsync("satexp");
            await _localStorage.RemoveItemAsync("satcr");
            await _localStorage.RemoveItemAsync("spotify_state"); // Clear stored state too
        }

        public async Task<bool> RefreshTokenIfNeededAsync()
        {
            // For now, just check if we need to re-authenticate
            // In a full implementation, you'd use refresh tokens
            return await IsAuthenticatedAsync();
        }

        public string GetAuthorizationUrl(string state, string redirectUri)
        {
            var clientId = configuration["Spotify:ClientId"];
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

        public async Task<string?> GetStoredStateAsync()
        {
            return await _localStorage.GetItemAsync<string>("spotify_state");
        }
    }
}