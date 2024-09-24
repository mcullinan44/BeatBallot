using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Entertainer.Models;

namespace Entertainer.Services
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
