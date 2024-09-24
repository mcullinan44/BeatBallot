using Dapper;
using Entertainer.Models;

namespace Entertainer.Data
{
    public class JamRepository
    {
        private readonly DapperContext _context;

        public JamRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Jam>> GetJamsAsync()
        {
            var query = "SELECT * FROM Jam";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<Jam>(query);
            }
        }

        public async Task<Jam> GetJamByIdAsync(int id)
        {
            var query = "SELECT * FROM Jam WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QuerySingleOrDefaultAsync<Jam>(query, new { Id = id });
            }
        }

        public async Task CreateJamAsync(Jam jam)
        {
            var query = "INSERT INTO Jam (CreatedDate, DeeJayId) VALUES (@CreatedDate, @DeeJayId)";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, jam);
            }
        }

        public async Task UpdateJamAsync(Jam jam)
        {
            var query = "UPDATE Jam SET CreatedDate = @CreatedDate, DeeJayId = @DeeJayId WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, jam);
            }
        }

        public async Task DeleteJamAsync(int id)
        {
            var query = "DELETE FROM Jam WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, new { Id = id });
            }
        }
    }
}
