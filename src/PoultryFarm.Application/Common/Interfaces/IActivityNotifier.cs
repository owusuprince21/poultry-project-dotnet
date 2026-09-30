using PoultryFarm.Domain.Common;

namespace PoultryFarm.Application.Common.Interfaces;

public interface IActivityNotifier
{
    Task NotifyCompanyAsync(
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
        CancellationToken cancellationToken = default);

    Task NotifySystemAdminsAsync(
        string title,
        string detail,
        Guid? actorUserId = null,
        string? actorName = null,
        string kind = "activity",
        string? targetType = null,
        Guid? targetId = null,
        CancellationToken cancellationToken = default);
}
