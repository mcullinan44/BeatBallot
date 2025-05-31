using BeatBallot.Models;
using Dapper;

namespace BeatBallot.Data
{
    public class SessionPlaylistRepository
    {
        private readonly DapperContext _context;

        public SessionPlaylistRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<SessionPlaylist>> GetJamPlaylistsAsync()
        {
            var query = "SELECT * FROM JamPlaylist";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<SessionPlaylist>(query);
            }
        }

        public async Task<SessionPlaylist> GetJamPlaylistByIdAsync(int id)
        {
            var query = "SELECT * FROM JamPlaylist WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QuerySingleOrDefaultAsync<SessionPlaylist>(query, new { Id = id });
            }
        }

        public async Task CreateJamPlaylistAsync(SessionPlaylist sessionPlaylist)
        {
            var query = "INSERT INTO JamPlaylist (JamId, Title, ExternalId, Href) VALUES (@JamId, @Title, @ExternalId, @Href)";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, sessionPlaylist);
            }
        }

        public async Task UpdateJamPlaylistAsync(SessionPlaylist sessionPlaylist)
        {
            var query = "UPDATE JamPlaylist SET JamId = @JamId, Title = @Title, ExternalId = @ExternalId, Href = @Href WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, sessionPlaylist);
            }
        }

        public async Task DeleteJamPlaylistAsync(int id)
        {
            var query = "DELETE FROM JamPlaylist WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, new { Id = id });
            }
        }
    }

}
