using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Api.Hubs;
using PoultryFarm.Api.Services;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IHubContext<ActivityHub> hubContext,
    IChatMessageProtector messageProtector) : ControllerBase
{
    private static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(10);

    [HttpGet("contacts")]
    public async Task<ActionResult<IReadOnlyCollection<ChatContactDto>>> Contacts(CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        await TouchAsync(currentUser, cancellationToken);

        var visibleUsers = await BuildVisibleUsersQuery(currentUser)
            .ToListAsync(cancellationToken);

        var companyIds = visibleUsers
            .Where(x => x.CompanyId.HasValue)
            .Select(x => x.CompanyId!.Value)
            .Distinct()
            .ToList();

        var companies = await dbContext.Companies
            .AsNoTracking()
            .Where(x => companyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var onlineThreshold = DateTimeOffset.UtcNow.AddMinutes(-3);
        var currentUserId = currentUser.Id;
        var contactIds = visibleUsers.Select(x => x.Id).ToList();
        var unreadCounts = await dbContext.ChatMessages
            .AsNoTracking()
            .Where(x => x.RecipientUserId == currentUserId && x.ReadAt == null && contactIds.Contains(x.SenderUserId))
            .GroupBy(x => x.SenderUserId)
            .Select(x => new { ContactId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ContactId, x => x.Count, cancellationToken);

        var latestMessages = await dbContext.ChatMessages
            .AsNoTracking()
            .Where(x =>
                (x.SenderUserId == currentUserId && contactIds.Contains(x.RecipientUserId)) ||
                (x.RecipientUserId == currentUserId && contactIds.Contains(x.SenderUserId)))
            .GroupBy(x => x.SenderUserId == currentUserId ? x.RecipientUserId : x.SenderUserId)
            .Select(x => x.OrderByDescending(message => message.SentAt).First())
            .ToListAsync(cancellationToken);

        var latestByContact = latestMessages.ToDictionary(
            x => x.SenderUserId == currentUserId ? x.RecipientUserId : x.SenderUserId,
            x => x);

        var contacts = visibleUsers
            .OrderByDescending(x => unreadCounts.GetValueOrDefault(x.Id))
            .ThenByDescending(x => latestByContact.TryGetValue(x.Id, out var lastMessage) ? lastMessage.SentAt : DateTimeOffset.MinValue)
            .ThenBy(x => x.IsSystemAdmin ? 0 : 1)
            .ThenBy(x => x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var companyName) ? companyName : "Platform")
            .ThenBy(x => x.FirstName)
            .ThenBy(x => x.UserName)
            .Select(x => new ChatContactDto(
                x.Id,
                x.UserName ?? string.Empty,
                x.FirstName,
                x.LastName,
                NormalizeRole(x),
                x.CompanyId,
                x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var companyName) ? companyName : null,
                x.LastSeenAt,
                x.LastSeenAt.HasValue && x.LastSeenAt.Value >= onlineThreshold,
                unreadCounts.GetValueOrDefault(x.Id),
                latestByContact.TryGetValue(x.Id, out var previewMessage) ? messageProtector.Unprotect(previewMessage.Body) : null,
                latestByContact.TryGetValue(x.Id, out var latestMessage) ? latestMessage.SentAt : null))
            .ToList();

        return Ok(contacts);
    }

    [HttpGet("messages/{contactId:guid}")]
    public async Task<ActionResult<IReadOnlyCollection<ChatMessageDto>>> Messages(Guid contactId, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!await CanChatWithAsync(currentUser, contactId, cancellationToken))
        {
            return Forbid();
        }

        await TouchAsync(currentUser, cancellationToken);

        var unreadMessages = await dbContext.ChatMessages
            .Where(x => x.SenderUserId == contactId && x.RecipientUserId == currentUser.Id && x.ReadAt == null)
            .ToListAsync(cancellationToken);
        var readAt = DateTimeOffset.UtcNow;

        foreach (var unreadMessage in unreadMessages)
        {
            unreadMessage.ReadAt = readAt;
        }

        if (unreadMessages.Count > 0)
        {
            var chatNotifications = await dbContext.AppNotifications
                .Where(x =>
                    x.RecipientUserId == currentUser.Id &&
                    x.Kind == "chat" &&
                    x.TargetId == contactId &&
                    x.ReadAt == null)
                .ToListAsync(cancellationToken);

            foreach (var notification in chatNotifications)
            {
                notification.ReadAt = readAt;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await hubContext.Clients.User(contactId.ToString())
                .SendAsync("chat.read", new ChatReadReceiptDto(currentUser.Id, readAt), cancellationToken);
        }

        var currentUserId = currentUser.Id;
        var chatMessages = await dbContext.ChatMessages
            .AsNoTracking()
            .Include(x => x.ReplyToMessage)
            .Include(x => x.Reactions)
            .Where(x =>
                (x.SenderUserId == currentUserId && x.RecipientUserId == contactId) ||
                (x.SenderUserId == contactId && x.RecipientUserId == currentUserId))
            .OrderBy(x => x.SentAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        var userIds = chatMessages
            .SelectMany(x => new[] { x.SenderUserId, x.RecipientUserId })
            .Distinct()
            .ToList();

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => DisplayName(x), cancellationToken);

        var messages = chatMessages
            .Select(x => ToDto(
                x,
                currentUserId,
                users.GetValueOrDefault(x.SenderUserId),
                users.GetValueOrDefault(x.RecipientUserId),
                x.ReplyToMessage == null ? null : users.GetValueOrDefault(x.ReplyToMessage.SenderUserId)))
            .ToList();

        return Ok(messages);
    }

    [HttpPost("messages")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(SendChatMessageRequest request, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (request.RecipientUserId == Guid.Empty)
        {
            return BadRequest(new { recipientUserId = "Select a recipient." });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { body = "Message is required." });
        }

        if (!await CanChatWithAsync(currentUser, request.RecipientUserId, cancellationToken))
        {
            return Forbid();
        }

        var recipient = await dbContext.Users.FindAsync([request.RecipientUserId], cancellationToken);
        if (recipient is null)
        {
            return NotFound(new { detail = "Recipient was not found." });
        }

        ChatMessage? replyToMessage = null;
        if (request.ReplyToMessageId.HasValue)
        {
            replyToMessage = await dbContext.ChatMessages
                .FirstOrDefaultAsync(x =>
                    x.Id == request.ReplyToMessageId.Value &&
                    ((x.SenderUserId == currentUser.Id && x.RecipientUserId == recipient.Id) ||
                     (x.SenderUserId == recipient.Id && x.RecipientUserId == currentUser.Id)),
                    cancellationToken);

            if (replyToMessage is null)
            {
                return BadRequest(new { replyToMessageId = "Reply message was not found in this conversation." });
            }
        }

        var message = new ChatMessage
        {
            SenderUserId = currentUser.Id,
            RecipientUserId = recipient.Id,
            CompanyId = currentUser.CompanyId ?? recipient.CompanyId,
            ReplyToMessageId = replyToMessage?.Id,
            Body = messageProtector.Protect(request.Body.Trim()),
            SentAt = DateTimeOffset.UtcNow
        };

        var notification = new AppNotification
        {
            RecipientUserId = recipient.Id,
            CompanyId = message.CompanyId,
            ActorUserId = currentUser.Id,
            ActorName = DisplayName(currentUser),
            Title = "New chat message",
            Detail = "New encrypted chat message",
            Kind = "chat",
            TargetType = "chat",
            TargetId = currentUser.Id,
            SentAt = message.SentAt
        };

        dbContext.ChatMessages.Add(message);
        dbContext.AppNotifications.Add(notification);
        await TouchAsync(currentUser, cancellationToken, save: false);
        await dbContext.SaveChangesAsync(cancellationToken);

        var senderDto = ToDto(
            message,
            currentUser.Id,
            DisplayName(currentUser),
            DisplayName(recipient),
            replyToMessage is null ? null : await DisplayNameForUserAsync(replyToMessage.SenderUserId, cancellationToken),
            replyToMessage is null ? null : messageProtector.Unprotect(replyToMessage.Body));
        var recipientDto = senderDto with { IsMine = false, CanEdit = false, CanDelete = false };

        await hubContext.Clients.User(currentUser.Id.ToString())
            .SendAsync("chat.message", senderDto, cancellationToken);
        await hubContext.Clients.User(recipient.Id.ToString())
            .SendAsync("chat.message", recipientDto, cancellationToken);
        await hubContext.Clients.User(recipient.Id.ToString())
            .SendAsync("activity.received", new
            {
                id = notification.Id,
                notification.Title,
                detail = notification.Detail,
                actorName = notification.ActorName,
                notification.Kind,
                notification.TargetType,
                notification.TargetId,
                at = notification.SentAt
            }, cancellationToken);

        return Ok(senderDto);
    }

    [HttpPut("messages/{id:guid}")]
    public async Task<ActionResult<ChatMessageDto>> UpdateMessage(Guid id, UpdateChatMessageRequest request, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { body = "Message is required." });
        }

        var message = await dbContext.ChatMessages
            .Include(x => x.ReplyToMessage)
            .Include(x => x.Reactions)
            .FirstOrDefaultAsync(x => x.Id == id && x.SenderUserId == currentUser.Id, cancellationToken);

        if (message is null)
        {
            return NotFound(new { detail = "Message was not found." });
        }

        if (DateTimeOffset.UtcNow - message.SentAt > EditWindow)
        {
            return BadRequest(new { detail = "Messages can only be edited within the first 10 minutes." });
        }

        message.Body = messageProtector.Protect(request.Body.Trim());
        message.EditedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var recipient = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == message.RecipientUserId, cancellationToken);
        var dto = ToDto(
            message,
            currentUser.Id,
            DisplayName(currentUser),
            recipient is null ? null : DisplayName(recipient),
            message.ReplyToMessage is null ? null : await DisplayNameForUserAsync(message.ReplyToMessage.SenderUserId, cancellationToken),
            message.ReplyToMessage is null ? null : messageProtector.Unprotect(message.ReplyToMessage.Body));
        var recipientDto = dto with { IsMine = false, CanEdit = false, CanDelete = false };

        await hubContext.Clients.User(currentUser.Id.ToString()).SendAsync("chat.message.updated", dto, cancellationToken);
        await hubContext.Clients.User(message.RecipientUserId.ToString()).SendAsync("chat.message.updated", recipientDto, cancellationToken);
        return Ok(dto);
    }

    [HttpDelete("messages/{id:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid id, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var message = await dbContext.ChatMessages.FirstOrDefaultAsync(x => x.Id == id && x.SenderUserId == currentUser.Id, cancellationToken);
        if (message is null)
        {
            return NotFound(new { detail = "Message was not found." });
        }

        message.IsDeleted = true;
        message.DeletedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var deleted = new ChatMessageDeletedDto(message.Id);
        await hubContext.Clients.User(currentUser.Id.ToString()).SendAsync("chat.message.deleted", deleted, cancellationToken);
        await hubContext.Clients.User(message.RecipientUserId.ToString()).SendAsync("chat.message.deleted", deleted, cancellationToken);
        return NoContent();
    }

    [HttpPost("messages/{id:guid}/reactions")]
    public async Task<IActionResult> React(Guid id, ChatReactionRequest request, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Emoji))
        {
            return BadRequest(new { emoji = "Select a reaction." });
        }

        var message = await dbContext.ChatMessages
            .Include(x => x.Reactions)
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                (x.SenderUserId == currentUser.Id || x.RecipientUserId == currentUser.Id),
                cancellationToken);

        if (message is null)
        {
            return NotFound(new { detail = "Message was not found." });
        }

        var emoji = request.Emoji.Trim();
        var existing = message.Reactions.FirstOrDefault(x => x.UserId == currentUser.Id && x.Emoji == emoji);
        if (existing is null)
        {
            dbContext.ChatMessageReactions.Add(new ChatMessageReaction
            {
                ChatMessageId = message.Id,
                UserId = currentUser.Id,
                Emoji = emoji
            });
        }
        else
        {
            dbContext.ChatMessageReactions.Remove(existing);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var reactions = await GetReactionSummaryAsync(message.Id, currentUser.Id, cancellationToken);
        var senderReactions = message.SenderUserId == currentUser.Id
            ? reactions
            : await GetReactionSummaryAsync(message.Id, message.SenderUserId, cancellationToken);
        var recipientReactions = message.RecipientUserId == currentUser.Id
            ? reactions
            : await GetReactionSummaryAsync(message.Id, message.RecipientUserId, cancellationToken);

        await hubContext.Clients.User(message.SenderUserId.ToString())
            .SendAsync("chat.reaction.updated", new ChatReactionUpdatedDto(message.Id, senderReactions), cancellationToken);
        await hubContext.Clients.User(message.RecipientUserId.ToString())
            .SendAsync("chat.reaction.updated", new ChatReactionUpdatedDto(message.Id, recipientReactions), cancellationToken);

        return Ok(new ChatReactionUpdatedDto(message.Id, reactions));
    }

    [HttpPost("messages/{contactId:guid}/read")]
    public async Task<IActionResult> MarkConversationRead(Guid contactId, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (!await CanChatWithAsync(currentUser, contactId, cancellationToken))
        {
            return Forbid();
        }

        var readAt = DateTimeOffset.UtcNow;
        var unreadMessages = await dbContext.ChatMessages
            .Where(x => x.SenderUserId == contactId && x.RecipientUserId == currentUser.Id && x.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var unreadMessage in unreadMessages)
        {
            unreadMessage.ReadAt = readAt;
        }

        var chatNotifications = await dbContext.AppNotifications
            .Where(x =>
                x.RecipientUserId == currentUser.Id &&
                x.Kind == "chat" &&
                x.TargetId == contactId &&
                x.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in chatNotifications)
        {
            notification.ReadAt = readAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (unreadMessages.Count > 0)
        {
            await hubContext.Clients.User(contactId.ToString())
                .SendAsync("chat.read", new ChatReadReceiptDto(currentUser.Id, readAt), cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("typing")]
    public async Task<IActionResult> Typing(TypingRequest request, CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (request.RecipientUserId == Guid.Empty || !await CanChatWithAsync(currentUser, request.RecipientUserId, cancellationToken))
        {
            return Forbid();
        }

        await hubContext.Clients.User(request.RecipientUserId.ToString())
            .SendAsync("chat.typing", new TypingDto(currentUser.Id, DisplayName(currentUser), request.IsTyping), cancellationToken);

        return NoContent();
    }

    private IQueryable<ApplicationUser> BuildVisibleUsersQuery(ApplicationUser currentUser)
    {
        var currentRole = NormalizeRole(currentUser);

        var query = dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id != currentUser.Id);

        if (currentRole is AppRoles.SuperAdmin or AppRoles.SubAdmin or AppRoles.SystemAdmin)
        {
            return query;
        }

        if (currentRole == AppRoles.FarmAdmin)
        {
            return query.Where(user =>
                user.IsSystemAdmin ||
                (user.CompanyId == currentUser.CompanyId && user.FarmRole == UserRole.Worker));
        }

        return query.Where(user =>
            user.CompanyId == currentUser.CompanyId &&
            !user.IsSystemAdmin &&
            (user.FarmRole == UserRole.Worker || user.FarmRole == UserRole.Admin));
    }

    private async Task<bool> CanChatWithAsync(ApplicationUser currentUser, Guid contactId, CancellationToken cancellationToken)
    {
        return await BuildVisibleUsersQuery(currentUser).AnyAsync(x => x.Id == contactId, cancellationToken);
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var id)
            ? await userManager.FindByIdAsync(id.ToString())
            : null;
    }

    private async Task TouchAsync(ApplicationUser user, CancellationToken cancellationToken, bool save = true)
    {
        user.LastSeenAt = DateTimeOffset.UtcNow;
        dbContext.Users.Update(user);
        if (save)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string NormalizeRole(ApplicationUser user)
    {
        if (user.IsSystemAdmin)
        {
            return user.FarmRole == UserRole.SubAdmin ? AppRoles.SubAdmin : AppRoles.SuperAdmin;
        }

        return user.FarmRole == UserRole.Admin ? AppRoles.FarmAdmin : AppRoles.Worker;
    }

    private static string DisplayName(ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "User" : name;
    }

    private async Task<string?> DisplayNameForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return user is null ? null : DisplayName(user);
    }

    private ChatMessageDto ToDto(
        ChatMessage message,
        Guid currentUserId,
        string? senderDisplayName,
        string? recipientDisplayName,
        string? replySenderDisplayName,
        string? replyPreview = null)
    {
        return new ChatMessageDto(
            message.Id,
            message.SenderUserId,
            message.RecipientUserId,
            messageProtector.Unprotect(message.Body),
            message.SentAt,
            message.SenderUserId == currentUserId,
            senderDisplayName,
            recipientDisplayName,
            message.ReadAt,
            message.ReplyToMessageId,
            replyPreview ?? (message.ReplyToMessage is null ? null : messageProtector.Unprotect(message.ReplyToMessage.Body)),
            replySenderDisplayName,
            message.EditedAt,
            message.SenderUserId == currentUserId && DateTimeOffset.UtcNow - message.SentAt <= EditWindow,
            message.SenderUserId == currentUserId,
            BuildReactionSummary(message.Reactions, currentUserId));
    }

    private static IReadOnlyCollection<ChatReactionDto> BuildReactionSummary(IEnumerable<ChatMessageReaction> reactions, Guid currentUserId) =>
        reactions
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.Emoji)
            .Select(x => new ChatReactionDto(x.Key, x.Count(), x.Any(r => r.UserId == currentUserId)))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Emoji)
            .ToList();

    private async Task<IReadOnlyCollection<ChatReactionDto>> GetReactionSummaryAsync(Guid messageId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var reactions = await dbContext.ChatMessageReactions
            .AsNoTracking()
            .Where(x => x.ChatMessageId == messageId)
            .ToListAsync(cancellationToken);

        return BuildReactionSummary(reactions, currentUserId);
    }
}

public sealed record ChatContactDto(
    Guid Id,
    string Username,
    string? FirstName,
    string? LastName,
    string Role,
    Guid? CompanyId,
    string? CompanyName,
    DateTimeOffset? LastSeenAt,
    bool IsOnline,
    int UnreadCount = 0,
    string? LastMessagePreview = null,
    DateTimeOffset? LastMessageAt = null);

public sealed record ChatMessageDto(
    Guid Id,
    Guid SenderUserId,
    Guid RecipientUserId,
    string Body,
    DateTimeOffset SentAt,
    bool IsMine,
    string? SenderDisplayName = null,
    string? RecipientDisplayName = null,
    DateTimeOffset? ReadAt = null,
    Guid? ReplyToMessageId = null,
    string? ReplyPreview = null,
    string? ReplySenderDisplayName = null,
    DateTimeOffset? EditedAt = null,
    bool CanEdit = false,
    bool CanDelete = false,
    IReadOnlyCollection<ChatReactionDto>? Reactions = null);

public sealed record SendChatMessageRequest(Guid RecipientUserId, string Body, Guid? ReplyToMessageId = null);
public sealed record UpdateChatMessageRequest(string Body);
public sealed record ChatReactionRequest(string Emoji);
public sealed record ChatReactionDto(string Emoji, int Count, bool ReactedByMe);
public sealed record ChatReactionUpdatedDto(Guid MessageId, IReadOnlyCollection<ChatReactionDto> Reactions);
public sealed record ChatMessageDeletedDto(Guid MessageId);
public sealed record ChatReadReceiptDto(Guid ContactId, DateTimeOffset ReadAt);
public sealed record TypingRequest(Guid RecipientUserId, bool IsTyping);
public sealed record TypingDto(Guid SenderUserId, string SenderDisplayName, bool IsTyping);
