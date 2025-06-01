using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Web;

namespace MyApi.Services
{
    public class SpotifyTokenData
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool IsExpired => DateTime.Now >= ExpiresAt;
    }

    public class SpotifyService : IDisposable
    {
        private readonly string clientId = "0e2a75f00d844860b354812f4d235613";
        private readonly string clientSecret = "48d9461f0b5d4602894324f6e9574fe7";
        private readonly string redirectUri = "https://b427-93-170-70-241.ngrok-free.app/callback"; 
        private readonly HttpClient httpClient = new();

        private readonly Dictionary<long, SpotifyTokenData> userTokens = new();

        public string GetAuthUrl()
        {
            var scopes = "playlist-modify-public playlist-modify-private user-top-read user-read-currently-playing user-read-recently-played playlist-read-private";

            return $"https://accounts.spotify.com/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString(scopes)}";
        }

        public async Task<bool> AuthenticateUserAsync(long userId)
        {
            var authUrl = GetAuthUrl();
            Console.WriteLine($"Authorize at: {authUrl}");

            using var http = new HttpListener();
            http.Prefixes.Add("http://+:8080/callback/");
            http.Start();

            var context = await http.GetContextAsync();
            var code = HttpUtility.ParseQueryString(context.Request.Url.Query).Get("code");

            context.Response.OutputStream.Close();
            http.Stop();

            if (string.IsNullOrEmpty(code)) return false;

            var tokenData = await GetTokensAsync(code);
            if (tokenData != null)
            {
                userTokens[userId] = tokenData;
                return true;
            }

            return false;
        }

        private async Task<SpotifyTokenData> GetTokensAsync(string code)
        {
            var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            tokenRequest.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
            });

            var response = await httpClient.SendAsync(tokenRequest);
            var jsonContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return null;

            var root = JsonDocument.Parse(jsonContent).RootElement;
            return new SpotifyTokenData
            {
                AccessToken = root.GetProperty("access_token").GetString(),
                RefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                ExpiresAt = DateTime.Now.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60)
            };
        }

        private async Task<bool> RefreshTokenAsync(long userId)
        {
            if (!userTokens.ContainsKey(userId) || string.IsNullOrEmpty(userTokens[userId].RefreshToken)) return false;

            var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", userTokens[userId].RefreshToken),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
            });

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            userTokens[userId].AccessToken = root.GetProperty("access_token").GetString();
            userTokens[userId].ExpiresAt = DateTime.Now.AddSeconds(root.GetProperty("expires_in").GetInt32() - 60);

            if (root.TryGetProperty("refresh_token", out var newRefresh))
                userTokens[userId].RefreshToken = newRefresh.GetString();

            return true;
        }

        private async Task<string> MakeSpotifyRequestAsync(long userId, string endpoint)
        {
            if (!userTokens.ContainsKey(userId)) return null;

            if (userTokens[userId].IsExpired && !await RefreshTokenAsync(userId))
                return null;

            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.spotify.com/v1{endpoint}");
            request.Headers.Add("Authorization", $"Bearer {userTokens[userId].AccessToken}");

            // Ось тут логування ЗАПИТУ
            Console.WriteLine($"Spotify request: {request.Method} {request.RequestUri}");

            var response = await httpClient.SendAsync(request);

            // Ось тут логування ВІДПОВІДІ
            string responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Spotify response: {response.StatusCode} - {responseContent}");

            return response.IsSuccessStatusCode ? responseContent : null;
        }

        //API METHODS
        public async Task<string> GetTopArtistsAsync(long userId, string timeRange = "short_term", int limit = 5)
        {
            var json = await MakeSpotifyRequestAsync(userId, $"/me/top/artists?time_range={timeRange}&limit={limit}");
            if (json == null) return "Помилка отримання топ-артистів.";

            var artists = JsonDocument.Parse(json).RootElement.GetProperty("items");
            var result = $"🎤 Топ-{limit} артистів:\n\n";

            int i = 1;
            foreach (var artist in artists.EnumerateArray())
                result += $"{i++}. {artist.GetProperty("name").GetString()}\n";

            return result;
        }

        public async Task<string> GetTopTracksAsync(long userId, string timeRange = "short_term", int limit = 5)
        {
            var json = await MakeSpotifyRequestAsync(userId, $"/me/top/tracks?time_range={timeRange}&limit={limit}");
            if (json == null) return "Помилка отримання топ-треків.";

            var tracks = JsonDocument.Parse(json).RootElement.GetProperty("items");
            var result = $"🎵 Топ-{limit} треків:\n\n";

            int i = 1;
            foreach (var track in tracks.EnumerateArray())
            {
                var name = track.GetProperty("name").GetString();
                var artist = track.GetProperty("artists")[0].GetProperty("name").GetString();
                result += $"{i++}. {name} - {artist}\n";
            }

            return result;
        }

        public async Task<string> GetCurrentlyPlayingAsync(long userId)
        {
            var json = await MakeSpotifyRequestAsync(userId, "/me/player/currently-playing");
            if (string.IsNullOrWhiteSpace(json)) return "Зараз нічого не грає.";

            var root = JsonDocument.Parse(json).RootElement;
            if (!root.TryGetProperty("item", out var item)) return "Зараз нічого не грає.";

            var name = item.GetProperty("name").GetString();
            var artist = item.GetProperty("artists")[0].GetProperty("name").GetString();
            var isPlaying = root.GetProperty("is_playing").GetBoolean();

            return $"{(isPlaying ? "▶️ Грає" : "⏸️ Пауза")}:\n🎵 {name} - {artist}";
        }

        public async Task<string> GetRecentlyPlayedAsync(long userId, int limit = 5)
        {
            var json = await MakeSpotifyRequestAsync(userId, $"/me/player/recently-played?limit={limit}");
            if (json == null) return "Помилка отримання історії прослуховувань.";

            var items = JsonDocument.Parse(json).RootElement.GetProperty("items");
            var result = $"🕒 Останні {limit} треків:\n\n";

            int i = 1;
            foreach (var item in items.EnumerateArray())
            {
                var track = item.GetProperty("track");
                var name = track.GetProperty("name").GetString();
                var artist = track.GetProperty("artists")[0].GetProperty("name").GetString();
                result += $"{i++}. {name} - {artist}\n";
            }

            return result;
        }

        public async Task<string> SearchTracksAsync(long userId, string query, int limit = 5)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var json = await MakeSpotifyRequestAsync(userId, $"/search?q={encodedQuery}&type=track&limit={limit}");
            if (json == null) return "Помилка пошуку.";

            var tracks = JsonDocument.Parse(json).RootElement.GetProperty("tracks").GetProperty("items");
            if (tracks.GetArrayLength() == 0) return "Треки не знайдено.";

            var result = $"🔍 Результати для \"{query}\":\n\n";
            int i = 1;
            foreach (var track in tracks.EnumerateArray())
            {
                var name = track.GetProperty("name").GetString();
                var artist = track.GetProperty("artists")[0].GetProperty("name").GetString();
                result += $"{i++}. {name} - {artist}\n";
            }

            return result;
        }

        public async Task<string> RawSearchTracksAsync(long userId, string query, int limit = 5)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var json = await MakeSpotifyRequestAsync(userId, $"/search?q={encodedQuery}&type=track&limit={limit}");
            return json; // raw json від Spotify
        }
        public async Task<string> GetUserPlaylistsAsync(long userId, int limit = 10)
        {
            var json = await MakeSpotifyRequestAsync(userId, $"/me/playlists?limit={limit}");
            if (json == null) return "Не вдалося отримати плейлисти.";

            var root = JsonDocument.Parse(json).RootElement;
            if (!root.TryGetProperty("items", out var playlists) || playlists.GetArrayLength() == 0)
                return "У вас немає плейлистів.";

            var result = "📂 Ваші плейлисти:\n\n";
            int i = 1;
            foreach (var pl in playlists.EnumerateArray())
            {
                var name = pl.GetProperty("name").GetString();
                var id = pl.GetProperty("id").GetString();
                result += $"{i++}. {name} (ID: {id})\n";
            }
            return result;
        }

        public async Task<string> CreatePlaylistAsync(long userId, string name, string description = "", bool isPublic = false)
        {
            // Спочатку треба дізнатися user Spotify id
            var userJson = await MakeSpotifyRequestAsync(userId, "/me");
            if (userJson == null) return "Не вдалося отримати ваш профіль Spotify.";

            var userRoot = JsonDocument.Parse(userJson).RootElement;
            var spotifyUserId = userRoot.GetProperty("id").GetString();

            // Далі — створення плейлисту
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.spotify.com/v1/users/{spotifyUserId}/playlists");
            req.Headers.Add("Authorization", $"Bearer {userTokens[userId].AccessToken}");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { name, description, @public = isPublic }),
                System.Text.Encoding.UTF8, "application/json");

            var resp = await httpClient.SendAsync(req);
            var respText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return $"Не вдалося створити плейлист: {resp.StatusCode}\n{respText}";

            var pl = JsonDocument.Parse(respText).RootElement;
            var plName = pl.GetProperty("name").GetString();
            var plId = pl.GetProperty("id").GetString();

            return $"✅ Плейлист \"{plName}\" створено! (ID: {plId})";
        }

        public async Task<string> AddTrackToPlaylistAsync(long userId, string playlistId, string trackUri)
        {
            // trackUri має бути типу "spotify:track:4uLU6hMCjMI75M1A2tKUQC"
            var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.spotify.com/v1/playlists/{playlistId}/tracks");
            req.Headers.Add("Authorization", $"Bearer {userTokens[userId].AccessToken}");
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { uris = new[] { trackUri } }),
                System.Text.Encoding.UTF8, "application/json");

            var resp = await httpClient.SendAsync(req);
            var respText = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return $"Не вдалося додати трек: {resp.StatusCode}\n{respText}";

            return "✅ Трек додано у плейлист!";
        }

        public async Task<string> DeletePlaylistAsync(long userId, string playlistId)
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, $"https://api.spotify.com/v1/playlists/{playlistId}/followers");
            req.Headers.Add("Authorization", $"Bearer {userTokens[userId].AccessToken}");

            var resp = await httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return $"Не вдалося видалити плейлист: {resp.StatusCode}";

            return "🗑️ Плейлист видалено з бібліотеки!";
        }




        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}
