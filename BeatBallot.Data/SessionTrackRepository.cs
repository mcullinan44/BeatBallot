using BeatBallot.Models;
using Dapper;

namespace BeatBallot.Data
{
    public class SessionTrackRepository
    {
        private readonly DapperContext _context;

        public SessionTrackRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<SessionTrack>> GetJamTracksAsync()
        {
            var query = "SELECT * FROM SessionTrack";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<SessionTrack>(query);
            }
        }

    }
}