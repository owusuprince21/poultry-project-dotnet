using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Batches;

public sealed class BatchVariant : AuditableEntity
{
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public VariantColor Color { get; set; }
    public int InitialCount { get; set; }
    public int CurrentCount { get; private set; }
    public string? Notes { get; set; }

    public EggColor EggColor => Color switch
    {
        VariantColor.White => EggColor.White,
        VariantColor.Brown => EggColor.Brown,
        _ => EggColor.Mixed
    };

    public void SetCurrentCount(int value)
    {
        CurrentCount = Math.Clamp(value, 0, InitialCount);
    }

    public void DecreaseCurrentCount(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        }

        if (count > CurrentCount)
        {
            throw new InvalidOperationException("Count cannot exceed current birds available.");
        }

        CurrentCount -= count;
    }

    public void RestoreCurrentCount(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative.");
        }

        CurrentCount = Math.Min(InitialCount, CurrentCount + count);
    }
}
