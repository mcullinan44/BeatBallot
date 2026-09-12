using Microsoft.Extensions.Caching.Memory;

namespace BeatBallot.Web.Services.Spotify
{

    public interface ISpotifyTokenService
    {
        Task SetDJTokenForSessionAsync(string sessionId, string accessToken);
        Task<string> GetDJTokenForSessionAsync(string sessionId);
        Task RemoveDJTokenForSessionAsync(string sessionId);
    }

    public class SpotifyTokenService : ISpotifyTokenService
    {
        private readonly IMemoryCache _cache;

        public SpotifyTokenService(IMemoryCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task SetDJTokenForSessionAsync(string sessionId, string accessToken)
        {
            var cacheKey = $"dj_token_{sessionId}";
            _cache.Set(cacheKey, accessToken, TimeSpan.FromHours(1)); // Spotify tokens typically last 1 hour
        }

        public async Task<string> GetDJTokenForSessionAsync(string sessionId)
        {
            var cacheKey = $"dj_token_{sessionId}";
            return _cache.TryGetValue(cacheKey, out string token) ? token : null;
        }

        public async Task RemoveDJTokenForSessionAsync(string sessionId)
        {
            var cacheKey = $"dj_token_{sessionId}";
            _cache.Remove(cacheKey);
        }
    }
}
