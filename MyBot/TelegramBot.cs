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
                case "/playlists":
                    await ShowPlaylists(chatId, userId, token);
                    break;
                case "/createplaylist":
                    await AskNewPlaylistName(chatId, userId, token);
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
                    await SendApiResult(chatId, $"spotify/currently-playing/{userId}", token);
                    break;
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
                new[] { InlineKeyboardButton.WithCallbackData("🗑️ Видалити", $"delpl_{playlistId}") },
                new[] { InlineKeyboardButton.WithCallbackData("➕ Додати трек", $"addtr_{playlistId}") }
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
