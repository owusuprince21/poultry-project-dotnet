using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Identity;

/// <summary>
/// Allow-list of dashboard page keys for a worker (e.g. production, feed, sales).
/// </summary>
public sealed class WorkerPagePermission : AuditableEntity
{
    public Guid UserId { get; set; }
    public string PageKey { get; set; } = string.Empty;
}
