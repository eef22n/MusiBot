
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
    }
}
