using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Services;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/assistance")]
[Authorize]
public sealed class AssistanceController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IFarmAssistantAgent assistantAgent) : ControllerBase
{
    [HttpGet("messages")]
    public async Task<ActionResult<IReadOnlyCollection<AssistanceMessageDto>>> Messages(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!CanUseAssistance(user))
        {
            return Forbid();
        }

        var companyId = user.CompanyId!.Value;
        var messages = await dbContext.FarmAssistanceMessages
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.UserId == user.Id)
            .OrderBy(x => x.SentAt)
            .Select(x => new AssistanceMessageDto(x.Id, x.Sender, x.Body, x.SentAt))
            .ToListAsync(cancellationToken);

        return Ok(messages);
    }

    [HttpPost("messages")]
    public async Task<ActionResult<IReadOnlyCollection<AssistanceMessageDto>>> Send(SendAssistanceMessageRequest request, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!CanUseAssistance(user))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { body = "Message is required." });
        }

        var companyId = user.CompanyId!.Value;
        var userMessage = new FarmAssistanceMessage
        {
            UserId = user.Id,
            CompanyId = companyId,
            Sender = "user",
            Body = request.Body.Trim(),
            SentAt = DateTimeOffset.UtcNow
        };

        dbContext.FarmAssistanceMessages.Add(userMessage);

        var assistantBody = await assistantAgent.GetReplyAsync(
            companyId,
            DisplayName(user),
            request.Body,
            cancellationToken);
        var assistantMessage = new FarmAssistanceMessage
        {
            UserId = user.Id,
            CompanyId = companyId,
            Sender = "assistant",
            Body = assistantBody,
            SentAt = DateTimeOffset.UtcNow.AddMilliseconds(10)
        };

        dbContext.FarmAssistanceMessages.Add(assistantMessage);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new[]
        {
            new AssistanceMessageDto(userMessage.Id, userMessage.Sender, userMessage.Body, userMessage.SentAt),
            new AssistanceMessageDto(assistantMessage.Id, assistantMessage.Sender, assistantMessage.Body, assistantMessage.SentAt)
        });
    }

    [HttpDelete("messages")]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!CanUseAssistance(user))
        {
            return Forbid();
        }

        var messages = await dbContext.FarmAssistanceMessages
            .Where(x => x.UserId == user.Id && x.CompanyId == user.CompanyId)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.IsDeleted = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { detail = "Assistance history cleared successfully." });
    }

    [HttpDelete("messages/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!CanUseAssistance(user))
        {
            return Forbid();
        }

        var message = await dbContext.FarmAssistanceMessages
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == user.Id && x.CompanyId == user.CompanyId, cancellationToken);

        if (message is null)
        {
            return NotFound(new { detail = "Message was not found." });
        }

        message.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new { detail = "Message deleted successfully." });
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var id)
            ? await userManager.FindByIdAsync(id.ToString())
            : null;
    }

    private static bool CanUseAssistance(ApplicationUser user) =>
        !user.IsSystemAdmin && user.CompanyId.HasValue && user.FarmRole is UserRole.Admin or UserRole.Worker;

    private static string DisplayName(ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "there" : name;
    }
}

public sealed record AssistanceMessageDto(Guid Id, string Sender, string Body, DateTimeOffset SentAt);

public sealed record SendAssistanceMessageRequest(string Body);
