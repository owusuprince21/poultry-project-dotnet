using Microsoft.EntityFrameworkCore;
using PoultryFarm.Domain.Audit;
using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Domain.Feed;
using PoultryFarm.Domain.Health;
using PoultryFarm.Domain.Identity;
using PoultryFarm.Domain.Marketplace;
using PoultryFarm.Domain.Production;
using PoultryFarm.Domain.Sales;
using PoultryFarm.Domain.Schedules;

namespace PoultryFarm.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Company> Companies { get; }
    DbSet<Batch> Batches { get; }
    DbSet<BatchVariant> BatchVariants { get; }
    DbSet<EggProduction> EggProductions { get; }
    DbSet<FeedConsumption> FeedConsumptions { get; }
    DbSet<FeedStock> FeedStocks { get; }
    DbSet<FeedStockLot> FeedStockLots { get; }
    DbSet<FeedConfiguration> FeedConfigurations { get; }
    DbSet<EggSale> EggSales { get; }
    DbSet<EggSaleItem> EggSaleItems { get; }
    DbSet<BirdSale> BirdSales { get; }
    DbSet<BirdHealthEvent> BirdHealthEvents { get; }
    DbSet<Medication> Medications { get; }
    DbSet<DebeakingSchedule> DebeakingSchedules { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<ChatMessage> ChatMessages { get; }
    DbSet<FarmAssistanceMessage> FarmAssistanceMessages { get; }
    DbSet<AppNotification> AppNotifications { get; }
    DbSet<FarmerRegistration> FarmerRegistrations { get; }
    DbSet<MarketplaceListing> MarketplaceListings { get; }
    DbSet<FarmActivityPost> FarmActivityPosts { get; }
    DbSet<FarmActivityComment> FarmActivityComments { get; }
    DbSet<FarmActivityCommentReaction> FarmActivityCommentReactions { get; }
    DbSet<FarmActivityLike> FarmActivityLikes { get; }
    DbSet<MarketplaceMediaAsset> MarketplaceMediaAssets { get; }
    DbSet<MarketplaceInquiry> MarketplaceInquiries { get; }
    DbSet<MarketplaceConversation> MarketplaceConversations { get; }
    DbSet<PasswordInvite> PasswordInvites { get; }
    DbSet<WorkerPagePermission> WorkerPagePermissions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
