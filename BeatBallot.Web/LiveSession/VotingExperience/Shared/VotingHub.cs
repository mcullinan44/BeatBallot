using System.Collections.Concurrent;
using BeatBallot.Web.LiveSession.VotingExperience;
using Microsoft.AspNetCore.SignalR;

namespace BeatBallot.Web.LiveSession.VotingExperience.Shared
{
    public class VotingHub : Hub
    {
        private static readonly ConcurrentDictionary<string, HashSet<string>> SessionGroups = new();
        private static readonly ConcurrentDictionary<string, UserSession> ConnectedUsers = new();
        private static readonly ConcurrentDictionary<string, string> ConnectionToGroup = new();

        private readonly IPlaylistVoteCacheService _cacheService;

        public VotingHub(IPlaylistVoteCacheService cacheService)
        {
            _cacheService = cacheService;
        }



        // DJ stops voting for their session
        public async Task NotifySessionVotingStopped(string playlistId, string sessionId)
        {
            var groupName = GetPlaylistGroupName(playlistId, sessionId);

            await Clients.Group(groupName).SendAsync("NotifyVotingStopped");

            // Clean up the session
            if (SessionGroups.ContainsKey(sessionId))
                SessionGroups.TryRemove(sessionId, out _);
        }


        public async Task NotifyTakingRequestsNewSongRequested(string sessionId, string songId)
        {
            var groupName = GetTakingRequestsGroupName(sessionId);
            await Clients.Group(groupName).SendAsync("NotifyNewSongRequested", songId);

        }

        // Automatic cleanup when users disconnect - NOW WORKS!
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var connectionId = Context.ConnectionId;

            // Get the group this connection belongs to
            if (ConnectionToGroup.TryRemove(connectionId, out var groupName))
            {
                await RemoveFromGroup(connectionId, groupName);
            }

            // Clean up user session info
            ConnectedUsers.TryRemove(connectionId, out var userSession);

            await base.OnDisconnectedAsync(exception);
        }


        private async Task RemoveFromGroup(string connectionId, string groupName)
        {
            // Remove from SignalR group
            await Groups.RemoveFromGroupAsync(connectionId, groupName);

            // Remove from SessionGroups tracking
            if (SessionGroups.TryGetValue(groupName, out var connections))
            {
                connections.Remove(connectionId);

                // Get user info for notification
                var userSession = ConnectedUsers.GetValueOrDefault(connectionId);
                var userId = userSession?.UserId ?? "unknown";
                var userName = userSession?.UserName ?? "Unknown User";

                // Send updated user count to remaining users
                var userCount = connections.Count;
                await Clients.Group(groupName).SendAsync("UpdateListenerCount", userCount);

                // Notify other users that someone left
                await Clients.OthersInGroup(groupName).SendAsync("UserLeft", userId, userName);

                // Clean up empty groups
                if (connections.Count == 0)
                {
                    SessionGroups.TryRemove(groupName, out _);
                    Console.WriteLine($"Removed empty group: {groupName}");
                }

                Console.WriteLine($"User {userId} ({connectionId}) left group {groupName}. Remaining users: {userCount}");
            }

            // Remove connection to group mapping
            ConnectionToGroup.TryRemove(connectionId, out _);
        }

        public static string GetPlaylistGroupName(string playlistId, string sessionId)
        {
            return $"playlistId_{playlistId}_sessionId_{sessionId}";
        }

        public static string GetTakingRequestsGroupName(string sessionId)
        {
            return $"sessionId_{sessionId}";
        }

        public async Task JoinPlaylistGroup(string playlistId, string sessionId)
        {
            var connectionId = Context.ConnectionId;
            var groupName = GetPlaylistGroupName(playlistId, sessionId);

            // Add to SignalR group
            await Groups.AddToGroupAsync(connectionId, groupName);

            ConnectionToGroup[connectionId] = groupName;

            // Track in SessionGroups dictionary
            SessionGroups.AddOrUpdate(
                groupName,
                new HashSet<string> { connectionId },
                (key, existing) =>
                {
                    existing.Add(connectionId);
                    return existing;
                });
            ConnectedUsers[connectionId] = new UserSession
            {
                ConnectionId = connectionId,
                UserId = "anonymous",
                UserName = "Anonymous User",
                PlaylistId = playlistId,
                SessionId = sessionId,
                JoinedAt = DateTime.UtcNow
            };
            var numberOfUsersInSession = SessionGroups[groupName].Count;
            await Clients.Group(groupName).SendAsync("UpdateListenerCount", numberOfUsersInSession);
        }



        public async Task JoinTakingRequestsGroup(string sessionId)
        {
            var connectionId = Context.ConnectionId;
            var groupName = GetTakingRequestsGroupName(sessionId);

            // Add to SignalR group
            await Groups.AddToGroupAsync(connectionId, groupName);

            ConnectionToGroup[connectionId] = groupName;

            // Track in SessionGroups dictionary
            SessionGroups.AddOrUpdate(
                groupName,
                new HashSet<string> { connectionId },
                (key, existing) =>
                {
                    existing.Add(connectionId);
                    return existing;
                });
            ConnectedUsers[connectionId] = new UserSession
            {
                ConnectionId = connectionId,
                UserId = "anonymous",
                UserName = "Anonymous User",
                SessionId = sessionId,
                JoinedAt = DateTime.UtcNow
            };
            var numberOfUsersInSession = SessionGroups[groupName].Count;
            await Clients.Group(groupName).SendAsync("UpdateListenerCount", numberOfUsersInSession);
        }


        public async Task CastVote(VoteRequest voteRequest)
        {
            var result = await _cacheService.CastVoteAsync(voteRequest);
            var groupName = GetPlaylistGroupName(voteRequest.PlaylistId, voteRequest.SessionId);
            // Send result to the voter
            await Clients.Caller.SendAsync("VoteCountUpdated", voteRequest.SongId, result.NewVoteCountForSong);

            if (result.Success)
            {
                // Broadcast updated vote count to all users in the playlist group
                await Clients.OthersInGroup(groupName).SendAsync("VoteCountUpdated", voteRequest.SongId, result.NewVoteCountForSong);
            }
        }
    }

    // User session tracking model
    public class UserSession
    {
        public string ConnectionId { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string PlaylistId { get; set; }

        public string SessionId { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
}
