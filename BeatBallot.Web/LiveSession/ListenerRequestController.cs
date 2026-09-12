using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.LiveSession.TakeRequests;
using BeatBallot.Web.LiveSession.TakeRequests.Shared;
using BeatBallot.Web.Services;
using BeatBallot.Web.Services.Local;
using BeatBallot.Web.Services.Local.BeatBallot.Web.Services.Local;
using BeatBallot.Web.Services.Spotify;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SpotifyAPI.Web;
using SpotifyAPI.Web;
using Stripe.Forwarding;
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text;


namespace BeatBallot.Web.LiveSession
{
    [ApiController]
    [Route("/api/listenerrequest")]
    public class ListenerRequestController : ControllerBase
    {


        private readonly SpotifySearchService _spotifySearchService;
        private readonly SpotifyService _spotifyService;
        private readonly ISongRequestCacheService _songRequestCacheService;
        private readonly ILogger<ListenerRequestController> _logger;
        private readonly IDeviceService _deviceService;
        private readonly IHubContext<SongRequestHub> _hubContext;

        public ListenerRequestController(SpotifySearchService spotifySearchService
            ,ISongRequestCacheService songRequestCacheService
            ,SpotifyService spotifyService
            ,ILogger<ListenerRequestController> logger
            ,IDeviceService deviceService
            ,IHubContext<SongRequestHub> hubContext)
        {
            this._songRequestCacheService = songRequestCacheService;
            this._spotifySearchService = spotifySearchService;
            _spotifyService = spotifyService;
            _logger = logger;
            _hubContext = hubContext;
            _deviceService = deviceService;
        }


 
        [HttpGet("checkIsDeviceInSession")]
        public async Task<IActionResult> CheckIsDeviceInSession([FromQuery] string sessionId, [FromQuery] string deviceId)
        {
            if (await _deviceService.IsDeviceInSessionAsync(deviceId, sessionId))
                return Ok(new
                {
                    inSession = true
                });
            return Ok(new
            {
                inSession = false
            });
        }

        [HttpPost("addDeviceToSession")]
        public async Task<IActionResult> AddDeviceToSession([FromQuery] string sessionId, [FromQuery] string deviceId)
        {

            if (await _deviceService.AddDeviceToSessionAsync(deviceId, sessionId))
                return Ok(new { added = true});
            return BadRequest();
        }


        [HttpGet("validatesession/{sessionId}")]
        public async Task<IActionResult> ValidateSession(string sessionId)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionId))
                {
                    return BadRequest(new { isActive = false, message = "Session ID is required" });
                }

                // Check if session exists and is accepting requests
                var isActive = await _songRequestCacheService.IsSessionActiveAsync(sessionId);

                _logger.LogInformation("Session validation for {SessionId}: {IsActive}", sessionId, isActive);

                return Ok(new { isActive });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating session {SessionId}", sessionId);
                return StatusCode(500, new { isActive = false, message = "Error validating session" });
            }
        }

        [HttpPost("registerdevice")]
        public async Task<IActionResult> RegisterDevice([FromBody] SimpleDeviceInfo deviceInfo)
        {
            try
            {
                if (deviceInfo == null)
                {
                    return BadRequest(new { success = false, message = "Device info is required" });
                }
                if (string.IsNullOrEmpty(deviceInfo.SessionId))
                {
                    return BadRequest(new { success = false, message = "Session ID is required" });
                }

                // Generate DeviceID hash from the DTO
                var deviceDataString = $"{deviceInfo.UserAgent}|{deviceInfo.Browser}|{deviceInfo.ScreenSize}|{deviceInfo.IsMobile}|{deviceInfo.SessionId}";
                var deviceIdHash = ComputeHash(deviceDataString);
                var deviceId = $"beatballot_device_{deviceIdHash}";

                _logger.LogInformation("Device registered for session {SessionId}: {DeviceId} - {Browser} - Mobile: {IsMobile} - Screen: {ScreenSize}",
                    deviceInfo.SessionId, deviceId, deviceInfo.Browser, deviceInfo.IsMobile, deviceInfo.ScreenSize);

                await _deviceService.AddDeviceToSessionAsync(deviceId, deviceInfo.SessionId);
                
                return Ok(new
                {
                    success = true,
                    deviceId = deviceId,
                    message = "Device registered successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering device for session {SessionId}", deviceInfo?.SessionId);
                return StatusCode(500, new { success = false, message = "Failed to register device" });
            }
        }

        private string ComputeHash(string input)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(input);
                var hashBytes = sha256.ComputeHash(bytes);
                return Convert.ToHexString(hashBytes)[..12]; // Take first 12 characters
            }
        }

        [HttpPost("search/{sessionId}")]
        public async Task<IActionResult> Search([FromBody] SearchRequest request, [FromRoute] string sessionId)
        {
            try
            {
                var isActive = await _songRequestCacheService.IsSessionActiveAsync(sessionId);
                if (!isActive)
                {
                    return BadRequest();
                }


                // Validate input
                if (request == null || string.IsNullOrWhiteSpace(request.Query))
                {
                    return BadRequest(new
                    {
                        tracks = new List<object>(),
                        error = "Search query is required"
                    });
                }

                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    return BadRequest(new
                    {
                        tracks = new List<object>(),
                        error = "Session ID is required"
                    });
                }

                // Use your existing SpotifySearchService (same as your Blazor component)
                var tracks = await _spotifySearchService.GetTracksListAsync(
                    request.Query,
                    request.Limit ?? 20,
                    0,
                    null,
                    sessionId
                );

                // Transform tracks to match what your JavaScript expects
                var transformedTracks = tracks?.Select(track => new
                {
                    id = track.Id,
                    name = track.Name,
                    artistNames = SpotifySearchService.GetAllArtistNames(track),
                    albumName = track.Album?.Name,
                    albumImageUrl = _spotifySearchService.GetAlbumImageUrl(track),
                    isExplicit = track.Explicit,
                    durationMs = track.DurationMs,
                    uri = track.Uri,
                    fullTrack = track
                    
                }).ToList();

                _logger.LogInformation(
                    "Search performed for query '{Query}' in session {SessionId}, returned {Count} results",
                    request.Query, sessionId, transformedTracks?.Count);

                return Ok(new
                {
                    tracks = transformedTracks
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching for tracks with query '{Query}' in session {SessionId}",
                    request?.Query, sessionId);

                return StatusCode(500, new
                {
                    tracks = new List<object>(),
                    error = "Search failed. Please try again."
                });
            }
        }



        [HttpPost("submit")]
        public async Task<IActionResult> SubmitRequests([FromBody] SimpleSongRequest simpleSongRequest)
        {
            try
            {
                // Validate request
                if (simpleSongRequest == null || !simpleSongRequest.Songs.Any())
                {
                    return BadRequest(new { success = false, message = "No songs provided in request" });
                }

                if (string.IsNullOrEmpty(simpleSongRequest.SessionId))
                {
                    return BadRequest(new { success = false, message = "Session ID is required" });
                }


                // Check if session is active
                var isActive = await _songRequestCacheService.IsSessionActiveAsync(simpleSongRequest.SessionId);
                if (!isActive)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "This session is not currently accepting requests"
                    });
                }

                // Convert SimpleSongRequest to SongRequestList with minimal FullTrack objects
                var songRequestList = new SongRequestList
                {
                    SessionId = simpleSongRequest.SessionId,
                    RequestedAt = simpleSongRequest.RequestedAt,
                    RequesterName = simpleSongRequest.RequesterName,
                    DeviceId = simpleSongRequest.DeviceId,
                    Songs = simpleSongRequest.Songs.Select(s => new SongRequest
                    {
                        SessionId = simpleSongRequest.SessionId,
                        RequestedAt = simpleSongRequest.RequestedAt,
                        RequesterName = simpleSongRequest.RequesterName,
                        DeviceId = simpleSongRequest.DeviceId,
                        Song = CreateMinimalFullTrack(s) // 👈 Create minimal FullTrack
                    }).ToList()
                };

                // Submit to cache service
                var result = await _songRequestCacheService.RequestSongs(songRequestList);

                if (result.Success)
                {
                    _logger.LogInformation("Song requests submitted for session {SessionId}: {SongsAdded} added, {Duplicates} duplicates",
                        songRequestList.SessionId, result.SongsAdded, result.DuplicateSongs.Count);

                    // Notify DJ of new song requests via SignalR if any songs were added
                    if (result.SongsAdded > 0)
                    {
                        await NotifyDJOfNewRequests(songRequestList.SessionId, result.SongsAdded, songRequestList.Songs);
                    }

                    // Return success response
                    return Ok(new SongRequestResult
                    {
                        Success = true,
                        Message = result.Message,
                        SongsAdded = result.SongsAdded,
                        DuplicateSongs = result.DuplicateSongs
                    });
                }
                else
                {
                    return BadRequest(new { success = false, message = result.Message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting song requests for session {SessionId}", simpleSongRequest?.SessionId);
                return StatusCode(500, new { success = false, message = "Failed to submit song requests" });
            }
        }

        private string GenerateDeviceId(Services.Local.DeviceInfo deviceInfo)
        {
            var fingerprint = $"{deviceInfo.UserAgent}|{deviceInfo.IpAddress}|{deviceInfo.ScreenResolution}|{deviceInfo.TimeZone}";
            var hash = ComputeSha256Hash(fingerprint);
            return $"dev_{hash[..12]}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        }

        private string ComputeSha256Hash(string input)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }


        private FullTrack CreateMinimalFullTrack(SimpleSong song)
        {
            return new FullTrack
            {
                Id = song.Id,
                Name = song.Name,
                Uri = song.Uri,
                Href = "", // Empty string instead of null
                DurationMs = song.DurationMs,
                Explicit = song.Explicit,
                LinkedFrom = null,
                PreviewUrl = null,
                Restrictions = null,
                AvailableMarkets = new List<string>(),
                ExternalIds = new Dictionary<string, string>(),
                ExternalUrls = new Dictionary<string, string>(),
                DiscNumber = 1,
                TrackNumber = 1,
                Type = ItemType.Track,
                IsLocal = false,
                IsPlayable = true,
                Popularity = 0,
                Album = new SimpleAlbum
                {
                    Id = "",
                    Name = song.AlbumName,
                    Images = string.IsNullOrEmpty(song.AlbumImageUrl)
                        ? new List<Image>()
                        : new List<Image> { new Image { Url = song.AlbumImageUrl } },
                    Uri = "",
                    Href = "",
                    AlbumType = "",
                    AlbumGroup = null,
                    ReleaseDate = "",
                    ReleaseDatePrecision = "",
                    TotalTracks = 1,
                    Artists = new List<SimpleArtist>(),
                    ExternalUrls = new Dictionary<string, string>(),
                    Restrictions = null,
                    AvailableMarkets = new List<string>()
                },
                Artists = new List<SimpleArtist>
                {
                    new SimpleArtist
                    {
                        Id = "",
                        Name = song.ArtistNames,
                        Uri = "",
                        Href = "",
                        ExternalUrls = new Dictionary<string, string>()
                    }
                }
            };
        }



        private async Task NotifyDJOfNewRequests(string sessionId, int songsAdded, List<SongRequest> newSongs)
        {
            try
            {
                var djGroupName = GetDJGroupName(sessionId);
                // 👇 USING THE HUB CONTEXT TO SEND SIGNALR MESSAGES
                await _hubContext.Clients.Group(djGroupName).SendAsync("NewSongRequests", new
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

        private static string GetDJGroupName(string sessionId) => $"dj_session_{sessionId}";


    }



    public class SimpleSongRequest
    {
        public string SessionId { get; set; } = "";
        public List<SimpleSong> Songs { get; set; } = new();
        public DateTime RequestedAt { get; set; }
        public string RequesterName { get; set; } = "";
        public string DeviceId { get; set; } = "";

    }

    public class SimpleSong
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Uri { get; set; } = "";
        public string ArtistNames { get; set; } = "";
        public string AlbumName { get; set; } = "";
        public string AlbumImageUrl { get; set; } = "";
        public bool Explicit { get; set; }
        public int DurationMs { get; set; }
    }


    public class DeviceRegistrationRequest
    {
        public string SessionId { get; set; } = "";
        public DeviceInfo DeviceInfo { get; set; } = new();
    }


    public class SimpleDeviceInfo
    {
        public string SessionId { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public string Browser { get; set; } = "";
        public string ScreenSize { get; set; } = "";
        public bool IsMobile { get; set; }
        public DateTime CurrentDateTime { get; set; }
    }

}
