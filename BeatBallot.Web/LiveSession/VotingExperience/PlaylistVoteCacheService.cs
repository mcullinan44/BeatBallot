using System.Collections.Concurrent;
using BeatBallot.Web.LiveSession.VotingExperience.DeeJay;
using BeatBallot.Web.LiveSession.VotingExperience.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;

namespace BeatBallot.Web.LiveSession.VotingExperience
{

    public class PlaylistVoteCache
    {
        public string PlaylistId { get; set; }

        public string SessionId { get; set; }
        public string PlaylistName { get; set; }
        public Dictionary<string, SongVoteInfo> Songs { get; set; } = new();
        public DateTime LastUpdated { get; set; }
        public string DjUserId { get; set; }
        public bool IsActive { get; set; }
    }

    public class SongVoteInfo
    {
        public string SongId { get; set; }
        public string SongName { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string ImageUrl { get; set; }
        public int VoteCount { get; set; }
        public HashSet<string> VoterIds { get; set; } = new(); // Track who voted
        public DateTime LastVoteTime { get; set; }
        public int Position { get; set; } // Position in playlist

        public bool IsPlaying { get; set; }
    }

    public class VoteRequest
    {
        public string PlaylistId { get; set; }
        public string SessionId { get; set; }
        public string SongId { get; set; }

    }

    public class VoteResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int NewVoteCountForSong { get; set; }
    }


    public interface IPlaylistVoteCacheService
    {
        Task<PlaylistVoteCache> GetPlaylistCacheAsync(string playlistId, string sessionId);


        Task<List<SongVoteInfo>> GetSongList(string playlistId, string sessionId);
        Task SetCurrentPlaylistAsync(string playlistId, string sessionId, List<ManageLiveComponent.PlaylistSongModel> songs, string djUserId);
        Task<VoteResult> CastVoteAsync(VoteRequest voteRequest);
        Task<bool> RemoveVoteAsync(string playlistId, string sessionId, string songId, string userId);
        Task<Dictionary<string, int>> GetVoteCountsAsync(string playlistId, string sessionId);
        Task ClearPlaylistCacheAsync(string playlistId, string sessionId);
        Task<List<SongVoteInfo>> GetTopVotedSongsAsync(string playlistId, string sessionId, int count = 10);


        Task<SongVoteInfo> GetCurrentSong(string playlistId, string sessionId);
    }


    public class PlaylistVoteCacheService : IPlaylistVoteCacheService
    {
        private readonly IMemoryCache _memoryCache;
        private readonly IHubContext<VotingHub> _hubContext;
        private readonly ILogger<PlaylistVoteCacheService> _logger;
        private readonly ConcurrentDictionary<string, object> _lockObjects = new();
        private readonly object _lockObject = new();

        // Cache keys
        private const string PlaylistCachePrefix = "playlist_votes_";
        private const string ActivePlaylistsKey = "active_playlists";

        public PlaylistVoteCacheService(
            IMemoryCache memoryCache,
            IHubContext<VotingHub> hubContext
           )
        {
            _memoryCache = memoryCache;
            _hubContext = hubContext;
            //_logger = logger;
        }


        public async Task<List<SongVoteInfo>> GetSongList(string playlistId, string sessionId)
        {
            var cacheKey = $"{PlaylistCachePrefix}{playlistId}{sessionId}";
            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out PlaylistVoteCache cachedPlaylist))
                {
                    return cachedPlaylist.Songs.Values.ToList();
                }
            }

            return new List<SongVoteInfo>();
        }

        public async Task<SongVoteInfo> GetCurrentSong(string playlistId, string sessionId)
        {
            var cacheKey = $"{PlaylistCachePrefix}{playlistId}{sessionId}";
            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out PlaylistVoteCache cachedPlaylist))
                {
                    return cachedPlaylist.Songs.FirstOrDefault(i => i.Value.IsPlaying).Value;
                }
            }
            return new SongVoteInfo();
        }


        public async Task<PlaylistVoteCache> GetPlaylistCacheAsync(string playlistId, string sessionId)
        {
            var cacheKey = $"{PlaylistCachePrefix}{playlistId}{sessionId}";
            lock (_lockObject)
            {
                if (_memoryCache.TryGetValue(cacheKey, out PlaylistVoteCache cachedPlaylist))
                {
                    return cachedPlaylist;
                }
            }

            return null;
        }

        public async Task SetCurrentPlaylistAsync(string playlistId, string sessionId, List<ManageLiveComponent.PlaylistSongModel> songs, string djUserId)
        {
            var lockObject = _lockObjects.GetOrAdd(playlistId, _ => new object());
            lock (lockObject)
            {
                var cacheKey = $"{PlaylistCachePrefix}{playlistId}{sessionId}";

                var playlistCache = new PlaylistVoteCache
                {
                    PlaylistId = playlistId,
                    SessionId = sessionId,
                    PlaylistName = songs.FirstOrDefault()?.Album ?? "DJ Playlist",
                    DjUserId = djUserId,
                    IsActive = true,
                    LastUpdated = DateTime.UtcNow
                };

                // Initialize songs with zero votes
                for (var i = 0; i < songs.Count; i++)
                {
                    var song = songs[i];
                    playlistCache.Songs[song.Id] = new SongVoteInfo
                    {
                        SongId = song.Id,
                        SongName = song.Title,
                        Artist = song.Artist,
                        Album = song.Album,
                        ImageUrl = song.ImageUrl,
                        VoteCount = 0,
                        Position = i + 1,
                        LastVoteTime = DateTime.UtcNow
                    };
                }

                // Cache for 24 hours (or until DJ session ends)
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24),
                    SlidingExpiration = TimeSpan.FromHours(6),
                    Priority = CacheItemPriority.High
                };

                try
                {
                    _memoryCache.Set(cacheKey, playlistCache, cacheOptions);

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
                // Track active playlists
                UpdateActivePlaylistsList(playlistId, true);
            }

            // Notify clients of new playlist
            await _hubContext.Clients.All.SendAsync("PlaylistActivated", playlistId);

        }

        public async Task<VoteResult> CastVoteAsync(VoteRequest voteRequest)
        {
            var lockObject = _lockObjects.GetOrAdd(voteRequest.PlaylistId, _ => new object());
            lock (lockObject)
            {
                var playlist =  GetPlaylistCacheSync(voteRequest.PlaylistId, voteRequest.SessionId);
                if (playlist == null)
                {
                    return new VoteResult
                    {
                        Success = false,
                        Message = "Playlist not found or no longer active"
                    };
                }

                if (!playlist.Songs.TryGetValue(voteRequest.SongId, out var songInfo))
                {
                    return new VoteResult
                    {
                        Success = false,
                        Message = "Song not found in current playlist"
                    };
                }

                // Add vote
                songInfo.VoteCount++;
                songInfo.LastVoteTime = DateTime.UtcNow;
                playlist.LastUpdated = DateTime.UtcNow;
                // Update cache
                var cacheKey = $"{PlaylistCachePrefix}{voteRequest.PlaylistId}{voteRequest.SessionId}";
                _memoryCache.Set(cacheKey, playlist);

                return new VoteResult
                {
                    Success = true,
                    Message = "Vote cast successfully",
                    NewVoteCountForSong = songInfo.VoteCount
                    
                };
            }
        }

        public async Task<bool> RemoveVoteAsync(string playlistId, string sessionId, string songId, string userId)
        {
            var lockObject = _lockObjects.GetOrAdd(playlistId, _ => new object());

            lock (lockObject)
            {
                var playlist = GetPlaylistCacheSync(playlistId, sessionId);
                if (playlist?.Songs.TryGetValue(songId, out var songInfo) == true)
                {
                    if (songInfo.VoterIds.Remove(userId))
                    {
                        songInfo.VoteCount = Math.Max(0, songInfo.VoteCount - 1);
                        playlist.LastUpdated = DateTime.UtcNow;

                        var cacheKey = $"{PlaylistCachePrefix}{playlistId}";
                        _memoryCache.Set(cacheKey, playlist);

                        return true;
                    }
                }

                return false;
            }
        }

        public async Task<Dictionary<string, int>> GetVoteCountsAsync(string playlistId, string sessionId)
        {
            var playlist = await GetPlaylistCacheAsync(playlistId, sessionId);
            if (playlist == null)
                return new Dictionary<string, int>();

            return playlist.Songs.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.VoteCount
            );
        }

        public async Task<List<SongVoteInfo>> GetTopVotedSongsAsync(string playlistId, string sessionId, int count = 10)
        {
            var playlist = await GetPlaylistCacheAsync(playlistId, sessionId);
            if (playlist == null)
                return new List<SongVoteInfo>();

            return playlist.Songs.Values
                .OrderByDescending(s => s.VoteCount)
                .ThenBy(s => s.Position)
                .Take(count)
                .ToList();
        }

        public async Task ClearPlaylistCacheAsync(string playlistId, string sessionId)
        {
            var cacheKey = $"{PlaylistCachePrefix}{playlistId}{sessionId}";
            _memoryCache.Remove(cacheKey);
            _lockObjects.TryRemove(playlistId, out _);

            UpdateActivePlaylistsList(playlistId, false);

            await _hubContext.Clients.All.SendAsync("PlaylistDeactivated", playlistId);

            _logger.LogInformation("Playlist cache cleared for {PlaylistId}", playlistId);
        }

        private PlaylistVoteCache GetPlaylistCacheSync(string playlistId, string sessionId)
        {
            var cacheKey = $"{PlaylistCachePrefix}{playlistId}{sessionId}";
            return _memoryCache.TryGetValue(cacheKey, out PlaylistVoteCache playlist) ? playlist : null;
        }

        private void UpdateActivePlaylistsList(string playlistId, bool isActive)
        {
            var lockObject = _lockObjects.GetOrAdd(playlistId, _ => new object());

            lock (lockObject)
            {

                var activePlaylists = _memoryCache.GetOrCreate(ActivePlaylistsKey,
                    _ => new HashSet<string>()) as HashSet<string> ?? new HashSet<string>();

                if (isActive)
                    activePlaylists.Add(playlistId);
                else
                    activePlaylists.Remove(playlistId);

                _memoryCache.Set(ActivePlaylistsKey, activePlaylists);
            }
        }
    }
}
