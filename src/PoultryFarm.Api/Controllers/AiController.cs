using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PoultryFarm.Api.Services;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class AiController(OpenAiService openAiService, ILogger<AiController> logger) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromBody] AiRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { detail = "Message is required." });
        }

        try
        {
            var answer = await openAiService.AskAsync(request.Message, cancellationToken);
            return Ok(new { answer });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "OpenAI ask endpoint failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { detail = ex.Message });
        }
    }
}

public sealed class AiRequest
{
    public string Message { get; set; } = string.Empty;
}
