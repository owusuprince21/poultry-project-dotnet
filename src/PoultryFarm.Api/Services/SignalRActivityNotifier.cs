using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Hubs;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Services;

public sealed class SignalRActivityNotifier(
    IHubContext<ActivityHub> hubContext,
    ApplicationDbContext dbContext) : IActivityNotifier
{
    public async Task NotifyCompanyAsync(
        Guid companyId,
        string title,
        string detail,
        Guid? actorUserId = null,
        string? actorName = null,
        string kind = "activity",
        string? targetType = null,
        Guid? targetId = null,
        IReadOnlyCollection<UserRole>? recipientRoles = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users
            .Where(x => x.CompanyId == companyId && (!actorUserId.HasValue || x.Id != actorUserId.Value))
            .AsQueryable();

        if (recipientRoles is { Count: > 0 })
        {
            query = query.Where(x => recipientRoles.Contains(x.FarmRole));
        }

        var recipients = await query
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        await PersistAndSendAsync(
            recipients,
            companyId,
            title,
            detail,
            actorUserId,
            actorName,
            kind,
            targetType,
            targetId,
            cancellationToken);
    }

    public async Task NotifySystemAdminsAsync(
        string title,
        string detail,
        Guid? actorUserId = null,
        string? actorName = null,
        string kind = "activity",
        string? targetType = null,
        Guid? targetId = null,
        CancellationToken cancellationToken = default)
    {
        var recipients = await dbContext.Users
            .Where(x => x.IsSystemAdmin && (!actorUserId.HasValue || x.Id != actorUserId.Value))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        await PersistAndSendAsync(
            recipients,
            null,
            title,
            detail,
            actorUserId,
            actorName,
            kind,
            targetType,
            targetId,
            cancellationToken);

        foreach (var recipient in recipients)
        {
            await hubContext.Clients.User(recipient.ToString())
                .SendAsync("activity.received", BuildPayload(Guid.NewGuid(), title, detail, actorName, kind, targetType, targetId), cancellationToken);
        }
    }

    private async Task PersistAndSendAsync(
        IReadOnlyCollection<Guid> recipients,
        Guid? companyId,
        string title,
        string detail,
        Guid? actorUserId,
        string? actorName,
        string kind,
        string? targetType,
        Guid? targetId,
        CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var notifications = recipients.Select(recipient => new AppNotification
        {
            RecipientUserId = recipient,
            CompanyId = companyId,
            ActorUserId = actorUserId,
            ActorName = actorName ?? "System",
            Title = title,
            Detail = detail,
            Kind = kind,
            TargetType = targetType,
            TargetId = targetId,
            SentAt = now
        }).ToList();

        dbContext.AppNotifications.AddRange(notifications);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            await hubContext.Clients.User(notification.RecipientUserId.ToString())
                .SendAsync("activity.received", BuildPayload(notification.Id, title, detail, actorName, kind, targetType, targetId), cancellationToken);
        }
    }

    private static object BuildPayload(Guid id, string title, string detail, string? actorName, string kind, string? targetType, Guid? targetId) => new
    {
        id,
        title,
        detail,
        actorName = actorName ?? "System",
        kind,
        targetType,
        targetId,
        at = DateTimeOffset.UtcNow
    };
}
