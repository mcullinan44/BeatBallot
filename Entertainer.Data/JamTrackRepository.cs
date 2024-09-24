using Dapper;
using Entertainer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entertainer.Data
{
    public class JamTrackRepository
    {
        private readonly DapperContext _context;

        public JamTrackRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<JamTrack>> GetJamTracksAsync()
        {
            var query = "SELECT * FROM JamTrack";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<JamTrack>(query);
            }
        }

    }
}