using PoultryFarm.Domain.Common;

namespace PoultryFarm.Domain.Communication;

public sealed class ChatMessageReaction : AuditableEntity
{
    public Guid ChatMessageId { get; set; }
    public ChatMessage? ChatMessage { get; set; }
    public Guid UserId { get; set; }
    public string Emoji { get; set; } = string.Empty;
}
