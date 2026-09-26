using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Audit;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Infrastructure.Services;

public sealed class AuditLogWriter(ApplicationDbContext dbContext) : IAuditLogWriter
{
    public async Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = request.ActorUserId,
            ActorName = request.ActorName ?? "System",
            ActorUsername = request.ActorUsername ?? string.Empty,
            ActorRole = request.ActorRole ?? string.Empty,
            CompanyId = request.CompanyId,
            CompanyName = request.CompanyName,
            Action = request.Action,
            TargetType = request.TargetType,
            TargetId = request.TargetId,
            TargetName = request.TargetName,
            TargetUsername = request.TargetUsername,
            Detail = request.Detail,
            Category = request.Category
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
