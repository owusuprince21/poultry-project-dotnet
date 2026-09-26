namespace PoultryFarm.Application.Common.Interfaces;

public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogWriteRequest request, CancellationToken cancellationToken = default);
}

public sealed record AuditLogWriteRequest(
    string Action,
    string Category,
    string TargetType,
    string TargetName,
    string Detail,
    Guid? TargetId = null,
    Guid? CompanyId = null,
    string? CompanyName = null,
    string? TargetUsername = null,
    Guid? ActorUserId = null,
    string? ActorName = null,
    string? ActorUsername = null,
    string? ActorRole = null);
