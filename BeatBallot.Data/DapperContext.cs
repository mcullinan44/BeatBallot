using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace BeatBallot.Data
{
    public class DapperContext
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public DapperContext(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        public IDbConnection CreateConnection() => new SqliteConnection(_connectionString);

        public async Task InitializeDatabaseAsync()
        {
            using (var connection = CreateConnection())
            {
                var createTableQuery = @"

CREATE TABLE IF NOT EXISTS ""DeeJay"" (
	""Id""	INTEGER NOT NULL UNIQUE,
	""FirstName""	TEXT NOT NULL,
	""LastName""	TEXT,
	PRIMARY KEY(""Id"" AUTOINCREMENT)
)
";
                await connection.ExecuteAsync(createTableQuery);

                 createTableQuery = @"
         
CREATE TABLE IF NOT EXISTS ""Jam"" (
	""Id""	INTEGER NOT NULL UNIQUE,
	""CreatedDate""	TEXT NOT NULL,
	""DeeJayId""	INTEGER NOT NULL,
	PRIMARY KEY(""Id"" AUTOINCREMENT)
)
";

                await connection.ExecuteAsync(createTableQuery);

                createTableQuery = @"
CREATE TABLE IF NOT EXISTS ""JamPlaylist"" (
	""Id""	INTEGER NOT NULL UNIQUE,
	""JamId""	INTEGER NOT NULL,
	""Title""	TEXT NOT NULL,
	""ExternalId""	TEXT NOT NULL,
	""Href""	TEXT NOT NULL,
	PRIMARY KEY(""Id"" AUTOINCREMENT),
	FOREIGN KEY(""JamId"") REFERENCES ""Jam""(""Id"")
)
";
                await connection.ExecuteAsync(createTableQuery);


                createTableQuery = @"
CREATE TABLE IF NOT EXISTS ""JamTrack"" (
	""Id""	INTEGER NOT NULL UNIQUE,
	""JamPlaylistId""	INTEGER NOT NULL,
	""ExternalId""	TEXT NOT NULL,
	""PlaylistPosition""	INTEGER,
	""IsPlayed""	INTEGER NOT NULL,
	""VoteCount""	INTEGER,
	FOREIGN KEY(""JamPlaylistId"") REFERENCES ""JamPlaylist""(""Id""),
	PRIMARY KEY(""Id"" AUTOINCREMENT)
)

";
                await connection.ExecuteAsync(createTableQuery);
            }
        }
        }
    
}
