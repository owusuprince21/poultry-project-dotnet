using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<NotificationDto>>> Get(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var notifications = await dbContext.AppNotifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == user.Id)
            .OrderByDescending(x => x.SentAt)
            .Take(100)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);

        return Ok(notifications);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var notification = await dbContext.AppNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == user.Id, cancellationToken);

        if (notification is null)
        {
            return NotFound(new { detail = "Notification was not found." });
        }

        notification.ReadAt ??= DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("chat/{contactId:guid}/read")]
    public async Task<IActionResult> MarkChatRead(Guid contactId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var now = DateTimeOffset.UtcNow;
        var notifications = await dbContext.AppNotifications
            .Where(x =>
                x.RecipientUserId == user.Id &&
                x.Kind == "chat" &&
                x.TargetId == contactId &&
                x.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.ReadAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var notification = await dbContext.AppNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == user.Id, cancellationToken);

        if (notification is null)
        {
            return NotFound(new { detail = "Notification was not found." });
        }

        notification.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var notifications = await dbContext.AppNotifications
            .Where(x => x.RecipientUserId == user.Id)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.IsDeleted = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var id)
            ? await userManager.FindByIdAsync(id.ToString())
            : null;
    }

    private static NotificationDto ToDto(AppNotification notification) => new(
        notification.Id,
        notification.Title,
        notification.ActorName,
        notification.Detail,
        notification.Kind,
        notification.TargetType,
        notification.TargetId,
        notification.SentAt,
        notification.ReadAt);
}

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string ActorName,
    string Detail,
    string Kind,
    string? TargetType,
    Guid? TargetId,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt);
