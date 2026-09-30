using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;

namespace PoultryFarm.Domain.Production;

public sealed class EggProduction : AuditableEntity
{
    public const int EggsPerCrate = 30;

    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid BatchVariantId { get; set; }
    public BatchVariant? BatchVariant { get; set; }
    public DateOnly Date { get; set; }
    public EggColor EggColor { get; set; }
    public EggCollectionType CollectionType { get; set; }
    public EggCollectionPeriod CollectionPeriod { get; set; } = EggCollectionPeriod.Morning;
    public int SmallEggs { get; private set; }
    public int MediumEggs { get; private set; }
    public int LargeEggs { get; private set; }
    public int ExtraLargeEggs { get; private set; }
    public int UnsortedEggs { get; private set; }
    public int SmallCrates { get; set; }
    public int SmallPieces { get; set; }
    public int MediumCrates { get; set; }
    public int MediumPieces { get; set; }
    public int LargeCrates { get; set; }
    public int LargePieces { get; set; }
    public int ExtraLargeCrates { get; set; }
    public int ExtraLargePieces { get; set; }
    public int UnsortedCrates { get; set; }
    public int UnsortedPieces { get; set; }
    public string? Notes { get; set; }
    public int TotalEggs => SmallEggs + MediumEggs + LargeEggs + ExtraLargeEggs + UnsortedEggs;

    public void RecalculateTotals()
    {
        SmallPieces = Math.Clamp(SmallPieces, 0, EggsPerCrate - 1);
        MediumPieces = Math.Clamp(MediumPieces, 0, EggsPerCrate - 1);
        LargePieces = Math.Clamp(LargePieces, 0, EggsPerCrate - 1);
        ExtraLargePieces = Math.Clamp(ExtraLargePieces, 0, EggsPerCrate - 1);
        UnsortedPieces = Math.Clamp(UnsortedPieces, 0, EggsPerCrate - 1);

        if (CollectionType == EggCollectionType.Sorted)
        {
            SmallEggs = SmallCrates * EggsPerCrate + SmallPieces;
            MediumEggs = MediumCrates * EggsPerCrate + MediumPieces;
            LargeEggs = LargeCrates * EggsPerCrate + LargePieces;
            ExtraLargeEggs = ExtraLargeCrates * EggsPerCrate + ExtraLargePieces;
            UnsortedEggs = 0;
            UnsortedCrates = 0;
            UnsortedPieces = 0;
            return;
        }

        UnsortedEggs = UnsortedCrates * EggsPerCrate + UnsortedPieces;
        SmallEggs = 0;
        MediumEggs = 0;
        LargeEggs = 0;
        ExtraLargeEggs = 0;
        SmallCrates = 0;
        SmallPieces = 0;
        MediumCrates = 0;
        MediumPieces = 0;
        LargeCrates = 0;
        LargePieces = 0;
        ExtraLargeCrates = 0;
        ExtraLargePieces = 0;
    }
}
