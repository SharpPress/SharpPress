using Microsoft.AspNetCore.Mvc;

namespace SamplePlugin.Controllers
{
    [ApiController]
    [Route("sample-api")]
    public class SampleController : ControllerBase
    {
        [HttpGet("ping")]
        public IActionResult Ping() => Ok(new
        {
            plugin = "SamplePlugin",
            source = "MVC controller",
            time = DateTime.UtcNow
        });

        [HttpGet("echo/{text}")]
        public IActionResult Echo(string text) => Ok(new { echo = text });
    }
}
