using BeatBallot.Models;
using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.Services.Local.BeatBallot.Web.Services.Local;
using SpotifyAPI.Web;

namespace BeatBallot.Web.Services.Spotify
{
    public class SpotifySearchService
    {

        private string _accessToken;
        private SpotifyClient _spotifyClient;
        public readonly Guid Guid;
        private readonly ISongRequestCacheService _cacheService;
        private readonly ISpotifyTokenService _tokenService; // New service to manage DJ tokens
        private readonly ILogger<SpotifySearchService> _logger;

        public SpotifySearchService(ISpotifyTokenService tokenService, ILogger<SpotifySearchService> logger, ISongRequestCacheService cacheService)
        {
            _tokenService = tokenService;
            _logger = logger;
            _cacheService = cacheService;
            Guid = Guid.NewGuid();


            _logger.Log(LogLevel.Information, $"Spotify Service Instance Created: {Guid}");
        }

        private async Task<SpotifyClient> GetClientAsync(string sessionId)
        {
            if (_spotifyClient == null)
            {
                var accessToken = await _tokenService.GetDJTokenForSessionAsync(sessionId);
                if (!string.IsNullOrEmpty(accessToken))
                {
                    _spotifyClient = new SpotifyClient(accessToken);
                }
            }

            return _spotifyClient;
        }

        public async Task<bool> GetHasValidToken(string sessionId = null )
        {
            return await GetClientAsync(sessionId) != null;
        }


        public async Task<SearchResponse> SearchTracksAsync(string query, int limit = 20, int offset = 0, string market = null, string sessionId = null, string deviceId = null)
        {
            var client = await GetClientAsync(sessionId);
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            if (string.IsNullOrWhiteSpace(query))
            {
                // Return empty search response
                return new SearchResponse();
            }

            try
            {
                // Validate and constrain parameters according to Spotify API limits
                limit = Math.Max(1, Math.Min(limit, 50));
                offset = Math.Max(0, offset);

                var searchRequest = new SearchRequest(SearchRequest.Types.Track, query.Trim())
                {
                    Limit = limit,
                    Offset = offset,
                    Market = market ?? "US" // Default to US market if not specified
                };

                // Get session policy for filtering
                SessionPolicy policy = null;
                if (!string.IsNullOrEmpty(sessionId))
                {
                    policy = await _cacheService.GetSessionPolicyAsync(sessionId);
                }

                var searchResult = await _spotifyClient.Search.Item(searchRequest);

                // Apply session policy filtering if policy exists and is valid
                if (policy != null && policy.IsValid() && searchResult?.Tracks?.Items != null)
                {
                    var originalCount = searchResult.Tracks.Items.Count;
                    var filteredTracks = ApplySessionPolicyFiltering(searchResult.Tracks.Items, policy);

                    // Modify the existing tracks collection in place
                    searchResult.Tracks.Items = filteredTracks;

                    // Update the total count to reflect filtered results
                    searchResult.Tracks.Total = filteredTracks.Count;

                    _logger.LogInformation("Applied session policy filtering for session {SessionId}. Original: {OriginalCount}, Filtered: {FilteredCount}",
                        sessionId, originalCount, filteredTracks.Count);
                }

                return searchResult;
            }
            catch (APITooManyRequestsException ex)
            {
                // Handle rate limiting
                _logger.LogWarning("Spotify API rate limit exceeded. Retry after: {RetryAfter}s", ex.RetryAfter);
                throw new SpotifyRateLimitException($"Rate limit exceeded. Retry after {ex.RetryAfter} seconds.");
            }
            catch (APIUnauthorizedException ex)
            {
                // Handle authentication errors
                _logger.LogError("Spotify API authentication error: {Message}", ex.Message);
                throw new SpotifyAuthenticationException(
                    $"Authentication failed. Please reconnect to Spotify. Details: {ex.Message}");
            }
            catch (APIException ex)
            {
                // Handle other Spotify API errors
                _logger.LogError("Spotify API error during search: {Response}", ex.Response?.Body);
                throw new SpotifySearchException($"Search failed: {ex.Message}", ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("Network error during Spotify search: {Message}", ex.Message);
                throw new SpotifySearchException($"Network error while searching: {ex.Message}", ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError("Spotify search request timeout: {Message}", ex.Message);
                throw new SpotifySearchException("Search request timed out. Please try again.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Spotify search");
                throw new SpotifySearchException($"An unexpected error occurred: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Applies session policy filtering to search results
        /// </summary>
        /// <param name="tracks">Original track list from Spotify</param>
        /// <param name="policy">Session policy with filtering rules</param>
        /// <returns>Filtered list of tracks</returns>
        private List<FullTrack> ApplySessionPolicyFiltering(IList<FullTrack> tracks, SessionPolicy policy)
        {
            if (tracks == null || !tracks.Any() || policy == null)
                return tracks?.ToList() ?? new List<FullTrack>();

            var filteredTracks = tracks.AsEnumerable();

            try
            {
                // Filter explicit content
                if (policy.BlockExplicitContent)
                {
                    filteredTracks = filteredTracks.Where(track => !track.Explicit);
                    _logger.LogDebug("Applied explicit content filter");
                }

                // Filter by minimum duration
                if (policy.MinSongDurationMs.HasValue)
                {
                    filteredTracks = filteredTracks.Where(track =>
                        track.DurationMs >= policy.MinSongDurationMs.Value);
                    _logger.LogDebug("Applied minimum duration filter: {MinDuration}ms", policy.MinSongDurationMs.Value);
                }

                // Filter by maximum duration
                if (policy.MaxSongDurationMs.HasValue)
                {
                    filteredTracks = filteredTracks.Where(track =>
                        track.DurationMs <= policy.MaxSongDurationMs.Value);
                    _logger.LogDebug("Applied maximum duration filter: {MaxDuration}ms", policy.MaxSongDurationMs.Value);
                }

                // Filter blocked tracks by ID
                if (policy.BlockedTracks != null && policy.BlockedTracks.Any())
                {
                    var blockedTrackIds = policy.BlockedTracks.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    filteredTracks = filteredTracks.Where(track =>
                        !blockedTrackIds.Contains(track.Id));
                    _logger.LogDebug("Applied blocked tracks filter: {BlockedCount} tracks blocked", policy.BlockedTracks.Count);
                }

                // Filter blocked artists
                if (policy.BlockedArtists != null && policy.BlockedArtists.Any())
                {
                    var blockedArtists = policy.BlockedArtists.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    filteredTracks = filteredTracks.Where(track =>
                        track.Artists == null ||
                        !track.Artists.Any(artist =>
                            blockedArtists.Contains(artist.Name) ||
                            blockedArtists.Contains(artist.Id)));
                    _logger.LogDebug("Applied blocked artists filter: {BlockedCount} artists blocked", policy.BlockedArtists.Count);
                }

                // Filter blocked genres (Note: Individual tracks don't always have genre info from Spotify API)
                // This is more effective when applied to artist data, but we'll do our best with available data
                if (policy.BlockedGenres != null && policy.BlockedGenres.Any())
                {
                    var blockedGenres = policy.BlockedGenres.ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Note: Most FullTrack objects don't include genre information directly
                    // Genre filtering would be more effective at the artist level or album level
                    // For now, we'll skip this filter or implement it when we have artist details
                    _logger.LogDebug("Genre filtering requested but limited by Spotify API track data");
                }

                return filteredTracks.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying session policy filters, returning unfiltered results");
                // Return original tracks if filtering fails
                return tracks.ToList();
            }
        }

        /// <summary>
        /// Gets detailed artist information for genre filtering (optional enhancement)
        /// </summary>
        /// <param name="artistIds">List of artist IDs to check</param>
        /// <param name="blockedGenres">List of blocked genres</param>
        /// <returns>True if any artist has blocked genres</returns>
        private async Task<bool> HasBlockedGenres(IEnumerable<string> artistIds, IList<string> blockedGenres)
        {
            if (artistIds == null || !artistIds.Any() || blockedGenres == null || !blockedGenres.Any())
                return false;

            try
            {
                // Get artist details to check genres
                var artists = await _spotifyClient.Artists.GetSeveral(new ArtistsRequest(artistIds.Take(50).ToList()));

                if (artists?.Artists == null)
                    return false;

                var blockedGenresSet = blockedGenres.ToHashSet(StringComparer.OrdinalIgnoreCase);

                return artists.Artists.Any(artist =>
                    artist.Genres != null &&
                    artist.Genres.Any(genre => blockedGenresSet.Contains(genre)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking artist genres for filtering");
                return false; // Don't block if we can't determine genres
            }
        }

        /// <summary>
        /// Enhanced version with artist genre checking (use this if you want comprehensive genre filtering)
        /// </summary>
        private async Task<List<FullTrack>> ApplySessionPolicyFilteringWithGenres(IList<FullTrack> tracks, SessionPolicy policy)
        {
            if (tracks == null || !tracks.Any() || policy == null)
                return tracks?.ToList() ?? new List<FullTrack>();

            var filteredTracks = new List<FullTrack>();

            foreach (var track in tracks)
            {
                try
                {
                    // Apply basic filters first
                    if (policy.BlockExplicitContent && track.Explicit)
                        continue;

                    if (policy.MinSongDurationMs.HasValue && track.DurationMs < policy.MinSongDurationMs.Value)
                        continue;

                    if (policy.MaxSongDurationMs.HasValue && track.DurationMs > policy.MaxSongDurationMs.Value)
                        continue;

                    if (policy.BlockedTracks != null && policy.BlockedTracks.Contains(track.Id, StringComparer.OrdinalIgnoreCase))
                        continue;

                    // Check blocked artists
                    if (policy.BlockedArtists != null && policy.BlockedArtists.Any() && track.Artists != null)
                    {
                        var hasBlockedArtist = track.Artists.Any(artist =>
                            policy.BlockedArtists.Contains(artist.Name, StringComparer.OrdinalIgnoreCase) ||
                            policy.BlockedArtists.Contains(artist.Id, StringComparer.OrdinalIgnoreCase));

                        if (hasBlockedArtist)
                            continue;
                    }

                    // Check blocked genres (requires additional API call)
                    if (policy.BlockedGenres != null && policy.BlockedGenres.Any() && track.Artists != null)
                    {
                        var artistIds = track.Artists.Select(a => a.Id).ToList();
                        var hasBlockedGenre = await HasBlockedGenres(artistIds, policy.BlockedGenres);

                        if (hasBlockedGenre)
                            continue;
                    }

                    // Track passed all filters
                    filteredTracks.Add(track);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error filtering track {TrackId}, including in results", track.Id);
                    // Include track if filtering fails for this specific track
                    filteredTracks.Add(track);
                }
            }

            return filteredTracks;
        }

        /// <summary>
        /// Advanced search with filters for artist, album, genre, etc.
        /// </summary>
        public async Task<SearchResponse> SearchTracksAdvancedAsync(
            string query,
            string artist = null,
            string album = null,
            string genre = null,
            int? year = null,
            int? minYear = null,
            int? maxYear = null,
            int limit = 20,
            int offset = 0,
            string market = null, string sessionId = null)
        {
            var client = await GetClientAsync(sessionId);
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            var searchQuery = BuildAdvancedSearchQuery(query, artist, album, genre, year, minYear, maxYear);
            return await SearchTracksAsync(searchQuery, limit, offset, market, sessionId);
        }

        /// <summary>
        /// Get tracks optimized for autocomplete scenarios
        /// </summary>
        public async Task<IEnumerable<FullTrack>> GetAutocompleteTracksAsync(string query, int limit = 8, string market = null, string sessionId = null)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return Enumerable.Empty<FullTrack>();
            }

            try
            {
                var searchResult = await SearchTracksAsync(query, limit, 0, market, sessionId );

                // Return tracks sorted by popularity for better autocomplete results
                return searchResult.Tracks?.Items?
                    .OrderByDescending(t => t.Popularity)
                    .Take(limit) ?? Enumerable.Empty<FullTrack>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting autocomplete tracks for query: {Query}", query);
                // Return empty collection on error to prevent breaking the UI
                return Enumerable.Empty<FullTrack>();
            }
        }

        /// <summary>
        /// Get tracks as a simple list (for easier use in components)
        /// </summary>
        public async Task<List<FullTrack>> GetTracksListAsync(string query, int limit = 20, int offset = 0,
            string market = null, string sessionId = null, string deviceId = null)
        {
            try
            {
                var searchResult = await SearchTracksAsync(query, limit, offset, market, sessionId, deviceId);
                return searchResult.Tracks?.Items?.ToList() ?? new List<FullTrack>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tracks list for query: {Query}", query);
                return new List<FullTrack>();
            }
        }

        /// <summary>
        /// Search for tracks by specific artist
        /// </summary>
        public async Task<SearchResponse> SearchTracksByArtistAsync(string artistName, int limit = 20, int offset = 0,
            string market = null, string sessionId = null)
        {
            var query = $"artist:\"{artistName}\"";
            return await SearchTracksAsync(query, limit, offset, market, sessionId);
        }

        /// <summary>
        /// Search for tracks from a specific album
        /// </summary>
        public async Task<SearchResponse> SearchTracksByAlbumAsync(string albumName, string artistName = null,
            int limit = 20, int offset = 0, string market = null, string sessionId = null)
        {
            var query = $"album:\"{albumName}\"";
            if (!string.IsNullOrEmpty(artistName))
            {
                query += $" artist:\"{artistName}\"";
            }

            return await SearchTracksAsync(query, limit, offset, market, sessionId);
        }

        /// <summary>
        /// Search for tracks by genre
        /// </summary>
        public async Task<SearchResponse> SearchTracksByGenreAsync(string genre, int limit = 20, int offset = 0,
            string market = null, string sessionId = null)
        {
            var query = $"genre:\"{genre}\"";
            return await SearchTracksAsync(query, limit, offset, market, sessionId);
        }

        /// <summary>
        /// Get popular tracks matching the search query
        /// </summary>
        public async Task<IEnumerable<FullTrack>> GetPopularTracksAsync(string query, int limit = 20,
            string market = null, string sessionId = null)
        {
            try
            {
                var searchResult = await SearchTracksAsync(query, Math.Min(limit * 2, 50), 0, market, sessionId);

                return searchResult.Tracks?.Items?
                    .Where(t => t.Popularity > 50) // Filter for popular tracks
                    .OrderByDescending(t => t.Popularity)
                    .Take(limit) ?? Enumerable.Empty<FullTrack>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting popular tracks for query: {Query}", query);
                return Enumerable.Empty<FullTrack>();
            }
        }

        /// <summary>
        /// Search with multiple pages (useful for getting more comprehensive results)
        /// </summary>
        public async Task<List<FullTrack>> SearchTracksMultiPageAsync(string query, int totalLimit = 100,
            string market = null, string sessionId = null)
        {
            var allTracks = new List<FullTrack>();
            var pageSize = 50; // Max per request
            var offset = 0;

            try
            {
                while (allTracks.Count < totalLimit)
                {
                    var currentLimit = Math.Min(pageSize, totalLimit - allTracks.Count);
                    var searchResult = await SearchTracksAsync(query, currentLimit, offset, market, sessionId);

                    if (searchResult.Tracks?.Items == null || !searchResult.Tracks.Items.Any())
                        break;

                    allTracks.AddRange(searchResult.Tracks.Items);

                    // Check if we've reached the end of results
                    if (searchResult.Tracks.Items.Count < currentLimit ||
                        searchResult.Tracks.Next == null)
                        break;

                    offset += currentLimit;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during multi-page search for query: {Query}", query);
            }

            return allTracks.Take(totalLimit).ToList();
        }

        public string GetAlbumImageUrl(FullTrack track, string sessionId = null)
        {
            var client = GetClientAsync(sessionId);
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return track.Album?.Images?.OrderByDescending(img => img.Width).FirstOrDefault()?.Url ?? string.Empty;
        }

        /// <summary>
        /// Build advanced search query string with filters
        /// </summary>
        private static string BuildAdvancedSearchQuery(
            string baseQuery,
            string artist = null,
            string album = null,
            string genre = null,
            int? year = null,
            int? minYear = null,
            int? maxYear = null)
        {
            var queryParts = new List<string> { baseQuery };

            if (!string.IsNullOrEmpty(artist))
                queryParts.Add($"artist:\"{artist}\"");

            if (!string.IsNullOrEmpty(album))
                queryParts.Add($"album:\"{album}\"");

            if (!string.IsNullOrEmpty(genre))
                queryParts.Add($"genre:\"{genre}\"");

            if (year.HasValue)
                queryParts.Add($"year:{year}");
            else if (minYear.HasValue && maxYear.HasValue)
                queryParts.Add($"year:{minYear}-{maxYear}");
            else if (minYear.HasValue)
                queryParts.Add($"year:{minYear}-");
            else if (maxYear.HasValue)
                queryParts.Add($"year:-{maxYear}");

            return string.Join(" ", queryParts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        /// <summary>
        /// Helper method to format track duration for display
        /// </summary>
        public static string FormatDuration(int durationMs)
        {
            var timeSpan = TimeSpan.FromMilliseconds(durationMs);
            return timeSpan.ToString(@"m\:ss");
        }

        /// <summary>
        /// Helper method to get primary artist name from FullTrack
        /// </summary>
        public static string GetPrimaryArtistName(FullTrack track)
        {
            return track.Artists?.FirstOrDefault()?.Name ?? "Unknown Artist";
        }

        /// <summary>
        /// Helper method to get all artist names as comma-separated string
        /// </summary>
        public static string GetAllArtistNames(FullTrack track)
        {
            return string.Join(", ", track.Artists?.Select(a => a.Name) ?? new[] { "Unknown Artist" });
        }

        ///// <summary>
        ///// Helper method to get the best available album image URL
        ///// </summary>
        //public static string? GetAlbumImageUrl(FullTrack track, int preferredSize = 300)
        //{
        //    if (track.Album?.Images == null || !track.Album.Images.Any())
        //        return null;

        //    // Try to get image closest to preferred size, or largest if none match
        //    var bestImage = track.Album.Images
        //        .OrderBy(img => Math.Abs((img.Width ?? 0) - preferredSize))
        //        .FirstOrDefault();

        //    return bestImage?.Url;
        //}

        ///// <summary>
        ///// Get track recommendations based on a search query
        ///// </summary>
        //public async Task<IEnumerable<FullTrack>> GetRecommendationsBasedOnSearchAsync(string query, int limit = 10, string? market = null)
        //{
        //    try
        //    {
        //        // First, search for tracks to get seed tracks
        //        var searchResult = await SearchTracksAsync(query, 5, 0, market);
        //        var seedTracks = searchResult.Tracks?.Items?.Take(5).Select(t => t.Id).ToList();

        //        if (seedTracks == null || !seedTracks.Any())
        //            return Enumerable.Empty<FullTrack>();

        //        var recommendationRequest = new RecommendationsRequest
        //        {
        //            SeedTracks = seedTracks,
        //            Limit = limit,
        //            Market = market ?? "US"
        //        };

        //        var recommendations = await _spotifyClient.Browse.GetRecommendations(recommendationRequest);
        //        return recommendations.Tracks ?? Enumerable.Empty<FullTrack>();
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogWarning(ex, "Error getting recommendations for query: {Query}", query);
        //        return Enumerable.Empty<FullTrack>();
        //    }
        //}
    }

    // Custom exception classes
    public class SpotifySearchException : Exception
    {
        public SpotifySearchException(string message) : base(message)
        {
        }

        public SpotifySearchException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class SpotifyRateLimitException : SpotifySearchException
    {
        public SpotifyRateLimitException(string message) : base(message)
        {
        }
    }

    public class SpotifyAuthenticationException : SpotifySearchException
    {
        public SpotifyAuthenticationException(string message) : base(message)
        {
        }
    }
}


