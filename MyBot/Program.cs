using MyBot;
using System.Net.Http;

// Ініціалізація TelegramBot з HttpClient
var httpClient = new HttpClient();
var bot = new TelegramBot(httpClient);

await bot.StartAsync();
