using BeatBallot.Models;
using Dapper;

namespace BeatBallot.Data
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