using Microsoft.Data.Sqlite;

namespace MyApi.Services
{
    public class DatabaseService
    {
        private const string ConnectionString = "Data Source=spotify.db";

        public DatabaseService()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
            @"
                CREATE TABLE IF NOT EXISTS SearchHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    Query TEXT NOT NULL,
                    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public void SaveSearch(long userId, string query)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
            @"
                INSERT INTO SearchHistory (UserId, Query)
                VALUES ($userId, $query);
            ";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$query", query);
            cmd.ExecuteNonQuery();
        }

        public List<string> GetUserSearchHistory(long userId, int limit = 5)
        {
            var result = new List<string>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
            @"
                SELECT Query, Timestamp
                FROM SearchHistory
                WHERE UserId = $userId
                ORDER BY Timestamp DESC
                LIMIT $limit;
            ";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add($"{reader.GetString(0)} ({reader.GetDateTime(1):g})");
            }

            return result;
        }
    }
}
