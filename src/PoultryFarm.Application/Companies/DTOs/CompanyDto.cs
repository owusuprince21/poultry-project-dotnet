namespace PoultryFarm.Application.Companies.DTOs;

public sealed record CompanyDto(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive);

public sealed record CreateCompanyRequest(
    string Name,
    string? Email,
    string? Phone,
    string? Address);
