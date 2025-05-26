using BeatBallot.Models;
using Dapper;

namespace BeatBallot.Data
{
    public class DeeJayRepository
    {
        private readonly DapperContext _context;

        public DeeJayRepository(DapperContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<DeeJayUser>> GetDeeJaysAsync()
        {
            var query = "SELECT * FROM DeeJay";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QueryAsync<DeeJayUser>(query);
            }
        }

        public async Task<DeeJayUser> GetDeeJayByIdAsync(int id)
        {
            var query = "SELECT * FROM DeeJay WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                return await connection.QuerySingleOrDefaultAsync<DeeJayUser>(query, new { Id = id });
            }
        }

        public async Task CreateDeeJayAsync(DeeJayUser deeJay)
        {
            var query = "INSERT INTO DeeJay (FirstName, LastName) VALUES (@FirstName, @LastName)";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, deeJay);
            }
        }

        public async Task UpdateDeeJayAsync(DeeJayUser deeJay)
        {
            var query = "UPDATE DeeJay SET FirstName = @FirstName, LastName = @LastName WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, deeJay);
            }
        }

        public async Task DeleteDeeJayAsync(int id)
        {
            var query = "DELETE FROM DeeJay WHERE Id = @Id";
            using (var connection = _context.CreateConnection())
            {
                await connection.ExecuteAsync(query, new { Id = id });
            }
        }
    }

}
