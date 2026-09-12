namespace BeatBallot.Web.Services.Local
{
    using System;

    namespace BeatBallot.Web.Services.Local
    {
        /// <summary>
        /// Represents the policy settings for a DJ session that control 
        /// what content listeners can search for and request.
        /// </summary>
        public class SessionPolicy
        {
            /// <summary>
            /// The unique identifier for the session this policy applies to.
            /// </summary>
            public string SessionId { get; set; } = string.Empty;

            /// <summary>
            /// When true, explicit/profanity-marked songs are filtered out of search results for listeners.
            /// When false, all songs including explicit content are available for request.
            /// </summary>
            public bool BlockExplicitContent { get; set; } = true;

            /// <summary>
            /// Optional: Maximum number of requests allowed per listener.
            /// Null means unlimited requests.
            /// </summary>
            public int? MaxRequestsPerListener { get; set; } = null;

            /// <summary>
            /// Optional: Minimum song duration in milliseconds.
            /// Songs shorter than this won't appear in search results.
            /// </summary>
            public int? MinSongDurationMs { get; set; } = null;

            /// <summary>
            /// Optional: Maximum song duration in milliseconds.
            /// Songs longer than this won't appear in search results.
            /// </summary>
            public int? MaxSongDurationMs { get; set; } = null;

            /// <summary>
            /// Optional: List of artist names or IDs to block from requests.
            /// </summary>
            public List<string> BlockedArtists { get; set; } = new List<string>();

            /// <summary>
            /// Optional: List of specific track IDs to block from requests.
            /// </summary>
            public List<string> BlockedTracks { get; set; } = new List<string>();

            /// <summary>
            /// Optional: List of genres to block from requests.
            /// </summary>
            public List<string> BlockedGenres { get; set; } = new List<string>();

            /// <summary>
            /// When true, allows listeners to request the same song multiple times.
            /// When false, prevents duplicate song requests.
            /// </summary>
            public bool AllowDuplicateRequests { get; set; } = true;

            /// <summary>
            /// When true, listeners can see what songs others have requested.
            /// When false, requests are hidden from other listeners.
            /// </summary>
            public bool ShowRequestsToListeners { get; set; } = false;

            /// <summary>
            /// Optional: Custom message to display to listeners about session policies.
            /// </summary>
            public string? PolicyMessage { get; set; } = null;

            /// <summary>
            /// Timestamp when this policy was created.
            /// </summary>
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

            /// <summary>
            /// Timestamp when this policy was last updated.
            /// </summary>
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

            /// <summary>
            /// The DJ user ID who created/last modified this policy.
            /// </summary>
            public string DjUserId { get; set; } = string.Empty;

            /// <summary>
            /// Creates a default session policy with standard settings.
            /// </summary>
            /// <param name="sessionId">The session ID this policy applies to</param>
            /// <param name="djUserId">The DJ user ID creating the policy</param>
            /// <returns>A new SessionPolicy with default values</returns>
            public static SessionPolicy CreateDefault(string sessionId, string djUserId)
            {
                return new SessionPolicy
                {
                    SessionId = sessionId,
                    DjUserId = djUserId,
                    BlockExplicitContent = true,
                    AllowDuplicateRequests = true,
                    ShowRequestsToListeners = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
            }

            /// <summary>
            /// Updates the timestamp to mark when this policy was last modified.
            /// </summary>
            public void MarkAsUpdated()
            {
                UpdatedAt = DateTime.UtcNow;
            }

            /// <summary>
            /// Validates that the policy settings are consistent and valid.
            /// </summary>
            /// <returns>True if the policy is valid, false otherwise</returns>
            public bool IsValid()
            {
                // Basic validation
                if (string.IsNullOrEmpty(SessionId) || string.IsNullOrEmpty(DjUserId))
                    return false;

                // Duration validation
                if (MinSongDurationMs.HasValue && MaxSongDurationMs.HasValue)
                {
                    if (MinSongDurationMs.Value >= MaxSongDurationMs.Value)
                        return false;
                }

                // Request limit validation
                if (MaxRequestsPerListener.HasValue && MaxRequestsPerListener.Value <= 0)
                    return false;

                return true;
            }
        }
    }
}
