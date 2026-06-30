using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;

namespace PoultryFarm.UnitTests;

public sealed class BatchVariantTests
{
    [Fact]
    public void SetCurrentCount_ClampsToInitialCount()
    {
        var variant = new BatchVariant
        {
            Color = VariantColor.Brown,
            InitialCount = 100
        };

        variant.SetCurrentCount(125);

        Assert.Equal(100, variant.CurrentCount);
    }

    [Fact]
    public void DecreaseCurrentCount_RejectsCountAboveAvailableBirds()
    {
        var variant = new BatchVariant
        {
            Color = VariantColor.White,
            InitialCount = 50
        };
        variant.SetCurrentCount(10);

        Assert.Throws<InvalidOperationException>(() => variant.DecreaseCurrentCount(11));
    }
}
