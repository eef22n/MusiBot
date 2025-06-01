
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MyApi.Services;

namespace MyApi.Controllers
{
    [ApiController]
    [Route("spotify")]
    public class SpotifyController : ControllerBase
    {
        private readonly SpotifyService _spotify;

        public SpotifyController(SpotifyService spotify)
        {
            _spotify = spotify;
        }

        [HttpGet("get-auth-url/{userId:long}")]
        public ActionResult<string> GetAuthUrl(long userId)
        {
            return _spotify.GetAuthUrl();
        }

        [HttpPost("authenticate/{userId:long}")]
        public async Task<IActionResult> Authenticate(long userId)
        {
            var success = await _spotify.AuthenticateUserAsync(userId);
            return success ? Ok("Authenticated") : BadRequest("Failed");
        }

        [HttpGet("top-artists/{userId:long}")]
        public async Task<IActionResult> TopArtists(long userId)
        {
            var result = await _spotify.GetTopArtistsAsync(userId);
            return Ok(result);
        }

        [HttpGet("top-tracks/{userId:long}")]
        public async Task<IActionResult> TopTracks(long userId)
        {
            var result = await _spotify.GetTopTracksAsync(userId);
            return Ok(result);
        }

        [HttpGet("currently-playing/{userId:long}")]
        public async Task<IActionResult> CurrentlyPlaying(long userId)
        {
            var result = await _spotify.GetCurrentlyPlayingAsync(userId);
            return Ok(result);
        }

        [HttpGet("recently-played/{userId:long}")]
        public async Task<IActionResult> RecentlyPlayed(long userId)
        {
            var result = await _spotify.GetRecentlyPlayedAsync(userId);
            return Ok(result);
        }

        [HttpGet("search/{userId:long}")]
        public async Task<IActionResult> Search(long userId, [FromQuery] string query)
        {
            var result = await _spotify.SearchTracksAsync(userId, query);
            return Ok(result);
        }

       [HttpGet("search/forplaylist/{userId:long}")]
public async Task<IActionResult> SearchForPlaylist(long userId, [FromQuery] string query, [FromQuery] int limit = 5)
{
    var json = await _spotify.RawSearchTracksAsync(userId, query, limit);

    if (string.IsNullOrWhiteSpace(json) || !json.Trim().StartsWith("{"))
        return Ok(new List<object>());

    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    var tracks = root.GetProperty("tracks").GetProperty("items");

    if (tracks.GetArrayLength() == 0) return Ok(new List<object>());

    var result = new List<object>();
    foreach (var track in tracks.EnumerateArray())
    {
        var name = track.GetProperty("name").GetString();
        var artist = track.GetProperty("artists")[0].GetProperty("name").GetString();
        var id = track.GetProperty("id").GetString();
        result.Add(new { Name = name, Artist = artist, Id = id });
    }
    return Ok(result);
}


        [HttpGet("playlists/{userId:long}")]
        public async Task<IActionResult> GetPlaylists(long userId)
        {
            var result = await _spotify.GetUserPlaylistsAsync(userId);
            return Ok(result);
        }

        [HttpPost("playlists/{userId:long}")]
        public async Task<IActionResult> CreatePlaylist(long userId, [FromQuery] string name)
        {
            var result = await _spotify.CreatePlaylistAsync(userId, name);
            return Ok(result);
        }

        [HttpPost("playlists/{userId:long}/{playlistId}/add-track")]
        public async Task<IActionResult> AddTrack(long userId, string playlistId, [FromQuery] string trackUri)
        {
            var result = await _spotify.AddTrackToPlaylistAsync(userId, playlistId, trackUri);
            return Ok(result);
        }

        [HttpDelete("playlists/{userId:long}/{playlistId}")]
        public async Task<IActionResult> DeletePlaylist(long userId, string playlistId)
        {
            var result = await _spotify.DeletePlaylistAsync(userId, playlistId);
            return Ok(result);
        }
    }
}
