
using Microsoft.AspNetCore.Mvc;
using MyApi.Services;

namespace MyApi.Controllers
{
    [ApiController]
    [Route("db")]
    public class DbController : ControllerBase
    {
        private readonly DatabaseService _database;

        public DbController(DatabaseService database)
        {
            _database = database;
        }

        [HttpPost("save-search/{userId:long}")]
        public IActionResult SaveSearch(long userId, [FromQuery] string query)
        {
            _database.SaveSearch(userId, query);
            return Ok("Search saved");
        }

        [HttpGet("history/{userId:long}")]
        public IActionResult GetHistory(long userId)
        {
            var history = _database.GetUserSearchHistory(userId);
            return Ok(history);
        }

        [HttpGet("ratings/{userId:long}")]
        public IActionResult GetRatings(long userId)
        {
            var ratings = _database.GetUserRatings(userId);
            return Ok(ratings);
        }

        [HttpPost("save-rating/{userId:long}")]
        public IActionResult SaveRating(
    long userId,
    [FromQuery] string trackId,
    [FromQuery] string trackName,
    [FromQuery] string artist,
    [FromQuery] int rating)
        {
            if (rating < 1 || rating > 10)
                return BadRequest("Оцінка має бути від 1 до 10.");

            _database.SaveRating(userId, trackId, trackName, artist, rating);
            return Ok("Оцінку збережено");
        }
    }
}
