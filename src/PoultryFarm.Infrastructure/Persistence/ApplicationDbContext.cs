using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Audit;
using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Domain.Feed;
using PoultryFarm.Domain.Health;
using PoultryFarm.Domain.Production;
using PoultryFarm.Domain.Sales;
using PoultryFarm.Domain.Schedules;
using PoultryFarm.Infrastructure.Identity;

namespace PoultryFarm.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IApplicationDbContext
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<BatchVariant> BatchVariants => Set<BatchVariant>();
    public DbSet<EggProduction> EggProductions => Set<EggProduction>();
    public DbSet<FeedConsumption> FeedConsumptions => Set<FeedConsumption>();
    public DbSet<FeedStock> FeedStocks => Set<FeedStock>();
    public DbSet<FeedStockLot> FeedStockLots => Set<FeedStockLot>();
    public DbSet<FeedConfiguration> FeedConfigurations => Set<FeedConfiguration>();
    public DbSet<EggSale> EggSales => Set<EggSale>();
    public DbSet<EggSaleItem> EggSaleItems => Set<EggSaleItem>();
    public DbSet<BirdSale> BirdSales => Set<BirdSale>();
    public DbSet<BirdHealthEvent> BirdHealthEvents => Set<BirdHealthEvent>();
    public DbSet<Medication> Medications => Set<Medication>();
    public DbSet<DebeakingSchedule> DebeakingSchedules => Set<DebeakingSchedule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatMessageReaction> ChatMessageReactions => Set<ChatMessageReaction>();
    public DbSet<FarmAssistanceMessage> FarmAssistanceMessages => Set<FarmAssistanceMessage>();
    public DbSet<AppNotification> AppNotifications => Set<AppNotification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ConfigureIdentity(builder);

        foreach (var foreignKey in builder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.LastSeenAt);
            entity.HasQueryFilter(x => !x.IsDeleted);
            entity.HasIndex(x => x.CompanyId);
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UserTokens");
    }
}
