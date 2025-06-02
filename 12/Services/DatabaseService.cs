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

            cmd.CommandText =
@"
    CREATE TABLE IF NOT EXISTS TrackRatings (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        UserId INTEGER NOT NULL,
        TrackId TEXT NOT NULL,
        TrackName TEXT NOT NULL,
        ArtistName TEXT NOT NULL,
        Rating INTEGER NOT NULL,
        Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
    );
";
            cmd.ExecuteNonQuery();
        }

        public void SaveSearch(long userId, string query)
        {
            Console.WriteLine($"👉 Вставка в БД: userId = {userId}, query = \"{query}\"");

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
        public void SaveRating(long userId, string trackId, string trackName, string artistName, int rating)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            // Видалення попередньої оцінки (якщо є)
            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText =
            @"
        DELETE FROM TrackRatings
        WHERE UserId = $userId AND TrackId = $trackId;
    ";
            deleteCmd.Parameters.AddWithValue("$userId", userId);
            deleteCmd.Parameters.AddWithValue("$trackId", trackId);
            deleteCmd.ExecuteNonQuery();

            // Додавання нової оцінки
            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText =
            @"
        INSERT INTO TrackRatings (UserId, TrackId, TrackName, ArtistName, Rating)
        VALUES ($userId, $trackId, $trackName, $artistName, $rating);
    ";
            insertCmd.Parameters.AddWithValue("$userId", userId);
            insertCmd.Parameters.AddWithValue("$trackId", trackId);
            insertCmd.Parameters.AddWithValue("$trackName", trackName);
            insertCmd.Parameters.AddWithValue("$artistName", artistName);
            insertCmd.Parameters.AddWithValue("$rating", rating);
            insertCmd.ExecuteNonQuery();
        }
        public List<string> GetUserRatings(long userId, int limit = 10)
        {
            var result = new List<string>();

            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
            @"
        SELECT TrackName, ArtistName, Rating, Timestamp
        FROM TrackRatings
        WHERE UserId = $userId
        ORDER BY Timestamp DESC
        LIMIT $limit;
    ";
            cmd.Parameters.AddWithValue("$userId", userId);
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(0);
                string artist = reader.GetString(1);
                int rating = reader.GetInt32(2);
                DateTime time = reader.GetDateTime(3);

                result.Add($"⭐ {rating}/10 – {name} - {artist} ({time:g})");
            }

            return result;
        }

    }
}
