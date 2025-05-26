using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace BeatBallot.Web.Services
{
    public class NotificationHub : Hub
    {
        private static readonly ConcurrentDictionary<string, HashSet<string>> SessionGroups = new();
        private static readonly ConcurrentDictionary<string, string> ConnectionSessions = new();


        // Participant casts a vote (session-specific)
        public async Task CastVote(string sessionId, int songId, int newVoteCount)
        {
            // Update all participants in this session
            await Clients.Group($"Session_{sessionId}").SendAsync("UpdateSongVotes", songId, newVoteCount);

            // Update the DJ for this session
            await Clients.Group($"DJ_{sessionId}").SendAsync("UpdateSongVotes", songId, newVoteCount);
        }

        // DJ notifies session participants about new song
        public async Task NotifySessionNewSong(string sessionId, string title, string artist)
        {
            await Clients.Group($"Session_{sessionId}").SendAsync("NotifyNewSong", title, artist);
        }

        // DJ notifies session participants about play/pause
        public async Task NotifySessionPlayPause(string sessionId, bool isPlaying)
        {
            await Clients.Group($"Session_{sessionId}").SendAsync("NotifyPlayPause", isPlaying);
        }

        // DJ stops voting for their session
        public async Task NotifySessionVotingStopped(string sessionId)
        {
            await Clients.Group($"Session_{sessionId}").SendAsync("NotifyVotingStopped");

            // Clean up the session
            if (SessionGroups.ContainsKey(sessionId))
                SessionGroups.TryRemove(sessionId, out _);
        }

        // Legacy methods for backward compatibility
        public async Task SendVoteToUsers(string message)
        {
            // This can be removed or used for global announcements
            await Clients.All.SendAsync("SendVoteToUsers", message);
        }

        // Handle connection cleanup
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (ConnectionSessions.TryGetValue(Context.ConnectionId, out var sessionId))
            {
                // Participant disconnect
                if (SessionGroups.ContainsKey(sessionId))
                {
                    SessionGroups[sessionId].Remove(Context.ConnectionId);

                    // Update listener count
                    var listenerCount = SessionGroups[sessionId].Count;
                    await Clients.Groups($"Session_{sessionId}", $"DJ_{sessionId}").SendAsync("UpdateListenerCount", listenerCount);

                    // Clean up empty sessions
                    if (SessionGroups[sessionId].Count == 0)
                        SessionGroups.TryRemove(sessionId, out _);
                }

                ConnectionSessions.TryRemove(Context.ConnectionId, out _);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Get session statistics (for DJ dashboard)
        public async Task GetSessionStats(string sessionId)
        {
            if (SessionGroups.ContainsKey(sessionId))
            {
                var listenerCount = SessionGroups[sessionId].Count;
                await Clients.Caller.SendAsync("SessionStats", listenerCount);
            }
        }
    }
}
