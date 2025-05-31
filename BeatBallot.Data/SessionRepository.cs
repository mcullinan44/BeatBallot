using BeatBallot.Models;
using Dapper;

namespace BeatBallot.Data
{
    public class SessionRepository
    {
        private readonly DapperContext _context;

        public SessionRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Session>> GetJamsAsync()
        {
            var query = "SELECT * FROM Jam";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<Session>(query);
            }
        }

        public async Task<Session> GetSessionByIdAsync(int id)
        {
            var query = "SELECT * FROM Jam WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QuerySingleOrDefaultAsync<Session>(query, new { Id = id });
            }
        }

        public async Task CreateJamAsync(Session session)
        {
            var query = "INSERT INTO Jam (CreatedDate, DeeJayId) VALUES (@CreatedDate, @DeeJayId)";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, session);
            }
        }

        public async Task UpdateSessionAsync(Session session)
        {
            var query = "UPDATE Jam SET CreatedDate = @CreatedDate, DeeJayId = @DeeJayId WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, session);
            }
        }

        public async Task DeleteSessionAsync(int id)
        {
            var query = "DELETE FROM Jam WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, new { Id = id });
            }
        }
    }
}
