using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Marketplace;

public sealed class FarmActivityComment : AuditableEntity
{
    public Guid PostId { get; set; }
    public FarmActivityPost? Post { get; set; }
    public Guid? ParentCommentId { get; set; }
    public FarmActivityComment? ParentComment { get; set; }
    public Guid? AuthorUserId { get; set; }
    public Guid? AuthorCompanyId { get; set; }
    public string? AuthorReactorKey { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public ICollection<FarmActivityComment> Replies { get; set; } = new List<FarmActivityComment>();
    public ICollection<FarmActivityCommentReaction> Reactions { get; set; } = new List<FarmActivityCommentReaction>();
}
