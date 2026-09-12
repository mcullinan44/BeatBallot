using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.LiveSession.TakeRequests.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BeatBallot.Web.Services.Local
{
    // 1. Device Information Model
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string ScreenResolution { get; set; } = string.Empty;
        public string TimeZone { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public bool IsMobile { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string SessionId { get; set; } = string.Empty;
        
        public int RequestCount { get; set; } 
    }

    // 4. Client-side JavaScript Helper (wwwroot/js/deviceInfo.js)
    public class ClientDeviceInfo
    {
        public string ScreenResolution { get; set; } = string.Empty;
        public string TimeZone { get; set; } = string.Empty;
    }

    // 2. Device Service Interface
    public interface IDeviceService
    {
        Task<string> GetOrCreateDeviceIdAsync();
        Task<DeviceInfo> GetDeviceInfoAsync();
        Task<DeviceInfo> GetDeviceInfoAsync(string deviceId);
        Task<bool> AddDeviceToSessionAsync(string deviceId, string sessionId);

        Task<bool> RemoveDeviceFromSessionAsync(string deviceId, string sessionId);
        Task<List<DeviceInfo>> GetSessionDevicesAsync(string sessionId);
        Task<int> GetSessionDeviceCountAsync(string sessionId);
        Task<bool> IsDeviceInSessionAsync(string deviceId, string sessionId);
        Task UpdateDeviceLastSeenAsync(string deviceId, string sessionId);
        Task<bool> ValidateDeviceIdAsync(string deviceId);
        Task CleanupInactiveDevicesAsync(string sessionId, TimeSpan inactiveThreshold);
    }


    // 3. Device Service Implementation
    public class DeviceService : IDeviceService
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ISongRequestCacheService _songRequestCache;
        private readonly ILogger<DeviceService> _logger;
        private readonly IHubContext<SongRequestHub> _hubContext;

        public DeviceService(
            IJSRuntime jsRuntime,
            IHttpContextAccessor httpContextAccessor,
            ISongRequestCacheService songRequestCache,
            ILogger<DeviceService> logger,
            IHubContext<SongRequestHub> hubContext)
        {
            _jsRuntime = jsRuntime;
            _httpContextAccessor = httpContextAccessor;
            _songRequestCache = songRequestCache;
            _logger = logger;
            _hubContext = hubContext;
        }

        public async Task<string> GetOrCreateDeviceIdAsync()
        {
            try
            {
                // Try to get device ID from browser storage first
                var existingId = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "beatballot_device_id");

                if (!string.IsNullOrEmpty(existingId) && await ValidateDeviceIdAsync(existingId))
                {
                    return existingId;
                }

                // Generate new device ID
                var deviceInfo = await CollectDeviceInfoAsync();
                var deviceId = GenerateDeviceId(deviceInfo);

                // Store in browser
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "beatballot_device_id", deviceId);

                _logger.LogInformation("Created new device ID: {DeviceId}", deviceId);
                return deviceId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting or creating device ID");
                return GenerateFallbackDeviceId();
            }
        }

        public async Task<DeviceInfo> GetDeviceInfoAsync()
        {
            var deviceId = await GetOrCreateDeviceIdAsync();
            return await GetDeviceInfoAsync(deviceId);
        }

        public async Task<DeviceInfo> GetDeviceInfoAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return new DeviceInfo();

            // Device info is stored within session caches, so we need to check active sessions
            // For now, we'll collect fresh device info - in production you might want to cache this separately
            var deviceInfo = await CollectDeviceInfoAsync();
            deviceInfo.DeviceId = deviceId;
            return deviceInfo;
        }


        public async Task<bool> AddDeviceToSessionAsync(string deviceId, string sessionId)
        {
            try
            {
                var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);

                if (!sessionCache.IsActive)
                {
                    _logger.LogWarning("Attempted to add device {DeviceId} to inactive session {SessionId}",
                        deviceId, sessionId);
                    return false;
                }

                // Add device to session
                sessionCache.ConnectedDevices.Add(deviceId);
                sessionCache.DeviceLastSeen[deviceId] = DateTime.UtcNow;

                // Get/create device details
                var deviceInfo = await CollectDeviceInfoAsync();
                deviceInfo.DeviceId = deviceId;
                deviceInfo.SessionId = sessionId;
                deviceInfo.FirstSeen = sessionCache.DeviceDetails.ContainsKey(deviceId)
                    ? sessionCache.DeviceDetails[deviceId].FirstSeen
                    : DateTime.UtcNow;
                deviceInfo.LastSeen = DateTime.UtcNow;

                sessionCache.DeviceDetails[deviceId] = deviceInfo;
                sessionCache.LastUpdated = DateTime.UtcNow;

                // Update cache through the service (this will handle the actual caching)
                await _songRequestCache.UpdateSessionCacheAsync(sessionCache);

                // Notify other clients
                await _hubContext.Clients.Group($"session_{sessionId}")
                    .SendAsync("DeviceJoined", new
                    {
                        DeviceId = deviceId,
                        SessionId = sessionId,
                        DeviceCount = sessionCache.ConnectedDevices.Count,
                        IsMobile = deviceInfo.IsMobile
                    });

                _logger.LogInformation("Device {DeviceId} joined session {SessionId}. Total devices: {DeviceCount}",
                    deviceId, sessionId, sessionCache.ConnectedDevices.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding device {DeviceId} to session {SessionId}", deviceId, sessionId);
                return false;
            }
        }

        public async Task<bool> RemoveDeviceFromSessionAsync(string deviceId, string sessionId)
        {
            try
            {
                var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);

                var wasConnected = sessionCache.ConnectedDevices.Remove(deviceId);
                if (wasConnected)
                {
                    sessionCache.DeviceLastSeen.Remove(deviceId);
                    sessionCache.LastUpdated = DateTime.UtcNow;

                    // Keep device details for historical purposes, but mark as disconnected
                    if (sessionCache.DeviceDetails.ContainsKey(deviceId))
                    {
                        sessionCache.DeviceDetails[deviceId].LastSeen = DateTime.UtcNow;
                    }

                    await _songRequestCache.UpdateSessionCacheAsync(sessionCache);

                    // Notify other clients
                    await _hubContext.Clients.Group($"session_{sessionId}")
                        .SendAsync("DeviceLeft", new
                        {
                            DeviceId = deviceId,
                            SessionId = sessionId,
                            DeviceCount = sessionCache.ConnectedDevices.Count
                        });

                    _logger.LogInformation("Device {DeviceId} left session {SessionId}. Remaining devices: {DeviceCount}",
                        deviceId, sessionId, sessionCache.ConnectedDevices.Count);
                }

                return wasConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing device {DeviceId} from session {SessionId}", deviceId, sessionId);
                return false;
            }
        }

        public async Task<List<DeviceInfo>> GetSessionDevicesAsync(string sessionId)
        {
            var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);

            return sessionCache.ConnectedDevices
                .Where(deviceId => sessionCache.DeviceDetails.ContainsKey(deviceId))
                .Select(deviceId => sessionCache.DeviceDetails[deviceId])
                .OrderBy(d => d.FirstSeen)
                .ToList();
        }

        public async Task<int> GetSessionDeviceCountAsync(string sessionId)
        {
            var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);
            return sessionCache.ConnectedDevices.Count;
        }

        public async Task<bool> IsDeviceInSessionAsync(string deviceId, string sessionId)
        {
            var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);
            return sessionCache.ConnectedDevices.Contains(deviceId);
        }

        public async Task UpdateDeviceLastSeenAsync(string deviceId, string sessionId)
        {
            try
            {
                var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);

                if (sessionCache.ConnectedDevices.Contains(deviceId))
                {
                    sessionCache.DeviceLastSeen[deviceId] = DateTime.UtcNow;

                    if (sessionCache.DeviceDetails.ContainsKey(deviceId))
                    {
                        sessionCache.DeviceDetails[deviceId].LastSeen = DateTime.UtcNow;
                    }

                    sessionCache.LastUpdated = DateTime.UtcNow;
                    await _songRequestCache.UpdateSessionCacheAsync(sessionCache);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last seen for device {DeviceId} in session {SessionId}",
                    deviceId, sessionId);
            }
        }

        public async Task<bool> ValidateDeviceIdAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                return false;

            // Basic validation - adjust regex based on your device ID format
            var deviceIdRegex = new System.Text.RegularExpressions.Regex(@"^dev_[a-f0-9]{12}_\d+$");
            return deviceIdRegex.IsMatch(deviceId);
        }

        public async Task CleanupInactiveDevicesAsync(string sessionId, TimeSpan inactiveThreshold)
        {
            try
            {
                var sessionCache = await _songRequestCache.GetSongRequestCacheAsync(sessionId);
                var cutoffTime = DateTime.UtcNow - inactiveThreshold;

                var inactiveDevices = sessionCache.DeviceLastSeen
                    .Where(kvp => kvp.Value < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var deviceId in inactiveDevices)
                {
                    await RemoveDeviceFromSessionAsync(deviceId, sessionId);
                }

                if (inactiveDevices.Any())
                {
                    _logger.LogInformation("Cleaned up {Count} inactive devices from session {SessionId}",
                        inactiveDevices.Count, sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up inactive devices for session {SessionId}", sessionId);
            }
        }

        // Private helper methods
        private async Task<DeviceInfo> CollectDeviceInfoAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var deviceInfo = new DeviceInfo();

            if (httpContext != null)
            {
                deviceInfo.UserAgent = httpContext.Request.Headers.UserAgent.ToString();
                deviceInfo.IpAddress = GetClientIpAddress(httpContext);
                deviceInfo.Language = httpContext.Request.Headers.AcceptLanguage.ToString();
                deviceInfo.IsMobile = IsMobileDevice(deviceInfo.UserAgent);
            }

            try
            {
                var clientInfo = await _jsRuntime.InvokeAsync<ClientDeviceInfo>("getClientDeviceInfo");
                deviceInfo.ScreenResolution = clientInfo.ScreenResolution;
                deviceInfo.TimeZone = clientInfo.TimeZone;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not collect client-side device info");
            }

            return deviceInfo;
        }

        private string GenerateDeviceId(DeviceInfo deviceInfo)
        {
            var fingerprint = $"{deviceInfo.UserAgent}|{deviceInfo.IpAddress}|{deviceInfo.ScreenResolution}|{deviceInfo.TimeZone}";
            var hash = ComputeSha256Hash(fingerprint);
            return $"dev_{hash[..12]}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        private string GenerateFallbackDeviceId()
        {
            return $"dev_fallback_{Guid.NewGuid():N}"[..20];
        }

        private string ComputeSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private string GetClientIpAddress(HttpContext context)
        {
            var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = context.Request.Headers["X-Real-IP"].FirstOrDefault();
            }
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = context.Connection.RemoteIpAddress?.ToString();
            }
            return ipAddress ?? "unknown";
        }

        private bool IsMobileDevice(string userAgent)
        {
            var mobileKeywords = new[] { "Mobile", "Android", "iPhone", "iPad", "Windows Phone" };
            return mobileKeywords.Any(keyword => userAgent.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        //private async Task UpdateSessionCacheAsync(SongRequestCache sessionCache)
        //{
        //    // This is a bit of a workaround since we can't directly update the cache
        //    // We'll need to add a method to ISongRequestCacheService to update the cache
        //    // For now, we'll assume the cache is updated by reference
        //    // In production, you might want to add an UpdateSessionCacheAsync method to ISongRequestCacheService
        //}
    }



}
