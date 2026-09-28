using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PoultryFarm.Domain.Identity;
using PoultryFarm.Domain.Marketplace;

namespace PoultryFarm.Infrastructure.Persistence.Configurations;

public sealed class MarketplaceConfiguration :
    IEntityTypeConfiguration<FarmerRegistration>,
    IEntityTypeConfiguration<MarketplaceListing>,
    IEntityTypeConfiguration<FarmActivityPost>,
    IEntityTypeConfiguration<FarmActivityImage>,
    IEntityTypeConfiguration<FarmActivityComment>,
    IEntityTypeConfiguration<FarmActivityCommentReaction>,
    IEntityTypeConfiguration<FarmActivityLike>,
    IEntityTypeConfiguration<MarketplaceMediaAsset>,
    IEntityTypeConfiguration<MarketplaceInquiry>,
    IEntityTypeConfiguration<MarketplaceConversation>,
    IEntityTypeConfiguration<PasswordInvite>,
    IEntityTypeConfiguration<WorkerPagePermission>
{
    public void Configure(EntityTypeBuilder<FarmerRegistration> builder)
    {
        builder.ToTable("FarmerRegistrations");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.FarmName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContactFirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContactLastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Location).HasMaxLength(255);
        builder.Property(x => x.RequestedUsername).HasMaxLength(100);
        builder.Property(x => x.ReviewNotes).HasMaxLength(2000);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Email);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId);
    }

    public void Configure(EntityTypeBuilder<MarketplaceListing> builder)
    {
        builder.ToTable("MarketplaceListings");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.UnitLabel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PriceAmount).HasPrecision(18, 2);
        builder.Property(x => x.PriceText).HasMaxLength(100);
        builder.Property(x => x.ImageUrl).HasMaxLength(1000);
        builder.Property(x => x.QuantityOffered).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId);
        builder.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId);
        builder.HasOne(x => x.BatchVariant).WithMany().HasForeignKey(x => x.BatchVariantId);
    }

    public void Configure(EntityTypeBuilder<FarmActivityPost> builder)
    {
        builder.ToTable("FarmActivityPosts");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(8000).IsRequired();
        builder.Property(x => x.MediaUrl).HasMaxLength(1000);
        builder.HasIndex(x => new { x.CompanyId, x.IsPublished });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId);
        builder.HasMany(x => x.Comments).WithOne(x => x.Post!).HasForeignKey(x => x.PostId);
        builder.HasMany(x => x.Likes).WithOne(x => x.Post!).HasForeignKey(x => x.PostId);
        builder.HasMany(x => x.Images).WithOne(x => x.Post!).HasForeignKey(x => x.PostId);
    }

    public void Configure(EntityTypeBuilder<FarmActivityImage> builder)
    {
        builder.ToTable("FarmActivityImages");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Url).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.PostId, x.SortOrder });
        builder.HasOne(x => x.Post).WithMany(x => x.Images).HasForeignKey(x => x.PostId);
    }

    public void Configure(EntityTypeBuilder<FarmActivityComment> builder)
    {
        builder.ToTable("FarmActivityComments");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AuthorReactorKey).HasMaxLength(100);
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => new { x.PostId, x.CreatedAt });
        builder.HasIndex(x => x.AuthorReactorKey);
        builder.HasIndex(x => x.ParentCommentId);
        builder.HasOne(x => x.Post).WithMany(x => x.Comments).HasForeignKey(x => x.PostId);
        builder.HasOne(x => x.ParentComment)
            .WithMany(x => x.Replies)
            .HasForeignKey(x => x.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Reactions).WithOne(x => x.Comment!).HasForeignKey(x => x.CommentId);
    }

    public void Configure(EntityTypeBuilder<FarmActivityCommentReaction> builder)
    {
        builder.ToTable("FarmActivityCommentReactions");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Emoji).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ReactorKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.CommentId, x.ReactorKey, x.Emoji }).IsUnique();
        builder.HasOne(x => x.Comment).WithMany(x => x.Reactions).HasForeignKey(x => x.CommentId);
    }

    public void Configure(EntityTypeBuilder<FarmActivityLike> builder)
    {
        builder.ToTable("FarmActivityLikes");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.ReactorKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.PostId, x.ReactorKey }).IsUnique();
        builder.HasOne(x => x.Post).WithMany(x => x.Likes).HasForeignKey(x => x.PostId);
    }

    public void Configure(EntityTypeBuilder<MarketplaceMediaAsset> builder)
    {
        builder.ToTable("MarketplaceMediaAssets");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.Folder).HasMaxLength(50).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Data).IsRequired();
        builder.HasIndex(x => new { x.Folder, x.CreatedAt });
    }

    public void Configure(EntityTypeBuilder<MarketplaceInquiry> builder)
    {
        builder.ToTable("MarketplaceInquiries");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.BuyerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BuyerEmail).HasMaxLength(255);
        builder.Property(x => x.BuyerPhone).HasMaxLength(30);
        builder.Property(x => x.Message).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.QuantityRequested).HasPrecision(18, 2);
        builder.Property(x => x.FarmResponseNotes).HasMaxLength(2000);
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId);
    }

    public void Configure(EntityTypeBuilder<MarketplaceConversation> builder)
    {
        builder.ToTable("MarketplaceConversations");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.HasIndex(x => new { x.ListingId, x.BuyerUserId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.BuyerUserId });
        builder.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId);
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId);
        builder.HasOne(x => x.Inquiry).WithMany().HasForeignKey(x => x.InquiryId);
    }

    public void Configure(EntityTypeBuilder<PasswordInvite> builder)
    {
        builder.ToTable("PasswordInvites");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
    }

    public void Configure(EntityTypeBuilder<WorkerPagePermission> builder)
    {
        builder.ToTable("WorkerPagePermissions");
        builder.HasQueryFilter(x => !x.IsDeleted);
        builder.Property(x => x.PageKey).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.PageKey }).IsUnique();
    }
}
