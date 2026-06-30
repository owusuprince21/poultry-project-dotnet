using PoultryFarm.Domain.Common;

namespace PoultryFarm.Application.Batches.DTOs;

public sealed record BatchVariantDto(
    Guid Id,
    VariantColor Color,
    EggColor EggColor,
    int InitialCount,
    int CurrentCount,
    string? Notes);

public sealed record BatchDto(
    Guid Id,
    Guid CompanyId,
    string BatchNumber,
    BirdType BirdType,
    DateOnly ArrivalDate,
    string Breed,
    string? Supplier,
    DateOnly ExpectedSaleDate,
    BatchStatus Status,
    int InitialCount,
    int CurrentCount,
    decimal SurvivalRate,
    IReadOnlyCollection<BatchVariantDto> Variants);

public sealed record CreateBatchRequest(
    Guid CompanyId,
    BirdType BirdType,
    DateOnly ArrivalDate,
    string Breed,
    string? Supplier,
    DateOnly ExpectedSaleDate,
    string? Notes,
    IReadOnlyCollection<CreateBatchVariantRequest> Variants);

public sealed record CreateBatchVariantRequest(
    VariantColor Color,
    int InitialCount,
    string? Notes);
