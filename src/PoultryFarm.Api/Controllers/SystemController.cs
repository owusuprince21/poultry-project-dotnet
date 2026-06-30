using Microsoft.AspNetCore.Mvc;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            name = "Poultry Farm API",
            status = "ready",
            serverTime = DateTimeOffset.UtcNow
        });
    }
}
