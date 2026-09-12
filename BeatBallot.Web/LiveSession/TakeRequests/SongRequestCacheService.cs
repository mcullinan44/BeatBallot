using BeatBallot.Web.LiveSession.TakeRequests.Shared;
using BeatBallot.Web.LiveSession.VotingExperience.Shared;
using BeatBallot.Web.Services.Local;
using BeatBallot.Web.Services.Local.BeatBallot.Web.Services.Local;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using SpotifyAPI.Web;
using System.Collections.Concurrent;
using static BeatBallot.Web.LiveSession.TakeRequests.ISongRequestCacheService;

namespace BeatBallot.Web.LiveSession.TakeRequests
{
    public interface ISongRequestCacheService
    {
        Task<SongRequestCache> GetSongRequestCacheAsync(string sessionId);
        Task<SongRequestResult> RequestSongs(SongRequestList songRequestList);
        Task<SongRequestResult> CreateSessionAsync(string sessionId, string djUserId, SessionPolicy sessionPolicy);
        Task<bool> IsSessionActiveAsync(string sessionId);
        Task<SongRequestResult> DeactivateSessionAsync(string sessionId);
        Task<List<SongRequest>> GetSessionSongsAsync(string sessionId);
        Task<List<SongRequestGroup>> GetSessionSongsGroupedAsync(string sessionId);
        Task<int> GetSessionRequestCountAsync(string sessionId);
        Task<int> GetSessionUniqueSongCountAsync(string sessionId);
        Task<SongRequestResult> RemoveSongRequestAsync(string sessionId, string songId);
        Task<SongRequestResult> ClearAllRequestsAsync(string sessionId);
        Task<SessionPolicyUpdatedResult> UpdateSessionPolicyAsync(string sessionId, SessionPolicy sessionPolicy);
        Task<SessionPolicy> GetSessionPolicyAsync(string sessionId);
        Task UpdateSessionCacheAsync(SongRequestCache sessionCache);
        Task SaveTipAsync(Tip tip);
        Task<List<Tip>> GetSessionTipsAsync(string sessionId);
        Task<List<Tip>> GetDjTipsAsync(string djUserId, DateTime? startDate = null, DateTime? endDate = null);
        Task UpdateTipStatusAsync(string paymentIntentId, TipStatus status, string? stripeChargeId = null);
        Task<DjPaymentSettings?> GetDjPaymentSettingsAsync(string djUserId);
        Task<DjPaymentSettings> SaveDjPaymentSettingsAsync(DjPaymentSettings settings);
        Task<SessionInfo?> GetSessionInfoAsync(string sessionId);

        // Helper class for grouped song requests
        public class SongRequestGroup
        {
            public FullTrack Song { get; set; } = new();
            public List<SongRequest> Requests { get; set; } = new();
            public int RequestCount { get; set; }
            public DateTime FirstRequestedAt { get; set; }
            public DateTime LatestRequestedAt { get; set; }
            public List<string> DeviceIds { get; set; } = new();

            public List<string> RequesterNames { get; set; } = new();
        }
    }

    public class SongRequestCache
    {
        public SessionPolicy SessionPolicy { get; set; }
        public string SessionId { get; set; } = "";
        public Dictionary<string, SongRequest> Songs { get; set; } = new();
        public DateTime LastUpdated { get; set; }
        public string DjUserId { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        // NEW: Device tracking
        public HashSet<string> ConnectedDevices { get; set; } = new();
        public Dictionary<string, DeviceInfo> DeviceDetails { get; set; } = new();
        public Dictionary<string, DateTime> DeviceLastSeen { get; set; } = new();
        public Dictionary<string, Tip> Tips { get; set; } = new();
        public Dictionary<string, DjPaymentSettings> DjPaymentSettings { get; set; } = new();

    }

    public class SongRequest
    {
        public FullTrack Song { get; set; } = new();
        public string SessionId { get; set; } = "";
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public string RequesterName { get; set; } = ""; // Optional: track who requested

        public string DeviceId { get; set; }
    }

    public class SongRequestList
    {
        public List<SongRequest> Songs { get; set; } = new();
        public string SessionId { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public string DjUserId { get; set; } = string.Empty;
        public bool IsActive { get; set; } = false;
        public string RequesterName { get; set; } = ""; // Optional: track who requested
        public string DeviceId { get; set; }
    }

    public class SessionPolicyUpdatedResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;
    }

    public class SongRequestResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = string.Empty;

        public int SongsAdded { get; set; }
        public List<string> DuplicateSongs { get; set; } = new();
    }

    // Updated SongRequestCacheService with integrated device tracking

    public class SongRequestCacheService : ISongRequestCacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IHubContext<SongRequestHub> _hubContext;
        private readonly ILogger<SongRequestCacheService> _logger;
        private readonly ConcurrentDictionary<string, object> _lockObjects = new();
        private readonly object _lockObject = new();

        // Cache keys
        private const string SongRequestsCachePrefix = "song_requests_";
        private const string ActivePlaylistsKey = "active_playlists";
        private const string TipsCachePrefix = "tips_";
        private const string DjPaymentSettingsPrefix = "dj_payment_";
        private const string SessionInfoPrefix = "session_info_";
        

        private const string TipCacheKeysSetKey = "tip_cache_keys_set";
        private const string DjTipsIndexKey = "dj_tips_index";



        private readonly ConcurrentDictionary<string, bool> _tipCacheKeys = new();
        private readonly ConcurrentDictionary<string, HashSet<string>> _djTipsIndex = new();

        // Cache options
        private static readonly MemoryCacheEntryOptions DefaultCacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
            SlidingExpiration = TimeSpan.FromHours(4),
            Priority = CacheItemPriority.Normal
        };

        public SongRequestCacheService(
            IMemoryCache memoryCache,
            IHubContext<SongRequestHub> hubContext,
            ILogger<SongRequestCacheService> logger)
        {
            _memoryCache = memoryCache;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<SongRequestCache> GetSongRequestCacheAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return new SongRequestCache();

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache cachedSongRequestCache))
                {
                    return cachedSongRequestCache;
                }
            }

            // Return empty cache if not found
            return new SongRequestCache
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow,
                ConnectedDevices = new HashSet<string>(),
                DeviceDetails = new Dictionary<string, DeviceInfo>(),
                DeviceLastSeen = new Dictionary<string, DateTime>()
            };
        }

        public async Task<SongRequestResult> CreateSessionAsync(string sessionId, string djUserId, SessionPolicy sessionPolicy)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session ID is required"
                };
            }

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                // Check if session already exists
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache existingCache))
                {
                    if (existingCache.IsActive)
                    {
                        return new SongRequestResult
                        {
                            Success = false,
                            Message = "Session is already active"
                        };
                    }
                }

                // Create new session with device tracking
                var newCache = new SongRequestCache
                {
                    SessionId = sessionId,
                    DjUserId = djUserId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastUpdated = DateTime.UtcNow,
                    SessionPolicy = sessionPolicy,
                    Songs = new Dictionary<string, SongRequest>(),
                    ConnectedDevices = new HashSet<string>(),
                    DeviceDetails = new Dictionary<string, DeviceInfo>(),
                    DeviceLastSeen = new Dictionary<string, DateTime>()
                };

                _memoryCache.Set(cacheKey, newCache, DefaultCacheOptions);

                _logger?.LogInformation("Created new song request session: {SessionId} for DJ: {DjUserId}",
                    sessionId, djUserId);

                return new SongRequestResult
                {
                    Success = true,
                    Message = "Session created successfully"
                };
            }
        }

        public async Task<SongRequestResult> RequestSongs(SongRequestList songRequestList)
        {
            // Validation
            if (songRequestList == null || !songRequestList.Songs.Any())
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "No songs provided in request"
                };
            }

            if (string.IsNullOrEmpty(songRequestList.SessionId))
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session ID is required"
                };
            }

            // Validate device ID if provided
            if (!string.IsNullOrEmpty(songRequestList.DeviceId) && !IsValidDeviceId(songRequestList.DeviceId))
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "Invalid device ID format"
                };
            }

            var songsAdded = 0;
            var cacheKey = $"{SongRequestsCachePrefix}{songRequestList.SessionId}";

            lock (_lockObject)
            {
                // Get existing cache or create new one
                var songCache = _memoryCache.TryGetValue(cacheKey, out SongRequestCache existingCache)
                    ? existingCache
                    : new SongRequestCache
                    {
                        SessionId = songRequestList.SessionId,
                        DjUserId = songRequestList.DjUserId,
                        IsActive = songRequestList.IsActive,
                        CreatedAt = DateTime.UtcNow,
                        Songs = new Dictionary<string, SongRequest>(),
                        ConnectedDevices = new HashSet<string>(),
                        DeviceDetails = new Dictionary<string, DeviceInfo>(),
                        DeviceLastSeen = new Dictionary<string, DateTime>()
                    };

                // Check if session is active
                if (!songCache.IsActive)
                {
                    return new SongRequestResult
                    {
                        Success = false,
                        Message = "Session is not active"
                    };
                }

                // Verify device is connected to session (if device ID provided)
                if (!string.IsNullOrEmpty(songRequestList.DeviceId) &&
                    !songCache.ConnectedDevices.Contains(songRequestList.DeviceId))
                {
                    _logger?.LogWarning("Device {DeviceId} attempted to request songs but is not connected to session {SessionId}",
                        songRequestList.DeviceId, songRequestList.SessionId);

                    return new SongRequestResult
                    {
                        Success = false,
                        Message = "Device must be connected to session to request songs"
                    };
                }

                // Check session policy constraints
                var deviceRequestCount = GetDeviceRequestCount(songCache, songRequestList.DeviceId);
                if (songCache.SessionPolicy != null &&
                    ShouldBlockRequest(songCache.SessionPolicy, deviceRequestCount, songRequestList.Songs.Count))
                {
                    
                    
                    return new SongRequestResult
                    {
                        Success = false,
                        Message = $"Only {songCache.SessionPolicy.MaxRequestsPerListener} requests can be made for this session."
                    };
                }

                // Add each song from the request list
                foreach (var songRequest in songRequestList.Songs)
                {
                    if (songRequest.Song == null || string.IsNullOrEmpty(songRequest.Song.Id))
                    {
                        continue;
                    }

                    // Create a unique key for each individual request
                    var requestKey = $"{songRequest.Song.Id}_{Guid.NewGuid().ToString("N")[..8]}";

                    // Set request metadata
                    songRequest.SessionId = songRequestList.SessionId;
                    songRequest.RequestedAt = songRequestList.RequestedAt != default
                        ? songRequestList.RequestedAt
                        : DateTime.UtcNow;
                    songRequest.RequesterName = songRequestList.RequesterName;

                    if (!string.IsNullOrEmpty(songRequestList.DeviceId))
                    {
                        songRequest.DeviceId = songRequestList.DeviceId;
                    }

                    // Add song to cache with unique key
                    songCache.Songs[requestKey] = songRequest;
                    songsAdded++;

                    _logger?.LogInformation("Added song request: {SongName} by {Artist} to session: {SessionId} from device: {DeviceId}",
                        songRequest.Song.Name,
                        string.Join(", ", songRequest.Song.Artists?.Select(a => a.Name) ?? new[] { "Unknown" }),
                        songRequestList.SessionId,
                        songRequestList.DeviceId ?? "Unknown");
                }

                // Update device tracking
                if (!string.IsNullOrEmpty(songRequestList.DeviceId))
                {
                    UpdateDeviceActivity(songCache, songRequestList.DeviceId, songsAdded);
                }

                songCache.LastUpdated = DateTime.UtcNow;
                _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);
            }


            return new SongRequestResult
            {
                Success = songsAdded > 0,
                Message = songsAdded > 0 ? $"Successfully added {songsAdded} song(s)" : "Error",
                SongsAdded = songsAdded
            };
        }



        public async Task SaveTipAsync(Tip tip)
        {
            if (tip == null || string.IsNullOrEmpty(tip.Id))
            {
                throw new ArgumentException("Tip and tip ID are required", nameof(tip));
            }

            var tipCacheKey = $"{TipsCachePrefix}{tip.SessionId}";

            lock (_lockObject)
            {
                // Get existing tips for the session
                var existingTips = _memoryCache.TryGetValue(tipCacheKey, out Dictionary<string, Tip> tips)
                    ? tips
                    : new Dictionary<string, Tip>();

                // Add or update the tip
                existingTips[tip.Id] = tip;

                // Update cache
                _memoryCache.Set(tipCacheKey, existingTips, DefaultCacheOptions);

                // Track this cache key
                _tipCacheKeys.TryAdd(tipCacheKey, true);

                // Update DJ tips index
                if (!string.IsNullOrEmpty(tip.DjUserId))
                {
                    if (!_djTipsIndex.ContainsKey(tip.DjUserId))
                    {
                        _djTipsIndex[tip.DjUserId] = new HashSet<string>();
                    }
                    _djTipsIndex[tip.DjUserId].Add(tip.SessionId);
                }

                // Also update the session cache with tip info
                var sessionCacheKey = $"{SongRequestsCachePrefix}{tip.SessionId}";
                if (_memoryCache.TryGetValue(sessionCacheKey, out SongRequestCache sessionCache))
                {
                    if (sessionCache.Tips == null)
                        sessionCache.Tips = new Dictionary<string, Tip>();

                    sessionCache.Tips[tip.Id] = tip;
                    sessionCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(sessionCacheKey, sessionCache, DefaultCacheOptions);
                }

                _logger?.LogInformation("Saved tip: {TipId} for session: {SessionId} from {TipperName} amount: ${Amount}",
                    tip.Id, tip.SessionId, tip.TipperName, tip.Amount);
            }
        }


        public async Task<List<Tip>> GetSessionTipsAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return new List<Tip>();

            var tipCacheKey = $"{TipsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(tipCacheKey, out Dictionary<string, Tip> tips))
                {
                    return tips.Values
                        .Where(t => t.Status == TipStatus.Completed) // Only return completed tips
                        .OrderByDescending(t => t.CreatedAt)
                        .ToList();
                }

                // Also check session cache
                var sessionCacheKey = $"{SongRequestsCachePrefix}{sessionId}";
                if (_memoryCache.TryGetValue(sessionCacheKey, out SongRequestCache sessionCache) &&
                    sessionCache.Tips != null)
                {
                    return sessionCache.Tips.Values
                        .Where(t => t.Status == TipStatus.Completed)
                        .OrderByDescending(t => t.CreatedAt)
                        .ToList();
                }
            }

            return new List<Tip>();
        }


        public async Task<List<Tip>> GetDjTipsAsync(string djUserId, DateTime? startDate = null, DateTime? endDate = null)
        {
            if (string.IsNullOrEmpty(djUserId))
                return new List<Tip>();

            var allTips = new List<Tip>();

            lock (_lockObject)
            {
                // Use the DJ tips index to find relevant sessions
                if (_djTipsIndex.ContainsKey(djUserId))
                {
                    foreach (var sessionId in _djTipsIndex[djUserId])
                    {
                        var tipCacheKey = $"{TipsCachePrefix}{sessionId}";
                        if (_memoryCache.TryGetValue(tipCacheKey, out Dictionary<string, Tip> sessionTips))
                        {
                            var djTips = sessionTips.Values
                                .Where(t => t.DjUserId == djUserId && t.Status == TipStatus.Completed);

                            if (startDate.HasValue)
                                djTips = djTips.Where(t => t.CreatedAt >= startDate.Value);

                            if (endDate.HasValue)
                                djTips = djTips.Where(t => t.CreatedAt <= endDate.Value);

                            allTips.AddRange(djTips);
                        }
                    }
                }
                else
                {
                    // Fallback: search through all known tip cache keys
                    foreach (var cacheKey in _tipCacheKeys.Keys)
                    {
                        if (_memoryCache.TryGetValue(cacheKey, out Dictionary<string, Tip> sessionTips))
                        {
                            var djTips = sessionTips.Values
                                .Where(t => t.DjUserId == djUserId && t.Status == TipStatus.Completed);

                            if (startDate.HasValue)
                                djTips = djTips.Where(t => t.CreatedAt >= startDate.Value);

                            if (endDate.HasValue)
                                djTips = djTips.Where(t => t.CreatedAt <= endDate.Value);

                            allTips.AddRange(djTips);
                        }
                    }
                }
            }

            return allTips.OrderByDescending(t => t.CreatedAt).ToList();
        }

        public async Task UpdateTipStatusAsync(string paymentIntentId, TipStatus status, string? stripeChargeId = null)
        {
            if (string.IsNullOrEmpty(paymentIntentId))
            {
                throw new ArgumentException("Payment intent ID is required", nameof(paymentIntentId));
            }

            lock (_lockObject)
            {
                // Search through known tip cache keys
                foreach (var cacheKey in _tipCacheKeys.Keys)
                {
                    if (_memoryCache.TryGetValue(cacheKey, out Dictionary<string, Tip> sessionTips))
                    {
                        var tip = sessionTips.Values.FirstOrDefault(t => t.PaymentIntentId == paymentIntentId);
                        if (tip != null)
                        {
                            tip.Status = status;
                            if (!string.IsNullOrEmpty(stripeChargeId))
                            {
                                tip.StripeChargeId = stripeChargeId;
                            }

                            // Update in tips cache
                            sessionTips[tip.Id] = tip;
                            _memoryCache.Set(cacheKey, sessionTips, DefaultCacheOptions);

                            // Update in session cache too
                            var sessionCacheKey = $"{SongRequestsCachePrefix}{tip.SessionId}";
                            if (_memoryCache.TryGetValue(sessionCacheKey, out SongRequestCache sessionCache))
                            {
                                if (sessionCache.Tips == null)
                                    sessionCache.Tips = new Dictionary<string, Tip>();

                                sessionCache.Tips[tip.Id] = tip;
                                sessionCache.LastUpdated = DateTime.UtcNow;
                                _memoryCache.Set(sessionCacheKey, sessionCache, DefaultCacheOptions);
                            }

                            _logger?.LogInformation("Updated tip status: {TipId} to {Status} with charge: {ChargeId}",
                                tip.Id, status, stripeChargeId);

                            return;
                        }
                    }
                }

                _logger?.LogWarning("Could not find tip with payment intent ID: {PaymentIntentId}", paymentIntentId);
            }
        }


        public async Task<DjPaymentSettings?> GetDjPaymentSettingsAsync(string djUserId)
        {
            if (string.IsNullOrEmpty(djUserId))
                return null;

            var cacheKey = $"{DjPaymentSettingsPrefix}{djUserId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out DjPaymentSettings settings))
                {
                    return settings;
                }
            }

            return null;
        }


        public async Task<SessionInfo?> GetSessionInfoAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;

            var sessionCacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(sessionCacheKey, out SongRequestCache sessionCache))
                {
                    return new SessionInfo
                    {
                        SessionId = sessionCache.SessionId,
                        DjUserId = sessionCache.DjUserId,
                        IsActive = sessionCache.IsActive,
                        CreatedAt = sessionCache.CreatedAt,
                        LastUpdated = sessionCache.LastUpdated
                    };
                }
            }

            return null;
        }

        // Helper method to get DJ name from session
        public async Task<string> GetDjNameForSessionAsync(string sessionId)
        {
            var sessionInfo = await GetSessionInfoAsync(sessionId);
            if (sessionInfo != null)
            {
                // You might want to implement a separate DJ info cache
                // For now, return the user ID or implement as needed
                return sessionInfo.DjUserId;
            }
            return "Unknown DJ";
        }

        // Method to get tip statistics for a session
        public async Task<decimal> GetSessionTotalTipsAsync(string sessionId)
        {
            var tips = await GetSessionTipsAsync(sessionId);
            return tips.Sum(t => t.Amount);
        }

        // Method to get tip count for a session
        public async Task<int> GetSessionTipCountAsync(string sessionId)
        {
            var tips = await GetSessionTipsAsync(sessionId);
            return tips.Count;
        }

        // Method to get recent tips for a DJ across all sessions
        public async Task<List<Tip>> GetRecentDjTipsAsync(string djUserId, int count = 10)
        {
            var allTips = await GetDjTipsAsync(djUserId);
            return allTips.Take(count).ToList();
        }

        public async Task<DjPaymentSettings> SaveDjPaymentSettingsAsync(DjPaymentSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.DjUserId))
            {
                throw new ArgumentException("Payment settings and DJ user ID are required", nameof(settings));
            }

            var cacheKey = $"{DjPaymentSettingsPrefix}{settings.DjUserId}";
            settings.UpdatedAt = DateTime.UtcNow;

            // If this is a new settings object, set creation time
            if (settings.CreatedAt == default)
            {
                settings.CreatedAt = DateTime.UtcNow;
            }

            lock (_lockObject)
            {
                _memoryCache.Set(cacheKey, settings, DefaultCacheOptions);

                _logger?.LogInformation("Saved payment settings for DJ: {DjUserId}", settings.DjUserId);
            }

            return settings;
        }



        public async Task CleanupOldTipsAsync(TimeSpan maxAge)
        {
            var cutoffDate = DateTime.UtcNow.Subtract(maxAge);

            lock (_lockObject)
            {
                var keysToRemove = new List<string>();

                foreach (var cacheKey in _tipCacheKeys.Keys)
                {
                    if (_memoryCache.TryGetValue(cacheKey, out Dictionary<string, Tip> sessionTips))
                    {
                        var tipsToRemove = sessionTips.Values
                            .Where(t => t.CreatedAt < cutoffDate && t.Status == TipStatus.Completed)
                            .Select(t => t.Id)
                            .ToList();

                        if (tipsToRemove.Any())
                        {
                            foreach (var tipId in tipsToRemove)
                            {
                                sessionTips.Remove(tipId);
                            }

                            if (sessionTips.Any())
                            {
                                _memoryCache.Set(cacheKey, sessionTips, DefaultCacheOptions);
                            }
                            else
                            {
                                // Remove empty cache and track for cleanup
                                _memoryCache.Remove(cacheKey);
                                keysToRemove.Add(cacheKey);
                            }

                            _logger?.LogInformation("Cleaned up {Count} old tips from cache key: {CacheKey}",
                                tipsToRemove.Count, cacheKey);
                        }
                    }
                    else
                    {
                        // Cache key exists but no data - mark for removal
                        keysToRemove.Add(cacheKey);
                    }
                }

                // Clean up tracked cache keys
                foreach (var keyToRemove in keysToRemove)
                {
                    _tipCacheKeys.TryRemove(keyToRemove, out _);
                }
            }
        }

        // Method to clear all tip data for a session (useful when session is deleted)
        public async Task ClearSessionTipsAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;

            var tipCacheKey = $"{TipsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                // Remove from dedicated tip cache
                _memoryCache.Remove(tipCacheKey);
                _tipCacheKeys.TryRemove(tipCacheKey, out _);

                // Remove from session cache
                var sessionCacheKey = $"{SongRequestsCachePrefix}{sessionId}";
                if (_memoryCache.TryGetValue(sessionCacheKey, out SongRequestCache sessionCache))
                {
                    sessionCache.Tips?.Clear();
                    sessionCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(sessionCacheKey, sessionCache, DefaultCacheOptions);
                }

                // Update DJ tips index
                foreach (var djTipsEntry in _djTipsIndex)
                {
                    djTipsEntry.Value.Remove(sessionId);
                }

                _logger?.LogInformation("Cleared all tips for session: {SessionId}", sessionId);
            }
        }

        public async Task<TipStatistics> GetTipStatisticsAsync(string sessionId)
        {
            var tips = await GetSessionTipsAsync(sessionId);

            return new TipStatistics
            {
                SessionId = sessionId,
                TotalAmount = tips.Sum(t => t.Amount),
                TipCount = tips.Count,
                AverageAmount = tips.Any() ? tips.Average(t => t.Amount) : 0,
                LargestTip = tips.Any() ? tips.Max(t => t.Amount) : 0,
                SmallestTip = tips.Any() ? tips.Min(t => t.Amount) : 0,
                FirstTipTime = tips.Any() ? tips.Min(t => t.CreatedAt) : (DateTime?)null,
                LastTipTime = tips.Any() ? tips.Max(t => t.CreatedAt) : (DateTime?)null,
                UniqueTippers = tips.Select(t => t.TipperName).Distinct().Count()
            };
        }


        // NEW: Device management methods
        public async Task<bool> AddDeviceToSessionAsync(string deviceId, string sessionId, DeviceInfo deviceInfo)
        {
            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(sessionId))
                return false;

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache) && songCache.IsActive)
                {
                    songCache.ConnectedDevices.Add(deviceId);
                    songCache.DeviceLastSeen[deviceId] = DateTime.UtcNow;

                    // Initialize or update device details
                    if (!songCache.DeviceDetails.ContainsKey(deviceId))
                    {
                        deviceInfo.FirstSeen = DateTime.UtcNow;
                        deviceInfo.RequestCount = 0;
                    }
                    else
                    {
                        deviceInfo.FirstSeen = songCache.DeviceDetails[deviceId].FirstSeen;
                        deviceInfo.RequestCount = songCache.DeviceDetails[deviceId].RequestCount;
                    }

                    deviceInfo.LastSeen = DateTime.UtcNow;
                    deviceInfo.SessionId = sessionId;
                    songCache.DeviceDetails[deviceId] = deviceInfo;

                    songCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);

                    return true;
                }
            }

            return false;
        }

        public async Task<bool> RemoveDeviceFromSessionAsync(string deviceId, string sessionId)
        {
            if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(sessionId))
                return false;

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache))
                {
                    var removed = songCache.ConnectedDevices.Remove(deviceId);
                    songCache.DeviceLastSeen.Remove(deviceId);

                    // Keep device details for historical purposes but mark last seen
                    if (songCache.DeviceDetails.ContainsKey(deviceId))
                    {
                        songCache.DeviceDetails[deviceId].LastSeen = DateTime.UtcNow;
                    }

                    songCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);

                    return removed;
                }
            }

            return false;
        }

        public async Task<List<DeviceInfo>> GetSessionDevicesAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.ConnectedDevices
                .Where(deviceId => cache.DeviceDetails.ContainsKey(deviceId))
                .Select(deviceId => cache.DeviceDetails[deviceId])
                .OrderBy(d => d.FirstSeen)
                .ToList();
        }

        public async Task<int> GetSessionDeviceCountAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.ConnectedDevices.Count;
        }

        public async Task<bool> IsDeviceInSessionAsync(string deviceId, string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.ConnectedDevices.Contains(deviceId);
        }

        public async Task<Dictionary<string, int>> GetDeviceRequestStatsAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.DeviceDetails
                .Where(kvp => kvp.Value.RequestCount > 0)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.RequestCount);
        }

        // Helper methods for device validation and tracking
        private bool IsValidDeviceId(string deviceId)
        {
            return true;
            
            if (string.IsNullOrEmpty(deviceId))
                return false;

            // Validate device ID format - adjust regex based on your format
            var deviceIdRegex = new System.Text.RegularExpressions.Regex(@"^beatballot_device_[0-9A-F]{12}$");
            return deviceIdRegex.IsMatch(deviceId);
        }

        private int GetDeviceRequestCount(SongRequestCache cache, string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId) || !cache.DeviceDetails.ContainsKey(deviceId))
                return 0;

            return cache.DeviceDetails[deviceId].RequestCount;
        }

        private bool ShouldBlockRequest(SessionPolicy policy, int currentDeviceRequests, int newRequestCount)
        {
            // Implement your session policy logic here
            // Example: if policy has max requests per device
            if (policy.MaxRequestsPerListener.HasValue &&
                currentDeviceRequests + newRequestCount > policy.MaxRequestsPerListener.Value)
                return true;

            return false; // For now, allow all requests
        }

        private void UpdateDeviceActivity(SongRequestCache cache, string deviceId, int songsAdded)
        {
            if (string.IsNullOrEmpty(deviceId))
                return;

            // Update last seen
            cache.DeviceLastSeen[deviceId] = DateTime.UtcNow;

            // Update device details
            if (cache.DeviceDetails.ContainsKey(deviceId))
            {
                cache.DeviceDetails[deviceId].RequestCount += songsAdded;
                cache.DeviceDetails[deviceId].LastSeen = DateTime.UtcNow;
            }
        }

        // Keep all your existing methods unchanged
        public async Task<bool> IsSessionActiveAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.IsActive;
        }

        public async Task<SongRequestResult> DeactivateSessionAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session ID is required"
                };
            }

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache))
                {
                    songCache.IsActive = false;
                    songCache.LastUpdated = DateTime.UtcNow;

                    // Clear connected devices when session is deactivated
                    songCache.ConnectedDevices.Clear();
                    songCache.DeviceLastSeen.Clear();

                    _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);

                    _logger?.LogInformation("Deactivated song request session: {SessionId}", sessionId);

                    return new SongRequestResult
                    {
                        Success = true,
                        Message = "Session deactivated successfully"
                    };
                }

                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session not found"
                };
            }
        }

        // ... (keep all your other existing methods unchanged)
        public async Task<List<SongRequest>> GetSessionSongsAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.Songs.Values.OrderBy(s => s.RequestedAt).ToList();
        }

        public async Task<List<SongRequestGroup>> GetSessionSongsGroupedAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);

            var grouped = cache.Songs.Values
                .GroupBy(s => s.Song.Id)
                .Select(g => new SongRequestGroup
                {
                    Song = g.First().Song,
                    Requests = g.ToList(),
                    RequestCount = g.Count(),
                    FirstRequestedAt = g.Min(r => r.RequestedAt),
                    LatestRequestedAt = g.Max(r => r.RequestedAt),
                    DeviceIds = g.Select(r => r.DeviceId).Distinct().ToList(),
                    RequesterNames = g.Select(r => r.RequesterName).Distinct().ToList()
                })
                .OrderByDescending(g => g.LatestRequestedAt)
                .ToList();

            return grouped;
        }

        public async Task<int> GetSessionRequestCountAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.Songs.Count;
        }

        public async Task<int> GetSessionUniqueSongCountAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.Songs.Values.GroupBy(s => s.Song.Id).Count();
        }

        public async Task<SongRequestResult> RemoveSongRequestAsync(string sessionId, string songId)
        {
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(songId))
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session ID and Song ID are required"
                };
            }

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";
            var removedCount = 0;

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache))
                {
                    var requestsToRemove = songCache.Songs.Values
                        .Where(r => r.Song.Id == songId)
                        .ToList();

                    foreach (var request in requestsToRemove)
                    {
                        var key = songCache.Songs.FirstOrDefault(kvp => kvp.Value == request).Key;
                        if (!string.IsNullOrEmpty(key))
                        {
                            songCache.Songs.Remove(key);
                            removedCount++;
                        }
                    }

                    songCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);

                    return new SongRequestResult
                    {
                        Success = true,
                        Message = $"Removed {removedCount} request(s) for the song",
                        SongsAdded = -removedCount
                    };
                }

                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session not found"
                };
            }
        }

        public async Task<SongRequestResult> ClearAllRequestsAsync(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session ID is required"
                };
            }

            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";

            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache))
                {
                    var clearedCount = songCache.Songs.Count;
                    songCache.Songs.Clear();

                    // Reset device request counts
                    foreach (var device in songCache.DeviceDetails.Values)
                    {
                        device.RequestCount = 0;
                    }

                    songCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);

                    return new SongRequestResult
                    {
                        Success = true,
                        Message = $"Cleared {clearedCount} request(s)",
                        SongsAdded = -clearedCount
                    };
                }

                return new SongRequestResult
                {
                    Success = false,
                    Message = "Session not found"
                };
            }
        }

        public async Task<SessionPolicyUpdatedResult> UpdateSessionPolicyAsync(string sessionId, SessionPolicy sessionPolicy)
        {
            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";
            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache))
                {
                    songCache.SessionPolicy = sessionPolicy;
                    songCache.LastUpdated = DateTime.UtcNow;
                    _memoryCache.Set(cacheKey, songCache, DefaultCacheOptions);

                    return new SessionPolicyUpdatedResult
                    {
                        Success = true,
                        Message = "Policy updated"
                    };
                }
            }

            return new SessionPolicyUpdatedResult
            {
                Success = false,
                Message = "Session not found"
            };
        }

        public Task<SessionPolicy> GetSessionPolicyAsync(string sessionId)
        {
            var cacheKey = $"{SongRequestsCachePrefix}{sessionId}";
            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out SongRequestCache songCache))
                {
                    return Task.FromResult(songCache.SessionPolicy);
                }
            }
            return Task.FromResult<SessionPolicy>(null);
        }

        private async Task<int> GetSessionSongCountAsync(string sessionId)
        {
            var cache = await GetSongRequestCacheAsync(sessionId);
            return cache.Songs.Count;
        }


        // Enhanced UpdateSessionCacheAsync method with additional features

        public async Task UpdateSessionCacheAsync(SongRequestCache sessionCache)
        {
            if (sessionCache == null || string.IsNullOrEmpty(sessionCache.SessionId))
            {
                _logger?.LogWarning("Attempted to update null or invalid session cache");
                return;
            }

            var cacheKey = $"{SongRequestsCachePrefix}{sessionCache.SessionId}";

            lock (_lockObject)
            {
                // Validate that the session exists in cache first
                var existingCache = _memoryCache.TryGetValue(cacheKey, out SongRequestCache currentCache);

                if (!existingCache)
                {
                    _logger?.LogWarning("Attempted to update non-existent session cache: {SessionId}", sessionCache.SessionId);
                    // Create new cache entry if it doesn't exist
                    sessionCache.CreatedAt = DateTime.UtcNow;
                }
                else
                {
                    // Preserve the original creation time
                    sessionCache.CreatedAt = currentCache.CreatedAt;

                    // Merge any missing device details to avoid data loss
                    foreach (var existingDevice in currentCache.DeviceDetails)
                    {
                        if (!sessionCache.DeviceDetails.ContainsKey(existingDevice.Key))
                        {
                            sessionCache.DeviceDetails[existingDevice.Key] = existingDevice.Value;
                        }
                    }
                }

                // Update the last updated timestamp
                sessionCache.LastUpdated = DateTime.UtcNow;

                // Validate device data consistency
                ValidateDeviceDataConsistency(sessionCache);

                // Set the updated cache back to memory cache
                _memoryCache.Set(cacheKey, sessionCache, DefaultCacheOptions);

                _logger?.LogDebug("Updated session cache for session: {SessionId} with {DeviceCount} devices",
                    sessionCache.SessionId, sessionCache.ConnectedDevices.Count);
            }

            // Optional: Notify clients about session updates (outside of lock)
            try
            {
                await NotifySessionUpdateAsync(sessionCache);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to notify clients about session update for session: {SessionId}",
                    sessionCache.SessionId);
            }
        }

        /// <summary>
        /// Validates and fixes any inconsistencies in device data
        /// </summary>
        private void ValidateDeviceDataConsistency(SongRequestCache sessionCache)
        {
            try
            {
                // Ensure all connected devices have device details
                var devicesToAdd = sessionCache.ConnectedDevices
                    .Where(deviceId => !sessionCache.DeviceDetails.ContainsKey(deviceId))
                    .ToList();

                foreach (var deviceId in devicesToAdd)
                {
                    sessionCache.DeviceDetails[deviceId] = new DeviceInfo
                    {
                        DeviceId = deviceId,
                        SessionId = sessionCache.SessionId,
                        FirstSeen = DateTime.UtcNow,
                        LastSeen = DateTime.UtcNow,
                        RequestCount = 0
                    };

                    _logger?.LogWarning("Added missing device details for device: {DeviceId} in session: {SessionId}",
                        deviceId, sessionCache.SessionId);
                }

                // Ensure all connected devices have last seen timestamps
                var devicesNeedingTimestamp = sessionCache.ConnectedDevices
                    .Where(deviceId => !sessionCache.DeviceLastSeen.ContainsKey(deviceId))
                    .ToList();

                foreach (var deviceId in devicesNeedingTimestamp)
                {
                    sessionCache.DeviceLastSeen[deviceId] = DateTime.UtcNow;
                }

                // Remove device last seen entries for devices no longer connected
                var obsoleteTimestamps = sessionCache.DeviceLastSeen.Keys
                    .Where(deviceId => !sessionCache.ConnectedDevices.Contains(deviceId))
                    .ToList();

                foreach (var deviceId in obsoleteTimestamps)
                {
                    sessionCache.DeviceLastSeen.Remove(deviceId);
                }

                // Validate device request counts match actual requests
                var deviceRequestCounts = sessionCache.Songs.Values
                    .Where(request => !string.IsNullOrEmpty(request.DeviceId))
                    .GroupBy(request => request.DeviceId)
                    .ToDictionary(g => g.Key, g => g.Count());

                foreach (var deviceCount in deviceRequestCounts)
                {
                    if (sessionCache.DeviceDetails.ContainsKey(deviceCount.Key))
                    {
                        var expectedCount = deviceCount.Value;
                        var actualCount = sessionCache.DeviceDetails[deviceCount.Key].RequestCount;

                        if (expectedCount != actualCount)
                        {
                            _logger?.LogWarning("Request count mismatch for device {DeviceId}: Expected {Expected}, Actual {Actual}",
                                deviceCount.Key, expectedCount, actualCount);

                            // Fix the count
                            sessionCache.DeviceDetails[deviceCount.Key].RequestCount = expectedCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error validating device data consistency for session: {SessionId}",
                    sessionCache.SessionId);
            }
        }

        /// <summary>
        /// Notifies connected clients about session updates
        /// </summary>
        private async Task NotifySessionUpdateAsync(SongRequestCache sessionCache)
        {
            try
            {
                var updateData = new
                {
                    SessionId = sessionCache.SessionId,
                    DeviceCount = sessionCache.ConnectedDevices.Count,
                    TotalRequests = sessionCache.Songs.Count,
                    UniqueRequests = sessionCache.Songs.Values.GroupBy(s => s.Song.Id).Count(),
                    LastUpdated = sessionCache.LastUpdated,
                    IsActive = sessionCache.IsActive
                };

                await _hubContext.Clients.Group($"session_{sessionCache.SessionId}")
                    .SendAsync("SessionUpdated", updateData);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error sending session update notification for session: {SessionId}",
                    sessionCache.SessionId);
            }
        }

        /// <summary>
        /// Alternative method for bulk updates with validation
        /// </summary>
        public async Task UpdateSessionCacheWithValidationAsync(SongRequestCache sessionCache, bool skipNotification = false)
        {
            if (sessionCache == null || string.IsNullOrEmpty(sessionCache.SessionId))
            {
                throw new ArgumentException("Session cache and session ID are required", nameof(sessionCache));
            }

            var cacheKey = $"{SongRequestsCachePrefix}{sessionCache.SessionId}";

            lock (_lockObject)
            {
                // Get existing cache for comparison
                var hadExistingCache = _memoryCache.TryGetValue(cacheKey, out SongRequestCache currentCache);

                if (hadExistingCache)
                {
                    // Preserve creation time and merge critical data
                    sessionCache.CreatedAt = currentCache.CreatedAt;

                    // Log significant changes
                    LogSessionChanges(currentCache, sessionCache);
                }
                else
                {
                    sessionCache.CreatedAt = DateTime.UtcNow;
                    _logger?.LogInformation("Creating new session cache entry for session: {SessionId}", sessionCache.SessionId);
                }

                // Validate and fix data consistency
                ValidateDeviceDataConsistency(sessionCache);

                // Update timestamp
                sessionCache.LastUpdated = DateTime.UtcNow;

                // Update cache
                _memoryCache.Set(cacheKey, sessionCache, DefaultCacheOptions);

                _logger?.LogDebug("Successfully updated session cache for session: {SessionId}", sessionCache.SessionId);
            }

            // Notify clients if requested
            if (!skipNotification)
            {
                await NotifySessionUpdateAsync(sessionCache);
            }
        }

        /// <summary>
        /// Logs significant changes between cache versions
        /// </summary>
        private void LogSessionChanges(SongRequestCache oldCache, SongRequestCache newCache)
        {
            try
            {
                // Device changes
                var addedDevices = newCache.ConnectedDevices.Except(oldCache.ConnectedDevices).ToList();
                var removedDevices = oldCache.ConnectedDevices.Except(newCache.ConnectedDevices).ToList();

                if (addedDevices.Any())
                {
                    _logger?.LogInformation("Devices added to session {SessionId}: {DeviceIds}",
                        newCache.SessionId, string.Join(", ", addedDevices));
                }

                if (removedDevices.Any())
                {
                    _logger?.LogInformation("Devices removed from session {SessionId}: {DeviceIds}",
                        newCache.SessionId, string.Join(", ", removedDevices));
                }

                // Song request changes
                var oldRequestCount = oldCache.Songs.Count;
                var newRequestCount = newCache.Songs.Count;

                if (newRequestCount != oldRequestCount)
                {
                    _logger?.LogInformation("Song request count changed for session {SessionId}: {OldCount} -> {NewCount}",
                        newCache.SessionId, oldRequestCount, newRequestCount);
                }

                // Session status changes
                if (oldCache.IsActive != newCache.IsActive)
                {
                    _logger?.LogInformation("Session {SessionId} status changed: {OldStatus} -> {NewStatus}",
                        newCache.SessionId, oldCache.IsActive ? "Active" : "Inactive", newCache.IsActive ? "Active" : "Inactive");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error logging session changes for session: {SessionId}", newCache.SessionId);
            }
        }

    }

    public static class MemoryCacheExtensions
    {
        public static object? Field(this object obj, string name)
        {
            try
            {
                var field = obj.GetType().GetField(name,
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                return field?.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }
    }
}
