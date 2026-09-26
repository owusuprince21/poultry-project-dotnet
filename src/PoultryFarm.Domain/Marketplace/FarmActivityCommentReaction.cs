using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Marketplace;

public sealed class FarmActivityCommentReaction : AuditableEntity
{
    public Guid CommentId { get; set; }
    public FarmActivityComment? Comment { get; set; }
    public string Emoji { get; set; } = string.Empty;
    public string ReactorKey { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
}
