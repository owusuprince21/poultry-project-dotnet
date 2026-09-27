using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Controllers;
using PoultryFarm.Api.Hubs;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Services;

public sealed class MarketplaceChatFanout(
    ApplicationDbContext dbContext,
    IHubContext<ActivityHub> hubContext)
{
    public async Task<IReadOnlyList<Guid>> ParticipantIdsAsync(Guid companyId, Guid buyerUserId, CancellationToken cancellationToken)
    {
        var staffIds = await dbContext.Users
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                !x.IsDeleted &&
                (x.FarmRole == UserRole.Admin || x.FarmRole == UserRole.Worker))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (!staffIds.Contains(buyerUserId))
        {
            staffIds.Add(buyerUserId);
        }

        return staffIds;
    }

    public async Task PublishMessageAsync(
        string eventName,
        ChatMessageDto template,
        Guid companyId,
        Guid buyerUserId,
        Guid actorUserId,
        string notificationTitle,
        string actorName,
        bool createNotifications,
        CancellationToken cancellationToken)
    {
        var participants = await ParticipantIdsAsync(companyId, buyerUserId, cancellationToken);
        var notifications = new List<AppNotification>();
        var sentAt = DateTimeOffset.UtcNow;

        foreach (var userId in participants)
        {
            var isBuyer = userId == buyerUserId;
            var isSender = template.SenderUserId == userId;
            var dto = template with
            {
                IsMine = isSender,
                CanEdit = isSender && template.CanEdit,
                CanDelete = isSender && template.CanDelete,
                MarketplaceBuyerUserId = isBuyer ? null : buyerUserId
            };

            await hubContext.Clients.User(userId.ToString()).SendAsync(eventName, dto, cancellationToken);

            if (createNotifications && userId != actorUserId && !isBuyer)
            {
                notifications.Add(new AppNotification
                {
                    RecipientUserId = userId,
                    CompanyId = companyId,
                    ActorUserId = actorUserId,
                    ActorName = actorName,
                    Title = notificationTitle,
                    Detail = "New marketplace chat message",
                    Kind = "chat",
                    TargetType = "chat",
                    TargetId = buyerUserId,
                    SentAt = sentAt
                });
            }
        }

        if (notifications.Count == 0)
        {
            return;
        }

        dbContext.AppNotifications.AddRange(notifications);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await hubContext.Clients.User(notification.RecipientUserId.ToString())
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
        }
    }

    public async Task PublishDeletedAsync(
        ChatMessageDeletedDto deleted,
        Guid companyId,
        Guid buyerUserId,
        CancellationToken cancellationToken)
    {
        foreach (var userId in await ParticipantIdsAsync(companyId, buyerUserId, cancellationToken))
        {
            await hubContext.Clients.User(userId.ToString())
                .SendAsync("chat.message.deleted", deleted, cancellationToken);
        }
    }
}
