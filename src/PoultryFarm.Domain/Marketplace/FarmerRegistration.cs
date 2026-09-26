using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Marketplace;

public sealed class FarmerRegistration : AuditableEntity
{
    public string FarmName { get; set; } = string.Empty;
    public string ContactFirstName { get; set; } = string.Empty;
    public string ContactLastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public string? RequestedUsername { get; set; }
    public FarmerRegistrationStatus Status { get; set; } = FarmerRegistrationStatus.Pending;
    public string? ReviewNotes { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid? FarmAdminUserId { get; set; }
}
