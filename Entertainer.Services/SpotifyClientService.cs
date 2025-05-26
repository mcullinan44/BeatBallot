using SpotifyAPI.Web;

namespace BeatBallot.Services
{
    public sealed class SpotifyClientService
    {

        private readonly string _accessToken;
        private readonly SpotifyClient _spotifyClient;

        public SpotifyClientService(string accessToken)
        {
            this._accessToken = accessToken;
        }

        public async Task<string> GetTestTrackName(string trackId)
        {
            var spotify = new SpotifyClient(_accessToken);
            var track = await spotify.Tracks.Get(trackId);
            return track.Name;
        }
    }
}
