using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using SpotifyAPI.Web;

namespace BeatBallot.Services
{
    public class SpotifyService: ISpotifyService
    {
        private string _accessToken;

        private SpotifyClient _spotifyClient;

        public readonly Guid _guid;

        private readonly ILogger<SpotifyService> _logger;

        public SpotifyService(ILogger<SpotifyService> logger)
        {
            this._guid = Guid.NewGuid();
            this._logger = logger;

            _logger.Log(LogLevel.Information, $"Spotify Service Instance Created:  {this._guid.ToString()}");
        }
        public string AccessToken
        {
            get => _accessToken;
            set
            {
                this._accessToken = value;
                _spotifyClient = new SpotifyClient(this._accessToken);
            }
        }

        public async Task<Paging<FullPlaylist>> GetPlaylistsAsync()
        {
            return await _spotifyClient.Playlists.CurrentUsers();
        }

        public async Task<Paging<PlaylistTrack<IPlayableItem>>> GetSongsforPlaylistAsync(string playListId)
        {
            return await _spotifyClient.Playlists.GetItems(playListId);
        }

        public async Task<DeviceResponse> GetAvailableDevicesAsync()
        {
            return await _spotifyClient.Player.GetAvailableDevices();
        }

        public async Task PausePlaybackAsync()
        {
            await _spotifyClient.Player.PausePlayback();
        }

        public async Task PlaySongAsync(string trackUri)
        {

            try
            {
                PlayerResumePlaybackRequest playbackRequest = new PlayerResumePlaybackRequest
                {
                    Uris = new List<string> { trackUri }
                };
                await _spotifyClient.Player.ResumePlayback(playbackRequest);
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
            return await _spotifyClient.Player.GetQueue();
        }

        public async Task<SearchResponse> SearchAsync(string query)
        {
            SearchRequest searchRequest = new SearchRequest(SearchRequest.Types.All, query);
            Task<SearchResponse> response = _spotifyClient.Search.Item(searchRequest);

            return response.Result;
        }
    }
}