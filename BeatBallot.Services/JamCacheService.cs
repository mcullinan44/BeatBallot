using BeatBallot.Models;

namespace BeatBallot.Services
{


    public class JamCacheService
    {
        static readonly object padlock = new object();

        public List<Jam> JamList { get; set; }

        public JamCacheService()
        {
            this.JamList = new List<Jam>();
        }

        public void AddJam(Jam jam)
        {
            lock (padlock)
            {
                JamList.Add(jam);
            }
        }
    }
}
