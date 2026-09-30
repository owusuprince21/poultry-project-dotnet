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
        bool includeActor = false,
        CancellationToken cancellationToken = default)
    {
        var resolvedActor = await ResolveActorNameAsync(actorUserId, actorName, cancellationToken);
        var companyDetail = EnsureActorInDetail(detail, resolvedActor);

        var companyQuery = dbContext.Users
            .Where(x =>
                x.CompanyId == companyId &&
                !x.IsSystemAdmin &&
                x.FarmRole != UserRole.SystemAdmin &&
                x.FarmRole != UserRole.SubAdmin &&
                (includeActor || !actorUserId.HasValue || x.Id != actorUserId.Value))
            .AsQueryable();

        if (recipientRoles is { Count: > 0 })
        {
            companyQuery = companyQuery.Where(x => recipientRoles.Contains(x.FarmRole));
        }

        var companyRecipients = await companyQuery
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        await PersistAndSendAsync(
            companyRecipients,
            companyId,
            title,
            companyDetail,
            actorUserId,
            resolvedActor,
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
        var resolvedActor = await ResolveActorNameAsync(actorUserId, actorName, cancellationToken);
        var recipients = await dbContext.Users
            .Where(x => x.IsSystemAdmin && (!actorUserId.HasValue || x.Id != actorUserId.Value))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        await PersistAndSendAsync(
            recipients,
            null,
            title,
            EnsureActorInDetail(detail, resolvedActor),
            actorUserId,
            resolvedActor,
            kind,
            targetType,
            targetId,
            cancellationToken);
    }

    private async Task<string> ResolveActorNameAsync(Guid? actorUserId, string? actorName, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(actorName) &&
            !IsGenericActorLabel(actorName))
        {
            return actorName.Trim();
        }

        if (actorUserId.HasValue)
        {
            var user = await dbContext.Users
                .AsNoTracking()
                .Where(x => x.Id == actorUserId.Value)
                .Select(x => new { x.FirstName, x.LastName, x.UserName })
                .FirstOrDefaultAsync(cancellationToken);

            if (user is not null)
            {
                var fullName = $"{user.FirstName} {user.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(fullName))
                {
                    return fullName;
                }

                if (!string.IsNullOrWhiteSpace(user.UserName))
                {
                    return user.UserName!;
                }
            }
        }

        return string.IsNullOrWhiteSpace(actorName) ? "A team member" : actorName.Trim();
    }

    private static bool IsGenericActorLabel(string actorName)
    {
        var value = actorName.Trim();
        return value.Equals("System", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Farm operations", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Batch management", StringComparison.OrdinalIgnoreCase)
            || value.Equals("System reminder", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureActorInDetail(string detail, string actorName)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return $"{actorName} performed an action.";
        }

        if (detail.Contains(actorName, StringComparison.OrdinalIgnoreCase))
        {
            return detail;
        }

        return $"{actorName}: {detail}";
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
            ActorName = actorName ?? "A team member",
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
        actorName = actorName ?? "A team member",
        kind,
        targetType,
        targetId,
        at = DateTimeOffset.UtcNow
    };
}
