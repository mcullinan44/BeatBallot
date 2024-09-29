using Newtonsoft.Json;
using SpotifyAPI.Web;

namespace Entertainer.Models
{
    public class Jam
    {
        public Jam(DeeJayUser deeJayUser)
        {
            CreatedDateTime = DateTime.Now;
            DeeJayUser = deeJayUser;

        }

        public string Id { get; set; }
        public DeeJayUser DeeJayUser { get; private set; }

        public bool IsVoting { get; set; }

        public DateTime CreatedDateTime { get; private set; }

        public JamPlaylist JamPlaylist { get; set; }


        public List<VoteTarget> VoteTargets
        {
            get
            {
                return JamPlaylist.VoteTargetList.OrderBy(item => item.SongName).ThenBy(item => item.ArtistName)
                    .ToList();
            }
        }


        public int MaxVotes { get; set; }

    }


    public class JamPlaylist
    {
        public JamPlaylist(List<FullTrack> fullPlaylist)
        {
            SpotifyFullPlaylist = fullPlaylist;
            JamTrackList = new List<JamTrack>(fullPlaylist.Count);
            VoteTargetList = new List<VoteTarget>(fullPlaylist.Count);
            foreach (FullTrack fullTrack in fullPlaylist)
            {
                JamTrack jamtrack = new JamTrack(fullTrack);
                jamtrack.PlaylistPosition = fullPlaylist.IndexOf(fullTrack);
                jamtrack.IsPlaying = false;
                jamtrack.NumberOfVotes = 0;
                JamTrackList.Add(jamtrack);
            }

            foreach (JamTrack jamTrack in JamTrackList)
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

        public List<JamTrack> JamTrackList { get;  set; }

        public List<VoteTarget> VoteTargetList { get; set; }
    }

    public class JamTrack
    {

        public JamTrack(FullTrack fullTrack)
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
        public VoteNotification(string jid, string trackUri, int value)
        {
            this.JamId = jid;
            this.ExternalTrackUri = trackUri;
            this.Value = value;
        }

        [JsonProperty("JamId")]
        public string JamId { get; private set;  }

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
