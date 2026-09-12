using System.Text;
using BeatBallot.Web.Services.Local;
using BeatBallot.Web.Services.Local.BeatBallot.Web.Services.Local;
using SpotifyAPI.Web;
using System.Xml.Linq;

namespace BeatBallot.Web.Services
{

    public interface ISessionActivityLogger
    {
        void LogSessionCreated(string sessionId, string djName);
        void LogSessionLeft(string sessionId, string listenerName);
        void LogSongRequested(string sessionId, string songName, string artist, string requesterName, string deviceId);
        void LogSongPlayed(string sessionId, string songName, string artist, string djName);
        void LogSessionEnded(string sessionId, string djName, int totalRequests, TimeSpan duration);
        void LogError(string sessionId, string error, Exception? exception = null);
        void LogTipReceived(string sessionId, string tipperName, decimal tipAmount, string _djUserName);
        void LogDeviceJoined(string sessionId, string requesterName, DeviceInfo info);
        void LogPolicyChange(string sessionId, SessionPolicy previousSessionPolicy, SessionPolicy newSessionPolicy, string djUserName);

        Task<List<SessionActivityLogEntry>> GetRecentActivityAsync(int maxEntries = 100);
        Task<List<SessionActivityLogEntry>> GetActivityBySessionAsync(string sessionId);
        Task ClearOldLogsAsync(DateTime cutoffDate);
    }

    // SessionActivityLogEntry.cs
    public class SessionActivityLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string SessionId { get; set; } = "";
        public string ActivityType { get; set; } = "";
        public string Message { get; set; } = "";
        public string? UserName { get; set; }
        public string? SongName { get; set; }
        public string? Artist { get; set; }
        public string? AdditionalData { get; set; }

        public string? DeviceId { get; set; }
        public LogLevel Level { get; set; }
    }



    public class SessionActivityLogger : ISessionActivityLogger
    {
        private readonly ILogger<SessionActivityLogger> _logger;
        private readonly List<SessionActivityLogEntry> _logEntries;
        private readonly int _maxEntries;
        private readonly object _lock = new object();
        
        public SessionActivityLogger(ILogger<SessionActivityLogger> logger, IConfiguration configuration)
        {
            _logger = logger;
            _logEntries = new List<SessionActivityLogEntry>();
            _maxEntries = configuration.GetValue<int>("AdminSettings:MaxLogEntries", 1000);
        }

        public void LogTipReceived(string sessionId, string tipperName, decimal tipAmount, string _djUserName)
        {

        }


        public void LogSessionCreated(string sessionId, string djName)
        {
            var message = $"Session created by DJ: {djName}";
            AddLogEntry(sessionId, "SessionCreated", message, djName, level: LogLevel.Information);
            _logger.LogInformation("Session {SessionId} created by DJ {DjName}", sessionId, djName);
        }


        public void LogSessionLeft(string sessionId, string listenerName)
        {
            var message = $"Listener left: {listenerName}";
            AddLogEntry(sessionId, "ListenerLeft", message, listenerName, level: LogLevel.Information);
            _logger.LogInformation("Listener {ListenerName} left session {SessionId}", listenerName, sessionId);
        }

        public void LogDeviceJoined(string sessionId, string requesterName, DeviceInfo info)
        {

            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"IP Address: {info.IpAddress}");
            sb.AppendLine($"User Agent: {info.UserAgent}");
            sb.AppendLine($"Is Mobile: {info.IsMobile}");
            sb.AppendLine($"Language: {info.Language}");
            sb.AppendLine($"Screen Resolution: {info.ScreenResolution}");



            AddLogEntry(sessionId, "DeviceJoined", sb.ToString(), requesterName, null, null,
                null, LogLevel.Information, info.DeviceId);

            


        }

        public void LogSongRequested(string sessionId, string songName, string artist, string requesterName, string deviceId)
        {
            var message = $"Song requested: '{songName}' by {artist} (requested by {requesterName})";


            AddLogEntry(sessionId, "SongRequested", message, requesterName, songName, artist, deviceId);
            
            _logger.LogInformation(
                "Song requested in session {SessionId}: {SongName} by {Artist} (requester: {RequesterName}, device: {DeviceId})",
                sessionId, songName, artist, requesterName, deviceId);
        }

        public void LogSongPlayed(string sessionId, string songName, string artist, string djName)
        {
            var message = $"Song played: '{songName}' by {artist} (played by {djName})";
            AddLogEntry(sessionId, "SongPlayed", message, djName, songName, artist );
            _logger.LogInformation("Song played in session {SessionId}: {SongName} by {Artist} (DJ: {DjName})",
                sessionId, songName, artist, djName);
        }

        public void LogSessionEnded(string sessionId, string djName, int totalRequests, TimeSpan duration)
        {
            var message =
                $"Session ended by {djName}. Duration: {duration:hh\\:mm\\:ss}, Total requests: {totalRequests}";
            var additionalData =
                $"{{\"totalRequests\":{totalRequests},\"durationMinutes\":{duration.TotalMinutes:F1}}}";
            AddLogEntry(sessionId, "SessionEnded", message, djName, additionalData: additionalData,
                level: LogLevel.Information);
            _logger.LogInformation(
                "Session {SessionId} ended by {DjName}. Duration: {Duration}, Requests: {TotalRequests}",
                sessionId, djName, duration, totalRequests);
        }

        public void LogError(string sessionId, string error, Exception? exception = null)
        {
            var message = $"Error: {error}";
            var additionalData = exception?.ToString();
            AddLogEntry(sessionId, "Error", message, additionalData: additionalData, level: LogLevel.Error);

            if (exception != null)
                _logger.LogError(exception, "Error in session {SessionId}: {Error}", sessionId, error);
            else
                _logger.LogError("Error in session {SessionId}: {Error}", sessionId, error);
        }


        public void LogPolicyChange(string sessionId, SessionPolicy previousSessionPolicy, SessionPolicy newSessionPolicy, string djUserName)
        {
            try
            {
                var changes = new List<string>();

                // Handle case where previous policy was null (new policy created)
                if (previousSessionPolicy == null)
                {
                    var message = $"Session policy created for session {sessionId} by {djUserName}";
                    AddLogEntry(sessionId, "PolicyCreated", message, djUserName);
                    _logger.LogInformation("Session policy created for session {SessionId} by DJ: {DjName}", sessionId, djUserName);
                    return;
                }

                // Handle case where new policy is null (policy deleted)
                if (newSessionPolicy == null)
                {
                    var message = $"Session policy deleted for session {sessionId} by {djUserName}";
                    AddLogEntry(sessionId, "PolicyDeleted", message, djUserName);
                    _logger.LogInformation("Session policy deleted for session {SessionId} by DJ: {DjName}", sessionId, djUserName);
                    return;
                }

                // Compare BlockExplicitContent
                if (previousSessionPolicy.BlockExplicitContent != newSessionPolicy.BlockExplicitContent)
                {
                    changes.Add($"Explicit content blocking: {(previousSessionPolicy.BlockExplicitContent ? "Enabled" : "Disabled")} → {(newSessionPolicy.BlockExplicitContent ? "Enabled" : "Disabled")}");
                }

                // Compare MaxRequestsPerListener
                if (previousSessionPolicy.MaxRequestsPerListener != newSessionPolicy.MaxRequestsPerListener)
                {
                    var prevValue = previousSessionPolicy.MaxRequestsPerListener?.ToString() ?? "Unlimited";
                    var newValueStr = newSessionPolicy.MaxRequestsPerListener?.ToString() ?? "Unlimited";
                    changes.Add($"Max requests per listener: {prevValue} → {newValueStr}");
                }

                // Compare MinSongDurationMs
                if (previousSessionPolicy.MinSongDurationMs != newSessionPolicy.MinSongDurationMs)
                {
                    var prevValue = previousSessionPolicy.MinSongDurationMs.HasValue
                        ? $"{previousSessionPolicy.MinSongDurationMs.Value / 60000.0:F1} min"
                        : "No minimum";
                    var newValueStr = newSessionPolicy.MinSongDurationMs.HasValue
                        ? $"{newSessionPolicy.MinSongDurationMs.Value / 60000.0:F1} min"
                        : "No minimum";
                    changes.Add($"Minimum song duration: {prevValue} → {newValueStr}");
                }

                // Compare MaxSongDurationMs
                if (previousSessionPolicy.MaxSongDurationMs != newSessionPolicy.MaxSongDurationMs)
                {
                    var prevValue = previousSessionPolicy.MaxSongDurationMs.HasValue
                        ? $"{previousSessionPolicy.MaxSongDurationMs.Value / 60000.0:F1} min"
                        : "No maximum";
                    var newValueStr = newSessionPolicy.MaxSongDurationMs.HasValue
                        ? $"{newSessionPolicy.MaxSongDurationMs.Value / 60000.0:F1} min"
                        : "No maximum";
                    changes.Add($"Maximum song duration: {prevValue} → {newValueStr}");
                }

                // Compare AllowDuplicateRequests
                if (previousSessionPolicy.AllowDuplicateRequests != newSessionPolicy.AllowDuplicateRequests)
                {
                    changes.Add($"Duplicate requests: {(previousSessionPolicy.AllowDuplicateRequests ? "Allowed" : "Blocked")} → {(newSessionPolicy.AllowDuplicateRequests ? "Allowed" : "Blocked")}");
                }

                // Compare ShowRequestsToListeners
                if (previousSessionPolicy.ShowRequestsToListeners != newSessionPolicy.ShowRequestsToListeners)
                {
                    changes.Add($"Show requests to listeners: {(previousSessionPolicy.ShowRequestsToListeners ? "Enabled" : "Disabled")} → {(newSessionPolicy.ShowRequestsToListeners ? "Enabled" : "Disabled")}");
                }

                // Compare PolicyMessage
                if (previousSessionPolicy.PolicyMessage != newSessionPolicy.PolicyMessage)
                {
                    var prevMessage = string.IsNullOrEmpty(previousSessionPolicy.PolicyMessage) ? "[None]" : $"\"{previousSessionPolicy.PolicyMessage}\"";
                    var newMessage = string.IsNullOrEmpty(newSessionPolicy.PolicyMessage) ? "[None]" : $"\"{newSessionPolicy.PolicyMessage}\"";
                    changes.Add($"Custom message: {prevMessage} → {newMessage}");
                }

                // Compare BlockedArtists
                var artistChanges = CompareStringLists(
                    previousSessionPolicy.BlockedArtists ?? new List<string>(),
                    newSessionPolicy.BlockedArtists ?? new List<string>()
                );
                if (artistChanges.HasChanges)
                {
                    var changeDetails = new List<string>();
                    if (artistChanges.Added.Any())
                        changeDetails.Add($"Added: {string.Join(", ", artistChanges.Added)}");
                    if (artistChanges.Removed.Any())
                        changeDetails.Add($"Removed: {string.Join(", ", artistChanges.Removed)}");

                    changes.Add($"Blocked artists: {string.Join("; ", changeDetails)}");
                }

                // Compare BlockedGenres
                var genreChanges = CompareStringLists(
                    previousSessionPolicy.BlockedGenres ?? new List<string>(),
                    newSessionPolicy.BlockedGenres ?? new List<string>()
                );
                if (genreChanges.HasChanges)
                {
                    var changeDetails = new List<string>();
                    if (genreChanges.Added.Any())
                        changeDetails.Add($"Added: {string.Join(", ", genreChanges.Added)}");
                    if (genreChanges.Removed.Any())
                        changeDetails.Add($"Removed: {string.Join(", ", genreChanges.Removed)}");

                    changes.Add($"Blocked genres: {string.Join("; ", changeDetails)}");
                }

                // Compare BlockedTracks
                var trackChanges = CompareStringLists(
                    previousSessionPolicy.BlockedTracks ?? new List<string>(),
                    newSessionPolicy.BlockedTracks ?? new List<string>()
                );
                if (trackChanges.HasChanges)
                {
                    var changeDetails = new List<string>();
                    if (trackChanges.Added.Any())
                        changeDetails.Add($"Added: {string.Join(", ", trackChanges.Added.Take(5))}"); // Limit to 5 for readability
                    if (trackChanges.Removed.Any())
                        changeDetails.Add($"Removed: {string.Join(", ", trackChanges.Removed.Take(5))}"); // Limit to 5 for readability

                    if (trackChanges.Added.Count > 5 || trackChanges.Removed.Count > 5)
                        changeDetails.Add($"(showing first 5 of {trackChanges.Added.Count + trackChanges.Removed.Count} changes)");

                    changes.Add($"Blocked tracks: {string.Join("; ", changeDetails)}");
                }

                // Log the changes
                if (changes.Any())
                {
                    var changesSummary = string.Join(" | ", changes);
                    var message = $"Policy updated for session {sessionId} by {djUserName}: {changesSummary}";

                    AddLogEntry(sessionId, "PolicyChanged", message, djUserName);

                    _logger.LogInformation("Policy changed for session {SessionId} by DJ: {DjName}. Changes: {Changes}",
                        sessionId, djUserName, changesSummary);

                    // Log individual changes for detailed tracking
                    foreach (var change in changes)
                    {
                        _logger.LogDebug("Session {SessionId} policy change detail: {Change}", sessionId, change);
                    }
                }
                else
                {
                    // No actual changes detected
                    var message = $"Policy update triggered for session {sessionId} by {djUserName} but no changes detected";
                    AddLogEntry(sessionId, "PolicyUpdateNoChanges", message, djUserName);
                    _logger.LogInformation("Policy update triggered for session {SessionId} by DJ: {DjName} but no changes detected", sessionId, djUserName);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error logging policy changes for session {sessionId}: {ex.Message}";
                AddLogEntry(sessionId, "PolicyChangeLogError", errorMessage, djUserName);
                _logger.LogError(ex, "Error logging policy changes for session {SessionId}", sessionId);
            }
        }

        /// <summary>
        /// Helper method to compare two string lists and identify additions/removals
        /// </summary>
        /// <param name="previousList">The original list</param>
        /// <param name="newList">The updated list</param>
        /// <returns>Details about what changed</returns>
        private ListComparisonResult CompareStringLists(IList<string> previousList, IList<string> newList)
        {
            var previousSet = new HashSet<string>(previousList ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var newSet = new HashSet<string>(newList ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var added = newSet.Except(previousSet, StringComparer.OrdinalIgnoreCase).ToList();
            var removed = previousSet.Except(newSet, StringComparer.OrdinalIgnoreCase).ToList();

            return new ListComparisonResult
            {
                Added = added,
                Removed = removed,
                HasChanges = added.Any() || removed.Any()
            };
        }

        /// <summary>
        /// Helper class to hold list comparison results
        /// </summary>
        private class ListComparisonResult
        {
            public List<string> Added { get; set; } = new List<string>();
            public List<string> Removed { get; set; } = new List<string>();
            public bool HasChanges { get; set; }
        }



        public Task<List<SessionActivityLogEntry>> GetRecentActivityAsync(int maxEntries = 100)
        {
            lock (_lock)
            {
                return Task.FromResult(_logEntries
                    .OrderByDescending(e => e.Timestamp)
                    .Take(maxEntries)
                    .ToList());
            }
        }

        public Task<List<SessionActivityLogEntry>> GetActivityBySessionAsync(string sessionId)
        {
            lock (_lock)
            {
                return Task.FromResult(_logEntries
                    .Where(e => e.SessionId == sessionId)
                    .OrderByDescending(e => e.Timestamp)
                    .ToList());
            }
        }

        public Task ClearOldLogsAsync(DateTime cutoffDate)
        {
            lock (_lock)
            {
                var toRemove = _logEntries.Where(e => e.Timestamp < cutoffDate).ToList();
                foreach (var entry in toRemove)
                {
                    _logEntries.Remove(entry);
                }
            }

            _logger.LogInformation("Cleared {Count} old log entries before {CutoffDate}",
                _logEntries.Count, cutoffDate);

            return Task.CompletedTask;
        }

        private void AddLogEntry(string sessionId, string activityType, string message,
            string? userName = null, string? songName = null, string? artist = null,
            string? additionalData = null, LogLevel level = LogLevel.Information, string deviceId = null)
        {
            lock (_lock)
            {
                var entry = new SessionActivityLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    SessionId = sessionId,
                    ActivityType = activityType,
                    Message = message,
                    UserName = userName,
                    DeviceId = deviceId,
                    SongName = songName,
                    Artist = artist,
                    AdditionalData = additionalData,
                    Level = level
                };

                _logEntries.Add(entry);

                // Keep only the most recent entries
                if (_logEntries.Count > _maxEntries)
                {
                    var excess = _logEntries.Count - _maxEntries;
                    _logEntries.RemoveRange(0, excess);
                }
            }
        }
    }
}

