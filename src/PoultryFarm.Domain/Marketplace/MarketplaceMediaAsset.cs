using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Marketplace;

public sealed class MarketplaceMediaAsset : AuditableEntity
{
    public string Folder { get; set; } = "listings";
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long SizeBytes { get; set; }
    public byte[] Data { get; set; } = [];
}
