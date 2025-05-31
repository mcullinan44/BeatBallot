using Newtonsoft.Json;
using SpotifyAPI.Web;

namespace BeatBallot.Models
{
    public class Session
    {
        public Session(DeeJayUser deeJayUser)
        {
            CreatedDateTime = DateTime.Now;
            DeeJayUser = deeJayUser;

        }

        public string Id { get; set; }
        public DeeJayUser DeeJayUser { get; private set; }

        public bool IsVoting { get; set; }

        public DateTime CreatedDateTime { get; private set; }

        public SessionPlaylist SessionPlaylist { get; set; }


        public List<VoteTarget> VoteTargets
        {
            get
            {
                return SessionPlaylist.VoteTargetList.OrderBy(item => item.SongName).ThenBy(item => item.ArtistName)
                    .ToList();
            }
        }


        public int MaxVotes { get; set; }

    }


    public class SessionPlaylist
    {
        public SessionPlaylist(List<FullTrack> fullPlaylist)
        {
            SpotifyFullPlaylist = fullPlaylist;
            SessionTrackList = new List<SessionTrack>(fullPlaylist.Count);
            VoteTargetList = new List<VoteTarget>(fullPlaylist.Count);
            foreach (FullTrack fullTrack in fullPlaylist)
            {
                SessionTrack jamtrack = new SessionTrack(fullTrack);
                jamtrack.PlaylistPosition = fullPlaylist.IndexOf(fullTrack);
                jamtrack.IsPlaying = false;
                jamtrack.NumberOfVotes = 0;
                SessionTrackList.Add(jamtrack);
            }

            foreach (SessionTrack jamTrack in SessionTrackList)
            {
                VoteTarget voteTarget = new VoteTarget();
                voteTarget.ArtistName = jamTrack.SpotifyFullTrack.Artists[0].Name;
                voteTarget.SongName = jamTrack.SpotifyFullTrack.Name;
                voteTarget.ExternalTrackUri = jamTrack.SpotifyFullTrack.Uri;
                VoteTargetList.Add(voteTarget);
            }
        }

        public string Id { get; set; }
        public string Href { get; set; }

        public string ExternalId { get; set; }

        public bool IsActive { get; set; }

        public List<FullTrack> SpotifyFullPlaylist { get; private set; }

        public List<SessionTrack> SessionTrackList { get;  set; }

        public List<VoteTarget> VoteTargetList { get; set; }
    }

    public class SessionTrack
    {

        public SessionTrack(FullTrack fullTrack)
        {
            this.SpotifyFullTrack = fullTrack;
        }


        public string Id { get; set; }

        public FullTrack SpotifyFullTrack { get; private set; }

        public int PlaylistPosition { get; set; }
        
        public bool IsPlaying { get; set; }

        public int NumberOfVotes { get; set; }

    }


    public class DeeJayUser
    {
        public DeeJayUser(string Name)
        {
            this.Name = Name;
        }

        public string Id { get; set; }

        public string Name { get; private set; }

    }


    public class VoteNotification
    {
        public VoteNotification(string sessionId, string trackUri, int value)
        {
            this.SessionId = sessionId;
            this.ExternalTrackUri = trackUri;
            this.Value = value;
        }

        [JsonProperty("SessionId")]
        public string SessionId { get; private set;  }

        [JsonProperty("ExternalTrackUri")]
        public string ExternalTrackUri { get; private set;  }

        [JsonProperty("Value")]
        public int Value { get; private set; }

    }



    public class VoteTarget
    {
        public string SongName { get; set; }
        public string ArtistName { get; set; }

        public string ExternalTrackUri { get; set; }

        public int TotalVotes { get; set; }
    }

}
