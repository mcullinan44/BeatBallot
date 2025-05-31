using BeatBallot.Models;

namespace BeatBallot.Services
{


    public class SessionCacheService
    {
        static readonly object padlock = new object();

        public List<Session> SessionList { get; set; }

        public SessionCacheService()
        {
            this.SessionList = new List<Session>();
        }

        public void AddJam(Session jam)
        {
            lock (padlock)
            {
                SessionList.Add(jam);
            }
        }
    }
}
