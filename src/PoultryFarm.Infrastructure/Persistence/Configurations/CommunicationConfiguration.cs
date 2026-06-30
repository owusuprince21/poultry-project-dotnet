using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoultryFarm.Domain.Communication;

namespace PoultryFarm.Infrastructure.Persistence.Configurations;

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Body).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.ReplyToMessage).WithMany().HasForeignKey(x => x.ReplyToMessageId);
        builder.HasMany(x => x.Reactions).WithOne(x => x.ChatMessage).HasForeignKey(x => x.ChatMessageId);
        builder.HasIndex(x => new { x.SenderUserId, x.RecipientUserId, x.SentAt });
        builder.HasIndex(x => new { x.RecipientUserId, x.SentAt });
    }
}

public sealed class ChatMessageReactionConfiguration : IEntityTypeConfiguration<ChatMessageReaction>
{
    public void Configure(EntityTypeBuilder<ChatMessageReaction> builder)
    {
        builder.ToTable("ChatMessageReactions");
        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Emoji).HasMaxLength(16).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.ChatMessageId, x.UserId, x.Emoji }).IsUnique();
    }
}

public sealed class FarmAssistanceMessageConfiguration : IEntityTypeConfiguration<FarmAssistanceMessage>
{
    public void Configure(EntityTypeBuilder<FarmAssistanceMessage> builder)
    {
        builder.ToTable("FarmAssistanceMessages");
        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Sender).HasMaxLength(24).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.SentAt });
    }
}

public sealed class AppNotificationConfiguration : IEntityTypeConfiguration<AppNotification>
{
    public void Configure(EntityTypeBuilder<AppNotification> builder)
    {
        builder.ToTable("AppNotifications");
        builder.HasKey(x => x.Id);
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.ActorName).HasMaxLength(200);
        builder.Property(x => x.Title).HasMaxLength(180).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(60);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.RecipientUserId, x.ReadAt, x.SentAt });
        builder.HasIndex(x => new { x.CompanyId, x.SentAt });
    }
}
