namespace PoultryFarm.Application.Dashboard.DTOs;

public sealed record DashboardStatsDto(
    int TotalActiveBirds,
    int TodayEggProduction,
    decimal WeeklyFeedConsumption,
    int OverdueMedications,
    int OverdueDebeaking,
    int LowFeedStock,
    int CurrentBatchAge,
    decimal ProductionRate,
    int ActiveBatches,
    int EggInventory,
    decimal FeedStockKg,
    decimal TodaySalesRevenue,
    decimal WeeklySalesRevenue,
    int WeeklyEggsSold,
    int WeeklyBirdsSold,
    int WeeklyHealthEvents);
