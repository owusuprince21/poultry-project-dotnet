using Microsoft.AspNetCore.Identity;
using PoultryFarm.Domain.Common;

namespace PoultryFarm.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid? CompanyId { get; set; }
    public UserRole FarmRole { get; set; } = UserRole.Worker;
    public bool IsSystemAdmin { get; set; }
    public bool MustChangePassword { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
