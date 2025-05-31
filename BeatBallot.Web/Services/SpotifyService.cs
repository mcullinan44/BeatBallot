using BeatBallot.Web.Services;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;
using System.Net.Http.Headers;

namespace BeatBallot.Services
{
    public class SpotifyService
    {
        private string _accessToken;

        private SpotifyClient _spotifyClient;
        private readonly ITokenProvider _tokenProvider;

        public readonly Guid _guid;

        private readonly ILogger<SpotifyService> _logger;

        private readonly Blazored.LocalStorage.ILocalStorageService _localStorage;


        public SpotifyService(ITokenProvider tokenProvider, ILogger<SpotifyService> logger)
        {
            _tokenProvider = tokenProvider;
            _logger = logger;
            _guid = Guid.NewGuid();

            _logger.Log(LogLevel.Information, $"Spotify Service Instance Created: {_guid}");
        }

        private async Task<SpotifyClient?> GetClientAsync()
        {
            if (_spotifyClient == null)
            {
                var accessToken = await _tokenProvider.GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(accessToken))
                {
                    _spotifyClient = new SpotifyClient(accessToken);
                }
            }
            return _spotifyClient;
        }

        public async Task<bool> GetHasValidToken()
        {
            return await GetClientAsync() != null;
        }

        public async Task<Paging<FullPlaylist>> GetPlaylistsAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return await client.Playlists.CurrentUsers();
        }

        public async Task<Paging<PlaylistTrack<IPlayableItem>>> GetSongsforPlaylistAsync(string playListId)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return await client.Playlists.GetItems(playListId);
        }

        public async Task<DeviceResponse> GetAvailableDevicesAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return await client.Player.GetAvailableDevices();
        }


        public async Task<PrivateUser> GetUserName()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return await client.UserProfile.Current();
        }

        public async Task PausePlaybackAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            await client.Player.PausePlayback();
        }

        public async Task PlaySongAsync(string trackUri)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");


            try
            {
                PlayerResumePlaybackRequest playbackRequest = new PlayerResumePlaybackRequest
                {
                    Uris = new List<string> { trackUri }
                };
                await client.Player.ResumePlayback(playbackRequest);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An error occurred playing a song");

                if(e.Message.ToLower().Contains("no active device found"))
                {
                    throw new Exception(e.Message, e);
                }
            }
        }

        private async Task ActivateDeviceAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            DeviceResponse devices = await _spotifyClient.Player.GetAvailableDevices();
            var deviceId = devices.Devices.FirstOrDefault()?.Id;
            if (deviceId != null)
            {
                PlayerTransferPlaybackRequest transferRequest = new PlayerTransferPlaybackRequest(new List<string> { deviceId });
                await _spotifyClient.Player.TransferPlayback(transferRequest);
            }

        }

        public async Task AddToQueueAsync(List<string> trackUris, string accessToken)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            HttpClient httpClient = new HttpClient();
            foreach (string trackUri in trackUris)
            {
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"https://api.spotify.com/v1/me/player/queue?uri={trackUri}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
            }
        }


        public async Task<QueueResponse> GetQueueAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return await client.Player.GetQueue();
        }

        public async Task<SearchResponse> SearchAsync(string query)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            SearchRequest searchRequest = new SearchRequest(SearchRequest.Types.All, query);
            Task<SearchResponse> response = client.Search.Item(searchRequest);

            return response.Result;
        }
    }
}