using BeatBallot.Web.Services.Local;
using SpotifyAPI.Web;
using System.Net.Http.Headers;

namespace BeatBallot.Web.Services.Spotify
{
    public class SpotifyService
    {
        private string _accessToken;

        private SpotifyClient _spotifyClient;
        private readonly ITokenProvider _tokenProvider;

        public readonly Guid Guid;

        private readonly ILogger<SpotifyService> _logger;

        private readonly Blazored.LocalStorage.ILocalStorageService _localStorage;


        public SpotifyService(ITokenProvider tokenProvider, ILogger<SpotifyService> logger)
        {
            _tokenProvider = tokenProvider;
            _logger = logger;
            Guid = Guid.NewGuid();

            _logger.Log(LogLevel.Information, $"Spotify Service Instance Created: {Guid}");
        }

        private async Task<SpotifyClient> GetClientAsync()
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



        public async Task<FullTrack> GetTrackAsync(string trackId)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");



            return await client.Tracks.Get(trackId);
        }

        public string GetAlbumImageUrl(FullTrack track)
        {
            var client = GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return track.Album?.Images?.OrderByDescending(img => img.Width).FirstOrDefault()?.Url ?? string.Empty;
        }


        public async Task<PrivateUser> GetUser()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            return await client.UserProfile.Current();
        }


        // Enhanced SpotifyService methods for device management

        public async Task<DeviceResponse> GetAvailableDevicesAsync()
        {
            try
            {
                var client = await GetClientAsync();
                if (client == null)
                    throw new UnauthorizedAccessException("No valid Spotify token available");

                var devices = await client.Player.GetAvailableDevices();
                return new DeviceResponse { Success = true, Devices = devices.Devices };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting devices: {ex.Message}");
                return new DeviceResponse { Success = false, Message = ex.Message };
            }
        }

        public async Task<bool> ActivateDeviceAsync(string deviceId)
        {
            try
            {
                var client = await GetClientAsync();
                if (client == null)
                    throw new UnauthorizedAccessException("No valid Spotify token available");
                await client.Player.TransferPlayback(new PlayerTransferPlaybackRequest(new[] { deviceId })
                {
                    Play = false // Don't start playing immediately
                });

                // Wait a moment for the transfer to complete
                await Task.Delay(1000);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error activating device: {ex.Message}");
                return false;
            }
        }

        public async Task<PlaySongResponse> PlaySongWithDeviceCheckAsync(string spotifyUri)
        {
            try
            {

                var client = await GetClientAsync();
                if (client == null)
                    throw new UnauthorizedAccessException("No valid Spotify token available");
                // First, try to play normally
                try
                {
                    await client.Player.ResumePlayback(new PlayerResumePlaybackRequest
                    {
                        Uris = new List<string> { spotifyUri }
                    });
                    return new PlaySongResponse { Success = true };
                }
                catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // No active device, try to find and activate one
                    Console.WriteLine("No active device found, attempting to activate a device...");

                    var deviceResponse = await GetAvailableDevicesAsync();
                    if (!deviceResponse.Success || !deviceResponse.Devices.Any())
                    {
                        return new PlaySongResponse
                        {
                            Success = false,
                            Message = "No Spotify devices available. Please open Spotify on a device first.",
                            RequiresUserAction = true
                        };
                    }

                    // Try to activate the first available device
                    var device = deviceResponse.Devices.First();
                    var activated = await ActivateDeviceAsync(device.Id);

                    if (!activated)
                    {
                        return new PlaySongResponse
                        {
                            Success = false,
                            Message = "Failed to activate Spotify device. Please manually start Spotify.",
                            RequiresUserAction = true
                        };
                    }

                    // Now try to play again
                    await client.Player.ResumePlayback(new PlayerResumePlaybackRequest
                    {
                        DeviceId = device.Id,
                        Uris = new List<string> { spotifyUri }
                    });

                    return new PlaySongResponse
                    {
                        Success = true,
                        Message = $"Activated device '{device.Name}' and started playing.",
                        ActivatedDevice = device.Name
                    };
                }
            }
            catch (Exception ex)
            {
                return new PlaySongResponse
                {
                    Success = false,
                    Message = $"Error playing song: {ex.Message}"
                };
            }
        }

        public async Task<PlaySongResponse> PausePlaybackWithDeviceCheckAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");
            try
            {
                var playback = await client.Player.GetCurrentPlayback();

                if (playback == null || !playback.IsPlaying)
                {
                    return new PlaySongResponse
                    {
                        Success = false,
                        Message = "No active playback to pause."
                    };
                }

                await client.Player.PausePlayback(new PlayerPausePlaybackRequest
                {
                    DeviceId = playback.Device?.Id
                });

                return new PlaySongResponse { Success = true };
            }
            catch (Exception ex)
            {
                return new PlaySongResponse
                {
                    Success = false,
                    Message = $"Error pausing playback: {ex.Message}"
                };
            }
        }
        public async Task<CurrentlyPlayingContext?> GetCurrentPlaybackAsync()
        {
            try
            {
                var client = await GetClientAsync();
                if (client == null)
                    throw new UnauthorizedAccessException("No valid Spotify token available");
                return await client.Player.GetCurrentPlayback();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting current playback: {ex.Message}");
                return null;
            }
        }
        public async Task<PlaySongResponse> ResumePlaybackWithDeviceCheckAsync()
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            try
            {
                var playback = await client.Player.GetCurrentPlayback();

                if (playback?.Device?.Id != null)
                {
                    await client.Player.ResumePlayback(new PlayerResumePlaybackRequest
                    {
                        DeviceId = playback.Device.Id
                    });
                    return new PlaySongResponse { Success = true };
                }

                // No active device, try to activate one
                var deviceResponse = await GetAvailableDevicesAsync();
                if (!deviceResponse.Success || !deviceResponse.Devices.Any())
                {
                    return new PlaySongResponse
                    {
                        Success = false,
                        Message = "No Spotify devices available.",
                        RequiresUserAction = true
                    };
                }

                var device = deviceResponse.Devices.First();
                await ActivateDeviceAsync(device.Id);

                await _spotifyClient.Player.ResumePlayback(new PlayerResumePlaybackRequest
                {
                    DeviceId = device.Id
                });

                return new PlaySongResponse
                {
                    Success = true,
                    Message = $"Activated device '{device.Name}' and resumed playback.",
                    ActivatedDevice = device.Name
                };
            }
            catch (Exception ex)
            {
                return new PlaySongResponse
                {
                    Success = false,
                    Message = $"Error resuming playback: {ex.Message}"
                };
            }
        }

        // Response classes
        public class DeviceResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public List<Device> Devices { get; set; } = new();
        }

        public class PlaySongResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public bool RequiresUserAction { get; set; }
            public string? ActivatedDevice { get; set; }
        }

        public async Task<bool> PausePlaybackAsync()
        {
            try
            {

                var client = await GetClientAsync();
                if (client == null)
                    throw new UnauthorizedAccessException("No valid Spotify token available");
                // First check if there's an active playback session
                var playback = await client.Player.GetCurrentPlayback();

                if (playback == null || !playback.IsPlaying)
                {
                    // No active playback or already paused
                    return false;
                }

                // Only pause if there's an active device
                if (playback.Device?.Id != null)
                {
                    await client.Player.PausePlayback(new PlayerPausePlaybackRequest
                    {
                        DeviceId = playback.Device.Id
                    });
                    return true;
                }

                // Try without device ID as fallback
                await client.Player.PausePlayback();
                return true;
            }
            catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // No active device found
                Console.WriteLine("No active Spotify device found");
                return false;
            }
            catch (APIException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // Premium subscription required
                Console.WriteLine("Spotify Premium required for playback control");
                return false;
            }
            catch (Exception ex)
            {
                // Log the error but don't crash the app
                Console.WriteLine($"Error pausing playback: {ex.Message}");
                return false;
            }
        }


        public async Task<bool> ResumePlaybackAsync()
        {
            try
            {
                var playback = await _spotifyClient.Player.GetCurrentPlayback();

                if (playback?.Device?.Id != null)
                {
                    await _spotifyClient.Player.ResumePlayback(new PlayerResumePlaybackRequest
                    {
                        DeviceId = playback.Device.Id
                    });
                    return true;
                }

                // Fallback without device ID
                await _spotifyClient.Player.ResumePlayback();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resuming playback: {ex.Message}");
                return false;
            }
        }



        public async Task PlaySongAsync(string trackUri)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");


            try
            {
                var playbackRequest = new PlayerResumePlaybackRequest
                {
                    Uris = new List<string> { trackUri }
                };
                await client.Player.ResumePlayback(playbackRequest);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An error occurred playing a song");

                if (e.Message.ToLower().Contains("no active device found"))
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

            var devices = await _spotifyClient.Player.GetAvailableDevices();
            var deviceId = devices.Devices.FirstOrDefault()?.Id;
            if (deviceId != null)
            {
                var transferRequest =
                    new PlayerTransferPlaybackRequest(new List<string> { deviceId });
                await _spotifyClient.Player.TransferPlayback(transferRequest);
            }

        }

        public async Task AddToQueueAsync(List<string> trackUris, string accessToken)
        {
            var client = await GetClientAsync();
            if (client == null)
                throw new UnauthorizedAccessException("No valid Spotify token available");

            var httpClient = new HttpClient();
            foreach (var trackUri in trackUris)
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


    }

}
