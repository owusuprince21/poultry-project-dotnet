namespace PoultryFarm.Application.Common.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? CompanyId { get; }
    bool IsSystemAdmin { get; }
    bool IsCompanyAdmin { get; }
}
