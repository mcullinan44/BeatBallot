using BeatBallot.Models;
using Dapper;

namespace BeatBallot.Data
{
    public class JamPlaylistRepository
    {
        private readonly DapperContext _context;

        public JamPlaylistRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<JamPlaylist>> GetJamPlaylistsAsync()
        {
            var query = "SELECT * FROM JamPlaylist";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<JamPlaylist>(query);
            }
        }

        public async Task<JamPlaylist> GetJamPlaylistByIdAsync(int id)
        {
            var query = "SELECT * FROM JamPlaylist WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QuerySingleOrDefaultAsync<JamPlaylist>(query, new { Id = id });
            }
        }

        public async Task CreateJamPlaylistAsync(JamPlaylist jamPlaylist)
        {
            var query = "INSERT INTO JamPlaylist (JamId, Title, ExternalId, Href) VALUES (@JamId, @Title, @ExternalId, @Href)";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, jamPlaylist);
            }
        }

        public async Task UpdateJamPlaylistAsync(JamPlaylist jamPlaylist)
        {
            var query = "UPDATE JamPlaylist SET JamId = @JamId, Title = @Title, ExternalId = @ExternalId, Href = @Href WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, jamPlaylist);
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
