using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Infrastructure.Identity;

namespace PoultryFarm.Api.Services;

public sealed class ControllerAudit(
    IAuditLogWriter auditLogWriter,
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task WriteAsync(
        string action,
        string category,
        string targetType,
        string targetName,
        string detail,
        Guid? targetId = null,
        Guid? companyId = null,
        string? companyName = null,
        string? targetUsername = null,
        CancellationToken cancellationToken = default)
    {
        var user = httpContextAccessor.HttpContext?.User;
        ApplicationUser? actor = null;
        var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userId, out var parsedId))
        {
            actor = await userManager.FindByIdAsync(parsedId.ToString());
        }

        var actorName = actor is null
            ? user?.Identity?.Name ?? "System user"
            : $"{actor.FirstName} {actor.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(actorName))
        {
            actorName = actor?.UserName ?? "System user";
        }

        await auditLogWriter.WriteAsync(
            new AuditLogWriteRequest(
                Action: action,
                Category: category,
                TargetType: targetType,
                TargetName: targetName,
                Detail: detail,
                TargetId: targetId,
                CompanyId: companyId,
                CompanyName: companyName,
                TargetUsername: targetUsername,
                ActorUserId: parsedId == Guid.Empty ? null : parsedId,
                ActorName: actorName,
                ActorUsername: actor?.UserName ?? user?.Identity?.Name ?? string.Empty,
                ActorRole: user is null ? string.Empty : PermissionHelpers.GetNormalizedRole(user)),
            cancellationToken);
    }
}
