using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.Services.Local;
using BeatBallot.Web.Services.Local.BeatBallot.Web.Services.Local;
using Microsoft.AspNetCore.SignalR;
using SpotifyAPI.Web;
using System.Collections.Concurrent;

namespace BeatBallot.Web.LiveSession.TakeRequests.Shared
{
    public class SongRequestHub : Hub
    {
        private readonly ISongRequestCacheService _songRequestCacheService;
        private readonly ILogger<SongRequestHub> _logger;

        // Track connected users and their roles
        private static readonly ConcurrentDictionary<string, UserConnection> _connections = new();
        private static readonly ConcurrentDictionary<string, SessionInfo> _activeSessions = new();

        public SongRequestHub(
            ISongRequestCacheService songRequestCacheService,
            ILogger<SongRequestHub> logger)
        {
            _songRequestCacheService = songRequestCacheService;
            _logger = logger;
        }

        #region Connection Management

        public override async Task OnConnectedAsync()
        {
            var connectionId = Context.ConnectionId;
            var userAgent = Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString() ?? "";

            _logger.LogInformation("User connected: {ConnectionId} from {UserAgent}", connectionId, userAgent);

            // Store basic connection info
            _connections[connectionId] = new UserConnection
            {
                ConnectionId = connectionId,
                ConnectedAt = DateTime.UtcNow,
                UserAgent = userAgent
            };

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;

            if (_connections.TryRemove(connectionId, out var userConnection))
            {
                // Remove from any session groups they were part of
                if (!string.IsNullOrEmpty(userConnection.SessionId))
                {
                    await LeaveSession(userConnection.SessionId);
                }

                _logger.LogInformation("User disconnected: {ConnectionId} (was in session: {SessionId})",
                    connectionId, userConnection.SessionId ?? "none");
            }

            if (exception != null)
            {
                _logger.LogError(exception, "User disconnected with error: {ConnectionId}", connectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        #endregion

        #region DJ Session Management

        /// <summary>
        /// DJ joins a session to manage song requests
        /// </summary>
        public async Task StartTakeRequestsSession(string sessionId, PrivateUser user, SessionPolicy sessionPolicy)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    await Clients.Caller.SendAsync("Error", "Session ID is required");
                    return;
                }

                var connectionId = Context.ConnectionId;

                // Update connection info
                if (_connections.TryGetValue(connectionId, out var connection))
                {
                    connection.SessionId = sessionId;
                    connection.UserId = user.Id;
                    connection.Role = UserRole.DJ;
                }

                // Add to DJ group for this session
                var djGroupName = GetDJGroupName(sessionId);
                await Groups.AddToGroupAsync(connectionId, djGroupName);

                // Track active session
                _activeSessions[sessionId] = new SessionInfo
                {
                    SessionId = sessionId,
                    DJUserId = user.Id,
                    DJConnectionId = connectionId,
                    StartedAt = DateTime.UtcNow,
                    IsActive = true
                };

                // Create or activate the session in cache
                await _songRequestCacheService.CreateSessionAsync(sessionId, user.Id, sessionPolicy);

                _logger.LogInformation("DJ {DJUserId} started session {SessionId} with connection {ConnectionId}",
                    user.Id, sessionId, connectionId);

                // Notify DJ of successful join and current session state
                var existingRequests = await _songRequestCacheService.GetSessionSongsAsync(sessionId);
                await Clients.Caller.SendAsync("DJSessionJoined", new
                {
                    SessionId = sessionId,
                    Message = "Successfully joined session as DJ",
                    ExistingRequestsCount = existingRequests.Count,
                    ExistingRequests = existingRequests.Select(r => new
                    {
                        TrackId = r.Song.Id,
                        TrackName = r.Song.Name,
                        Artists = string.Join(", ", r.Song.Artists?.Select(a => a.Name) ?? new[] { "Unknown" }),
                        r.RequestedAt,
                        r.DeviceId
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining DJ session {SessionId} for user {DJUserId}", sessionId, user.Id);
                await Clients.Caller.SendAsync("Error", "Failed to join DJ session");
            }
        }

        /// <summary>
        /// DJ leaves a session
        /// </summary>
        public async Task LeaveDJSession(string sessionId)
        {
            try
            {
                var connectionId = Context.ConnectionId;

                // Remove from DJ group
                var djGroupName = GetDJGroupName(sessionId);
                await Groups.RemoveFromGroupAsync(connectionId, djGroupName);

                // Update connection info
                if (_connections.TryGetValue(connectionId, out var connection))
                {
                    connection.SessionId = null;
                    connection.Role = UserRole.None;
                }

                // Remove from active sessions
                _activeSessions.TryRemove(sessionId, out _);

                _logger.LogInformation("DJ left session {SessionId} with connection {ConnectionId}", sessionId, connectionId);

                await Clients.Caller.SendAsync("DJSessionLeft", new { SessionId = sessionId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving DJ session {SessionId}", sessionId);
            }
        }
        
        #endregion

        #region Listener Session Management

        /// <summary>
        /// Listener joins a session to make song requests
        /// </summary>
        public async Task JoinListenerSession(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    await Clients.Caller.SendAsync("Error", "Session ID is required");
                    return;
                }

                // Check if session is active
                var isActive = await _songRequestCacheService.IsSessionActiveAsync(sessionId);
                if (!isActive)
                {
                    await Clients.Caller.SendAsync("SessionInactive", new
                    {
                        SessionId = sessionId,
                        Message = "This session is not currently accepting requests"
                    });
                    return;
                }

                var connectionId = Context.ConnectionId;

                // Update connection info
                if (_connections.TryGetValue(connectionId, out var connection))
                {
                    connection.SessionId = sessionId;
                    connection.UserId =  $"listener_{connectionId[..8]}";
                    connection.Role = UserRole.Listener;
                }

                // Add to listener group for this session
                var listenerGroupName = GetListenerGroupName(sessionId);
                await Groups.AddToGroupAsync(connectionId, listenerGroupName);

                _logger.LogInformation("Listener {ListenerId} joined session {SessionId} with connection {ConnectionId}",
                     "anonymous", sessionId, connectionId);

                // Notify listener of successful join
                await Clients.Caller.SendAsync("ListenerSessionJoined", new
                {
                    SessionId = sessionId,
                    Message = "Successfully joined session",
                    CanMakeRequests = true
                });

                // Notify DJ of new listener count
                await NotifyDJOfListenerCount(sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining listener session {SessionId} for user {ListenerId}", sessionId, "anonymous");
                await Clients.Caller.SendAsync("Error", "Failed to join session");
            }
        }

        /// <summary>
        /// Listener leaves a session
        /// </summary>
        public async Task LeaveListenerSession(string sessionId)
        {
            await LeaveSession(sessionId);
        }

        #endregion

        public async Task TipReceived(TipNotification tip)
        {
            await Clients.Group($"dj-{tip.SessionId}").SendAsync("TipReceived", tip);
            await Clients.Group(tip.SessionId).SendAsync("TipReceived", tip);
        }

        #region Song Request Handling

        /// <summary>
        /// Submit song requests from a listener
        /// </summary>
        public async Task SubmitSongRequests(SongRequestList songRequestList)
        {
            try
            {
                var connectionId = Context.ConnectionId;

                // Validate request
                if (songRequestList == null || !songRequestList.Songs.Any())
                {
                    await Clients.Caller.SendAsync("RequestError", "No songs provided in request");
                    return;
                }

                // Verify caller is in the session as a listener
                if (!_connections.TryGetValue(connectionId, out var connection) ||
                    connection.SessionId != songRequestList.SessionId ||
                    connection.Role != UserRole.Listener)
                {
                    await Clients.Caller.SendAsync("RequestError", "Not authorized to make requests for this session");
                    return;
                }

                // Set requester ID
                //songRequestList.RequesterId = songRequestList.RequesterId;

                // Submit to cache service
                var result = await _songRequestCacheService.RequestSongs(songRequestList);

                if (result.Success)
                {
                    _logger.LogInformation("Song requests submitted by {RequesterId} for session {SessionId}: {SongsAdded} added, {Duplicates} duplicates",
                        connection.UserId, songRequestList.SessionId, result.SongsAdded, result.DuplicateSongs.Count);

                    // Notify the submitter
                    await Clients.Caller.SendAsync("RequestsSubmitted", new SongRequestResult()
                    {
                        Success = true,
                        Message = result.Message,
                        SongsAdded = result.SongsAdded,
                        DuplicateSongs = result.DuplicateSongs
                    });

                    // Notify DJ of new song requests
                    if (result.SongsAdded > 0)
                    {
                        await NotifyDJOfNewRequests(songRequestList.SessionId, result.SongsAdded, songRequestList.Songs);
                    }
                }
                else
                {
                    await Clients.Caller.SendAsync("RequestError", result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting song requests for session {SessionId}", songRequestList?.SessionId);
                await Clients.Caller.SendAsync("RequestError", "Failed to submit song requests");
            }
        }

        /// <summary>
        /// DJ acknowledges or plays a requested song
        /// </summary>
        public async Task DJPlaySong(string sessionId, string trackId)
        {
            try
            {
                var connectionId = Context.ConnectionId;

                // Verify caller is the DJ for this session
                if (!_connections.TryGetValue(connectionId, out var connection) ||
                    connection.SessionId != sessionId ||
                    connection.Role != UserRole.DJ)
                {
                    await Clients.Caller.SendAsync("Error", "Not authorized to control this session");
                    return;
                }

                _logger.LogInformation("DJ {DJUserId} playing song {TrackId} in session {SessionId}",
                    connection.UserId, trackId, sessionId);

                // Notify all session participants
                var listenerGroupName = GetListenerGroupName(sessionId);
                await Clients.Group(listenerGroupName).SendAsync("SongPlaying", new
                {
                    SessionId = sessionId,
                    TrackId = trackId,
                    Message = "DJ is now playing this song"
                });

                await Clients.Caller.SendAsync("SongPlayConfirmed", new
                {
                    TrackId = trackId,
                    Message = "Song play command sent"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error playing song {TrackId} in session {SessionId}", trackId, sessionId);
                await Clients.Caller.SendAsync("Error", "Failed to play song");
            }
        }

        #endregion

        #region Session Control

        /// <summary>
        /// DJ stops accepting requests for a session
        /// </summary>
        public async Task StopAcceptingRequests(string sessionId)
        {
            try
            {
                var connectionId = Context.ConnectionId;

                // Verify caller is the DJ for this session
                if (!_connections.TryGetValue(connectionId, out var connection) ||
                    connection.SessionId != sessionId ||
                    connection.Role != UserRole.DJ)
                {
                    await Clients.Caller.SendAsync("Error", "Not authorized to control this session");
                    return;
                }

                // Deactivate session
                var result = await _songRequestCacheService.DeactivateSessionAsync(sessionId);

                if (result.Success)
                {
                    _logger.LogInformation("DJ {DJUserId} stopped accepting requests for session {SessionId}",
                        connection.UserId, sessionId);

                    // Notify all listeners that requests are no longer being accepted
                    var listenerGroupName = GetListenerGroupName(sessionId);
                    await Clients.Group(listenerGroupName).SendAsync("RequestsStopped", new
                    {
                        SessionId = sessionId,
                        Message = "The DJ is no longer accepting song requests"
                    });

                    await Clients.Caller.SendAsync("RequestsStoppedConfirmed", new
                    {
                        SessionId = sessionId,
                        Message = "Session is no longer accepting requests"
                    });
                }
                else
                {
                    await Clients.Caller.SendAsync("Error", result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping requests for session {SessionId}", sessionId);
                await Clients.Caller.SendAsync("Error", "Failed to stop accepting requests");
            }
        }

        #endregion

        #region Private Helper Methods

        private async Task LeaveSession(string sessionId)
        {
            var connectionId = Context.ConnectionId;

            if (_connections.TryGetValue(connectionId, out var connection))
            {
                if (connection.Role == UserRole.Listener)
                {
                    var listenerGroupName = GetListenerGroupName(sessionId);
                    await Groups.RemoveFromGroupAsync(connectionId, listenerGroupName);

                    // Update listener count for DJ
                    await NotifyDJOfListenerCount(sessionId);
                }
                else if (connection.Role == UserRole.DJ)
                {
                    await LeaveDJSession(sessionId);
                }

                connection.SessionId = null;
                connection.Role = UserRole.None;
            }
        }

        public async Task NotifyDJOfNewRequests(string sessionId, int songsAdded, List<SongRequest> newSongs)
        {
            try
            {
                var djGroupName = GetDJGroupName(sessionId);
                await Clients.Group(djGroupName).SendAsync("NewSongRequests", new
                {
                    SessionId = sessionId,
                    SongsAdded = songsAdded,
                    NewSongs = newSongs.Select(s => new
                    {
                        TrackId = s.Song.Id,
                        TrackName = s.Song.Name,
                        Artists = string.Join(", ", s.Song.Artists?.Select(a => a.Name) ?? new[] { "Unknown" }),
                        Album = s.Song.Album?.Name,
                        Duration = s.Song.DurationMs,
                        s.RequestedAt,
                        s.RequesterName,
                        s.DeviceId
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying DJ of new requests for session {SessionId}", sessionId);
            }
        }

        public async Task NotifyDJOfListenerCount(string sessionId)
        {
            try
            {
                var listenerCount = _connections.Values.Count(c =>
                    c.SessionId == sessionId && c.Role == UserRole.Listener);

                var djGroupName = GetDJGroupName(sessionId);
                await Clients.Group(djGroupName).SendAsync("ListenerCountUpdate", new
                {
                    SessionId = sessionId,
                    ListenerCount = listenerCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating listener count for session {SessionId}", sessionId);
            }
        }

        private static string GetDJGroupName(string sessionId) => $"dj_session_{sessionId}";
        private static string GetListenerGroupName(string sessionId) => $"listeners_session_{sessionId}";

        #endregion

        #region Helper Classes

        private class UserConnection
        {
            public string ConnectionId { get; set; } = "";
            public string SessionId { get; set; }
            public string UserId { get; set; }
            public UserRole Role { get; set; } = UserRole.None;
            public DateTime ConnectedAt { get; set; }
            public string UserAgent { get; set; } = "";
        }

        private class SessionInfo
        {
            public string SessionId { get; set; } = "";
            public string DJUserId { get; set; } = "";
            public string DJConnectionId { get; set; } = "";
            public DateTime StartedAt { get; set; }
            public bool IsActive { get; set; }
        }

        private enum UserRole
        {
            None,
            DJ,
            Listener
        }

        #endregion
    }

    #region DTOs for SignalR Messages

    public class SongRequestNotification
    {
        public string SessionId { get; set; } = "";
        public string TrackId { get; set; } = "";
        public string TrackName { get; set; } = "";
        public string Artists { get; set; } = "";
        public string Album { get; set; }
        public int Duration { get; set; }
        public DateTime RequestedAt { get; set; }
        public string RequesterId { get; set; } = "";
    }

    #endregion
}