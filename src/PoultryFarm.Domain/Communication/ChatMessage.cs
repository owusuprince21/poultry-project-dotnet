using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Communication;

public sealed class ChatMessage : AuditableEntity
{
    public Guid SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public ChatMessage? ReplyToMessage { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EditedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public ICollection<ChatMessageReaction> Reactions { get; set; } = [];
}
