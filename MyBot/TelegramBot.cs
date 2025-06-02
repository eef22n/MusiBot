using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Text.Json;

namespace MyBot
{
    public class TelegramBot
    {
        private readonly TelegramBotClient _botClient;
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl = "https://localhost:50172";
        private readonly Dictionary<long, List<(string Id, string Name)>> _userPlaylists = new();
        private readonly Dictionary<long, string> _userAddTrackPlaylist = new();
        private readonly Dictionary<long, bool> _userCreatePlaylistState = new();
        private readonly Dictionary<long, string> _userSearchState = new();
        private readonly Dictionary<long, (string TrackId, string Name, string Artist)> _userPendingRatings = new();
        private readonly Dictionary<long, (string PlaylistId, int Page)> _userPlaylistPage = new();

        public TelegramBot(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _botClient = new TelegramBotClient("7988903424:AAGE4hUEoY5vB6QzKuukUSgpi69TVE4qKzU");
        }

        public async Task StartAsync()
        {
            using var cts = new CancellationTokenSource();
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery }
            };

            _botClient.StartReceiving(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cts.Token);
            var me = await _botClient.GetMe();
            Console.WriteLine($"Бот запущено: @{me.Username}");
            await Task.Delay(-1);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken token)
        {
            if (update.Type == UpdateType.Message && update.Message?.Text != null)
                await HandleMessageAsync(bot, update.Message, token);
            else if (update.Type == UpdateType.CallbackQuery)
                await HandleCallbackQueryAsync(bot, update.CallbackQuery, token);
        }

        private async Task HandleMessageAsync(ITelegramBotClient bot, Message message, CancellationToken token)
        {
            var userId = message.From.Id;
            var chatId = message.Chat.Id;
            var text = message.Text.ToLower();

            // Додавання треку через пошук для плейлисту
            if (_userAddTrackPlaylist.TryGetValue(userId, out var playlistIdForSearch))
            {
                _userAddTrackPlaylist.Remove(userId);
                await ShowTrackSearchResultsForAdd(chatId, userId, playlistIdForSearch, message.Text, token);
                return;
            }
            // Створення плейлисту
            if (_userCreatePlaylistState.TryGetValue(userId, out var _))
            {
                _userCreatePlaylistState.Remove(userId);
                await CreatePlaylist(chatId, userId, message.Text, token);
                return;
            }
            // Пошук треків/артистів для загального пошуку
            if (_userSearchState.ContainsKey(userId))
            {
                _userSearchState.Remove(userId);
                await SendSearchResults(chatId, userId, message.Text, token);
                return;
            }

            switch (text)
            {
                case "/start":
                    await bot.SendMessage(chatId, "Привіт! Натисни /menu, щоб почати.", cancellationToken: token);
                    break;
                case "/menu":
                    await SendMainMenu(chatId, token);
                    break;
                case "/auth":
                    await StartAuth(chatId, userId, token);
                    break;
               
                default:
                    await bot.SendMessage(chatId, "Невідома команда. Натисни /menu.", cancellationToken: token);
                    break;
            }
        }

        private async Task HandleCallbackQueryAsync(ITelegramBotClient bot, CallbackQuery query, CancellationToken token)
        {
            var chatId = query.Message.Chat.Id;
            var userId = query.From.Id;
            var data = query.Data;

            await bot.AnswerCallbackQuery(query.Id, cancellationToken: token);

            switch (data)
            {
                case "auth":
                    await StartAuth(chatId, userId, token);
                    break;
                case "top_artists":
                    await SendApiResult(chatId, $"spotify/top-artists/{userId}", token);
                    break;
                case "top_tracks":
                    await SendApiResult(chatId, $"spotify/top-tracks/{userId}", token);
                    break;
                case "currently_playing":
                    {
                        var response = await _httpClient.GetAsync($"{_apiBaseUrl}/spotify/currently-playing/{userId}");
                        var text = await response.Content.ReadAsStringAsync();

                        if (text.StartsWith("▶️") || text.StartsWith("⏸️"))
                        {
                            // Парсимо назву та артиста
                            var lines = text.Split('\n');
                            if (lines.Length >= 2)
                            {
                                var trackLine = lines[1]; // 🎵 Name - Artist
                                var parts = trackLine.Replace("🎵 ", "").Split(" - ");
                                if (parts.Length == 2)
                                {
                                    string name = parts[0].Trim();
                                    string artist = parts[1].Trim();

                                    // Отримаємо трек ID через search
                                    var searchResp = await _httpClient.GetAsync($"{_apiBaseUrl}/spotify/search/forplaylist/{userId}?query={Uri.EscapeDataString(name + " " + artist)}");
                                    var searchJson = await searchResp.Content.ReadAsStringAsync();
                                    var tracks = JsonSerializer.Deserialize<List<TrackSearchResult>>(searchJson);
                                    Console.WriteLine($"🎯 Пошук треку для оцінки: {trackLine}");
                                    Console.WriteLine($"🔍 Отримано {tracks?.Count ?? 0} результатів");
                                    if (tracks?.Count > 0)
                                    {
                                        var track = tracks[0];
                                        _userPendingRatings[userId] = (track.id, track.name, track.artist);

                                        var ratingKeyboard = new InlineKeyboardMarkup(new[]
                                        {
                        new[] { InlineKeyboardButton.WithCallbackData("⭐ Оцінити трек", "rate_now") }
                    });

                                        await _botClient.SendMessage(chatId, text, replyMarkup: ratingKeyboard, cancellationToken: token);
                                        break;
                                    }
                                }
                            }
                        }

                        // fallback — просто текст
                        await _botClient.SendMessage(chatId, text, cancellationToken: token);
                        break;
                    }
                case "recently_played":
                    await SendApiResult(chatId, $"spotify/recently-played/{userId}", token);
                    break;
                case "search":
                    _userSearchState[userId] = "waiting";
                    await _botClient.SendMessage(chatId, "🔍 Введи пошуковий запит:", cancellationToken: token);
                    break;
                case "show_playlists":
                    await ShowPlaylists(chatId, userId, token);
                    break;
                case "create_playlist":
                    await AskNewPlaylistName(chatId, userId, token);
                    break;
                case "rate_now":
                    {
                        if (_userPendingRatings.TryGetValue(userId, out var track))
                        {
                            var ratingButtons = Enumerable.Range(1, 10)
                                .Select(i => InlineKeyboardButton.WithCallbackData(i.ToString(), $"rate_{i}"))
                                .Chunk(5) // 2 рядки по 5 кнопок
                                .Select(row => row.ToArray())
                                .ToArray();

                            var markup = new InlineKeyboardMarkup(ratingButtons);
                            await _botClient.SendMessage(chatId, $"Оціни трек:\n🎵 {track.Name} - {track.Artist}", replyMarkup: markup, cancellationToken: token);
                        }
                        else
                        {
                            await _botClient.SendMessage(chatId, "Немає треку для оцінки.", cancellationToken: token);
                        }
                        break;
                    }
                case "ratings":
                    await ShowRatings(chatId, userId, token);
                    break;

                default:
                    // Дії з плейлистами
                    if (data.StartsWith("pl_"))
                    {
                        var playlistId = data.Substring("pl_".Length);
                        await ShowPlaylistActions(chatId, userId, playlistId, token);
                    }
                    else if (data.StartsWith("delpl_"))
                    {
                        var playlistId = data.Substring("delpl_".Length);
                        await DeletePlaylist(chatId, userId, playlistId, token);
                    }
                    else if (data.StartsWith("addtr_"))
                    {
                        var playlistId = data.Substring("addtr_".Length);
                        await AskTrackToAdd(chatId, userId, playlistId, token);
                    }
                    else if (data.StartsWith("viewpl_"))
                    {
                        var parts = data.Substring("viewpl_".Length).Split(':');
                        var playlistId = parts[0];
                        int page = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 0;

                        _userPlaylistPage[userId] = (playlistId, page);

                        var response = await _httpClient.GetAsync($"{_apiBaseUrl}/spotify/playlists/{userId}/{playlistId}/tracks");
                        var json = await response.Content.ReadAsStringAsync();

                        if (!json.Trim().StartsWith("["))
                        {
                            await _botClient.SendMessage(chatId, "Не вдалося завантажити треки.", cancellationToken: token);
                            return;
                        }

                        var tracks = JsonSerializer.Deserialize<List<string>>(json);
                        if (tracks == null || tracks.Count == 0)
                        {
                            await _botClient.SendMessage(chatId, "Плейлист порожній.", cancellationToken: token);
                            return;
                        }

                        int pageSize = 5;
                        int totalPages = (int)Math.Ceiling((double)tracks.Count / pageSize);
                        page = Math.Clamp(page, 0, totalPages - 1);

                        var trackPage = tracks.Skip(page * pageSize).Take(pageSize).ToList();
                        string message = $"🎶 Треки (сторінка {page + 1}/{totalPages}):\n\n" + string.Join("\n", trackPage);

                        var navButtons = new List<InlineKeyboardButton[]>();
                        var navRow = new List<InlineKeyboardButton>();

                        if (page > 0)
                            navRow.Add(InlineKeyboardButton.WithCallbackData("◀️ Назад", $"viewpl_{playlistId}:{page - 1}"));
                        if (page < totalPages - 1)
                            navRow.Add(InlineKeyboardButton.WithCallbackData("Вперед ▶️", $"viewpl_{playlistId}:{page + 1}"));

                        if (navRow.Count > 0)
                            navButtons.Add(navRow.ToArray());

                        var markup = navButtons.Count > 0 ? new InlineKeyboardMarkup(navButtons) : null;
                        await _botClient.SendMessage(chatId, message, replyMarkup: markup, cancellationToken: token);
                    }

                    else if (data.StartsWith("addtrk_"))
                    {
                        // addtrk_{playlistId}_{trackId}
                        var rest = data.Substring("addtrk_".Length);
                        var parts = rest.Split('_');
                        if (parts.Length == 2)
                        {
                            var playlistId = parts[0];
                            var trackId = parts[1];
                            await AddTrackToPlaylistById(chatId, userId, playlistId, trackId, token);
                        }
                    }
                    //обробка оцінки
                    else if (data.StartsWith("rate_"))
                    {
                        if (int.TryParse(data.Substring(5), out int rating) &&
                            _userPendingRatings.TryGetValue(userId, out var track))
                        {
                            var saveResp = await _httpClient.PostAsync(
                                $"{_apiBaseUrl}/db/save-rating/{userId}?trackId={track.TrackId}&trackName={Uri.EscapeDataString(track.Name)}&artist={Uri.EscapeDataString(track.Artist)}&rating={rating}", null);

                            var text = saveResp.IsSuccessStatusCode
                                ? $"✅ Оцінено \"{track.Name}\" на {rating}/10"
                                : "❌ Не вдалося зберегти оцінку.";

                            await _botClient.SendMessage(chatId, text, cancellationToken: token);
                        }
                        else
                        {
                            await _botClient.SendMessage(chatId, "Невірне значення або немає треку для оцінки.", cancellationToken: token);
                        }
                    }
                    break;
            }
        }

        private async Task SendMainMenu(long chatId, CancellationToken token)
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🎤 Топ-артисти", "top_artists") },
                new[] { InlineKeyboardButton.WithCallbackData("🎵 Топ-треки", "top_tracks") },
                new[] { InlineKeyboardButton.WithCallbackData("▶️ Зараз грає", "currently_playing") },
                new[] { InlineKeyboardButton.WithCallbackData("🕒 Останні треки", "recently_played") },
                new[] { InlineKeyboardButton.WithCallbackData("🔍 Пошук", "search") },
                 new[] { InlineKeyboardButton.WithCallbackData("⭐️ Переглянути оцінки", "ratings") },
                new[] { InlineKeyboardButton.WithCallbackData("📂 Плейлисти", "show_playlists") },
                new[] { InlineKeyboardButton.WithCallbackData("➕ Створити плейлист", "create_playlist") },
                new[] { InlineKeyboardButton.WithCallbackData("🔐 Авторизація", "auth") }
            });

            await _botClient.SendMessage(chatId, "📋 Меню Spotify:", replyMarkup: keyboard, cancellationToken: token);
        }

        private async Task SendApiResult(long chatId, string endpoint, CancellationToken token)
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/{endpoint}");
            var text = await response.Content.ReadAsStringAsync();
            await _botClient.SendMessage(chatId, text, cancellationToken: token);
        }

        private async Task SendSearchResults(long chatId, long userId, string query, CancellationToken token)
        {
            var url = $"{_apiBaseUrl}/spotify/search/{userId}?query={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();
            await _botClient.SendMessage(chatId, result, cancellationToken: token);
        }
        private async Task ShowRatings(long chatId, long userId, CancellationToken token)
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/db/ratings/{userId}");
            var json = await response.Content.ReadAsStringAsync();

            if (!json.Trim().StartsWith("["))
            {
                await _botClient.SendMessage(chatId, "Не вдалося отримати оцінки.", cancellationToken: token);
                return;
            }

            var ratings = JsonSerializer.Deserialize<List<string>>(json);
            if (ratings == null || ratings.Count == 0)
            {
                await _botClient.SendMessage(chatId, "У вас ще немає оцінених треків.", cancellationToken: token);
                return;
            }

            string message = "🎧 Ваші останні оцінки:\n\n" + string.Join("\n", ratings);
            await _botClient.SendMessage(chatId, message, cancellationToken: token);
        }


        // ==== Блок плейлистів з кнопками ====

        private async Task ShowPlaylists(long chatId, long userId, CancellationToken token)
        {
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/spotify/playlists/{userId}");
            var playlistsRaw = await response.Content.ReadAsStringAsync();

            var playlists = new List<(string Id, string Name)>();
            var matches = System.Text.RegularExpressions.Regex.Matches(playlistsRaw, @"\d+\. (.+) \(ID: ([^\)]+)\)");
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                playlists.Add((match.Groups[2].Value, match.Groups[1].Value));
            }
            _userPlaylists[userId] = playlists;

            if (playlists.Count == 0)
            {
                await _botClient.SendMessage(chatId, "У вас немає плейлистів. Натисніть ➕ щоб створити.", cancellationToken: token);
                return;
            }

            var keyboard = new InlineKeyboardMarkup(
                playlists.Select(p => new[] {
                    InlineKeyboardButton.WithCallbackData(p.Name, $"pl_{p.Id}")
                })
            );

            await _botClient.SendMessage(chatId, "Ваші плейлисти:", replyMarkup: keyboard, cancellationToken: token);
        }

        private async Task ShowPlaylistActions(long chatId, long userId, string playlistId, CancellationToken token)
        {
            string plName = _userPlaylists.TryGetValue(userId, out var pls)
                ? pls.FirstOrDefault(x => x.Id == playlistId).Name
                : "Плейлист";

            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🎶 Переглянути треки", $"viewpl_{playlistId}:0") },
                new[] { InlineKeyboardButton.WithCallbackData("➕ Додати трек", $"addtr_{playlistId}") },
                new[] { InlineKeyboardButton.WithCallbackData("🗑️ Видалити Плейліст", $"delpl_{playlistId}") }
            });
            await _botClient.SendMessage(chatId, $"Обери дію для \"{plName}\":", replyMarkup: keyboard, cancellationToken: token);
        }

        private async Task DeletePlaylist(long chatId, long userId, string playlistId, CancellationToken token)
        {
            var response = await _httpClient.DeleteAsync($"{_apiBaseUrl}/spotify/playlists/{userId}/{playlistId}");
            var result = await response.Content.ReadAsStringAsync();
            await _botClient.SendMessage(chatId, result, cancellationToken: token);
            await ShowPlaylists(chatId, userId, token);
        }

        private async Task AskTrackToAdd(long chatId, long userId, string playlistId, CancellationToken token)
        {
            _userAddTrackPlaylist[userId] = playlistId;
            await _botClient.SendMessage(chatId, "Введіть назву треку для додавання:", cancellationToken: token);
        }

       private async Task ShowTrackSearchResultsForAdd(long chatId, long userId, string playlistId, string query, CancellationToken token)
{
    var response = await _httpClient.GetAsync($"{_apiBaseUrl}/spotify/search/forplaylist/{userId}?query={Uri.EscapeDataString(query)}");
    var json = await response.Content.ReadAsStringAsync();

    // Якщо не масив, помилка
    if (!json.Trim().StartsWith("["))
    {
        await _botClient.SendMessage(chatId, "Треків не знайдено.", cancellationToken: token);
        return;
    }

    var tracks = JsonSerializer.Deserialize<List<TrackSearchResult>>(json);

    if (tracks == null || tracks.Count == 0)
    {
        await _botClient.SendMessage(chatId, "Треків не знайдено.", cancellationToken: token);
        return;
    }

    var keyboard = tracks
        .Select(track =>
            new[] { InlineKeyboardButton.WithCallbackData($"{track.name} - {track.artist}", $"addtrk_{playlistId}_{track.id}") }
        )
        .ToList();

    await _botClient.SendMessage(chatId, "Оберіть трек для додавання:", replyMarkup: new InlineKeyboardMarkup(keyboard), cancellationToken: token);
}

// Для десеріалізації:
public class TrackSearchResult
{
    public string name { get; set; }
    public string artist { get; set; }
    public string id { get; set; }
}

        private async Task AddTrackToPlaylistById(long chatId, long userId, string playlistId, string trackId, CancellationToken token)
        {
            var trackUri = $"spotify:track:{trackId}";
            var response = await _httpClient.PostAsync(
                $"{_apiBaseUrl}/spotify/playlists/{userId}/{playlistId}/add-track?trackUri={Uri.EscapeDataString(trackUri)}", null);
            var result = await response.Content.ReadAsStringAsync();
            await _botClient.SendMessage(chatId, result, cancellationToken: token);
        }

        private async Task AskNewPlaylistName(long chatId, long userId, CancellationToken token)
        {
            _userCreatePlaylistState[userId] = true;
            await _botClient.SendMessage(chatId, "Введіть назву нового плейлисту:", cancellationToken: token);
        }
        private async Task CreatePlaylist(long chatId, long userId, string name, CancellationToken token)
        {
            var response = await _httpClient.PostAsync(
                $"{_apiBaseUrl}/spotify/playlists/{userId}?name={Uri.EscapeDataString(name)}", null);
            var result = await response.Content.ReadAsStringAsync();
            await _botClient.SendMessage(chatId, result, cancellationToken: token);

            await ShowPlaylists(chatId, userId, token);
        }
        // ==== Кінець блоку плейлистів ====

        private async Task StartAuth(long chatId, long userId, CancellationToken token)
        {
            var authLink = await _httpClient.GetStringAsync($"{_apiBaseUrl}/spotify/get-auth-url/{userId}");
            await _botClient.SendMessage(chatId, $"🔐 Перейдіть для авторизації:\n\n{authLink}\n\n⏳ Очікуємо...", cancellationToken: token);

            _ = Task.Run(async () =>
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/spotify/authenticate/{userId}", null);
                var text = response.IsSuccessStatusCode ? "✅ Авторизація успішна!" : "❌ Помилка авторизації.";
                await _botClient.SendMessage(chatId, text, cancellationToken: token);
                await SendMainMenu(chatId, token);
            });
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken token)
        {
            Console.WriteLine($"Помилка бота: {exception.Message}");
            return Task.CompletedTask;
        }

        private class TrackInfo
        {
            public string Name { get; set; }
            public string Artist { get; set; }
            public string Id { get; set; }
        }
    }
}
