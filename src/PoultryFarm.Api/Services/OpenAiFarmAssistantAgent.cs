using System.Text;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Domain.Batches;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Feed;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Services;

public sealed class OpenAiFarmAssistantAgent(
    ApplicationDbContext dbContext,
    AiProviderClient aiProviderClient,
    ILogger<OpenAiFarmAssistantAgent> logger) : IFarmAssistantAgent
{
    public async Task<string> GetReplyAsync(Guid companyId, string userDisplayName, string prompt, CancellationToken cancellationToken = default)
    {
        var companyName = await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "your farm";

        if (IsPureGreeting(prompt))
        {
            return $"Hello {userDisplayName}! It is great to assist you today. How can I help you manage {companyName}?";
        }

        if (IsSimpleAcknowledgement(prompt))
        {
            return $"You're welcome, {userDisplayName}. I'm here whenever you need help with {companyName}.";
        }

        var localReply = await TryAnswerLocallyAsync(companyId, userDisplayName, prompt, cancellationToken);
        if (!string.IsNullOrWhiteSpace(localReply))
        {
            return localReply;
        }

        var context = await BuildFarmContextAsync(companyId, cancellationToken);
        var input = $"""
You are Poultry Farm Assistant, an enterprise poultry operations AI agent embedded inside a farm management system.
Answer naturally and professionally.
If the user message is only a greeting, greet them by name and ask how you can help manage their farm.
If the user message includes any real question, request, instruction, or farm problem, answer it directly. Do not ask "how can I help?" again.
Use the supplied farm activity context when answering operational questions about eggs, feed, medication, batches, health, mortality, inventory, sales, and daily routines.
Understand bird-health accounting exactly: sick bird records are active health-risk counts and do not reduce batch stock; dead bird records reduce the selected batch variant and overall batch current count. Sick birds can later recover, or some can die; recovered birds reduce active sick count only, while died birds reduce both active sick count and batch stock.
If the context has no records, say that clearly and ask the user to record activity before making data-based conclusions.
Keep answers practical, concise, and action-oriented for farm admins and workers.

User name: {userDisplayName}
Farm/company name: {companyName}

Farm activity context:
{context}

User message:
{prompt}
""";

        try
        {
            return await aiProviderClient.GenerateAsync(input, cancellationToken);
        }
        catch (AiProviderException ex)
        {
            logger.LogWarning(ex, "AI provider request failed.");
            if (ex.Provider == "OpenAI" && IsInsufficientQuota(ex.ResponseBody))
            {
                return BuildProviderIssueFallback(
                    userDisplayName,
                    "OpenAI rejected the request because the current API project has insufficient quota. Please switch AI:Provider to Gemini or check OpenAI billing, credits, or project limits.");
            }

            return BuildLocalFallback(userDisplayName, prompt, AssistantFallbackReason.RequestFailed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "AI provider request failed.");
            return BuildLocalFallback(userDisplayName, prompt, AssistantFallbackReason.RequestFailed);
        }
    }

    private async Task<string?> TryAnswerLocallyAsync(Guid companyId, string userDisplayName, string prompt, CancellationToken cancellationToken)
    {
        var normalized = NormalizePrompt(prompt);
        if (MentionsEggs(normalized) && MentionsInventoryIntent(normalized))
        {
            var stock = await GetEggStockAsync(companyId, cancellationToken);
            if (MentionsOnlyUnsortedEggs(normalized))
            {
                return stock.Unsorted > 0
                    ? $"Yes, {userDisplayName}. You currently have {stock.Unsorted:N0} unsorted eggs available."
                    : $"I do not see any unsorted eggs available right now, {userDisplayName}.";
            }

            return $"""
Current egg inventory:
- Small: {stock.Small:N0}
- Medium: {stock.Medium:N0}
- Large: {stock.Large:N0}
- Jumbo: {stock.ExtraLarge:N0}
- Unsorted: {stock.Unsorted:N0}
- Total: {stock.TotalEggs:N0}
""";
        }

        return null;
    }

    private async Task<EggStockSummary> GetEggStockAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var variants = await dbContext.BatchVariants
            .AsNoTracking()
            .Include(x => x.Batch)
            .Where(x => x.Batch != null && x.Batch.CompanyId == companyId && x.Batch.BirdType == BirdType.Layer)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (variants.Count == 0)
        {
            return new EggStockSummary(0, 0, 0, 0, 0);
        }

        var produced = await dbContext.EggProductions
            .AsNoTracking()
            .Where(x => variants.Contains(x.BatchVariantId))
            .GroupBy(_ => 1)
            .Select(g => new EggStockSummary(
                g.Sum(x => x.SmallEggs),
                g.Sum(x => x.MediumEggs),
                g.Sum(x => x.LargeEggs),
                g.Sum(x => x.ExtraLargeEggs),
                g.Sum(x => x.UnsortedEggs)))
            .FirstOrDefaultAsync(cancellationToken) ?? new EggStockSummary(0, 0, 0, 0, 0);

        var sold = await dbContext.EggSaleItems
            .AsNoTracking()
            .Where(x => variants.Contains(x.BatchVariantId))
            .GroupBy(x => x.Size)
            .Select(g => new { Size = g.Key, Eggs = g.Sum(x => x.Eggs) })
            .ToListAsync(cancellationToken);

        foreach (var item in sold)
        {
            produced = produced.Subtract(item.Size, item.Eggs);
        }

        return produced;
    }

    private async Task<string> BuildFarmContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart = today.AddDays(-7);
        var monthStart = today.AddDays(-30);

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => new { x.Name, x.Code })
            .FirstOrDefaultAsync(cancellationToken);

        var eggs = await dbContext.EggProductions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Date >= weekStart && x.Date <= today)
            .SumAsync(x => x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs, cancellationToken);

        var feedKg = await dbContext.FeedConsumptions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Date >= weekStart && x.Date <= today)
            .SumAsync(x => x.AmountKg, cancellationToken);

        var deaths = await dbContext.BirdHealthEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Date >= weekStart && x.Date <= today && x.Status == BirdHealthStatus.Dead)
            .SumAsync(x => x.Count, cancellationToken);

        var sickBirds = await dbContext.BirdHealthEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Date >= weekStart && x.Date <= today && x.Status == BirdHealthStatus.Sick)
            .SumAsync(x => x.Count, cancellationToken);

        var activeBatches = await dbContext.Batches
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == BatchStatus.Active, cancellationToken);

        var overdueMedication = await dbContext.Medications
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == ScheduleStatus.Overdue, cancellationToken);

        var lowInventory = await dbContext.FeedStocks
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.CurrentStockKg <= x.MinimumThresholdKg, cancellationToken);

        var eggSales = await dbContext.EggSales
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SaleDate >= weekStart && x.SaleDate <= today)
            .SumAsync(x => x.GrandTotal, cancellationToken);

        var birdSales = await dbContext.BirdSales
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SaleDate >= weekStart && x.SaleDate <= today)
            .SumAsync(x => x.TotalAmount, cancellationToken);

        var batches = await dbContext.Batches
            .AsNoTracking()
            .Include(x => x.Variants)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.Status == BatchStatus.Active)
            .ThenByDescending(x => x.ArrivalDate)
            .Take(12)
            .ToListAsync(cancellationToken);

        var feedConfigurations = await dbContext.FeedConfigurations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.BagSizeKg)
            .Select(x => new { x.Name, x.BagSizeKg })
            .ToListAsync(cancellationToken);

        var feedStockLots = await dbContext.FeedStockLots
            .AsNoTracking()
            .Include(x => x.FeedConfiguration)
            .Where(x => x.CompanyId == companyId && x.BagsRemaining > 0)
            .Select(x => new
            {
                FeedName = x.FeedConfiguration == null ? x.FeedType.ToString() : x.FeedConfiguration.Name,
                x.BagSizeKg,
                x.BagsRemaining
            })
            .ToListAsync(cancellationToken);

        var feedStocks = feedStockLots
            .GroupBy(x => new
            {
                x.FeedName,
                x.BagSizeKg
            })
            .Select(x => new
            {
                x.Key.FeedName,
                x.Key.BagSizeKg,
                Bags = x.Sum(lot => lot.BagsRemaining),
                Kg = x.Sum(lot => lot.BagsRemaining * lot.BagSizeKg)
            })
            .OrderBy(x => x.FeedName)
            .ThenBy(x => x.BagSizeKg)
            .ToList();

        var recentFeedConsumptions = await dbContext.FeedConsumptions
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Include(x => x.FeedConfiguration)
            .Where(x => x.CompanyId == companyId && x.Date >= monthStart && x.Date <= today)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var recentEggProduction = await dbContext.EggProductions
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Where(x => x.CompanyId == companyId && x.Date >= monthStart && x.Date <= today)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var recentHealthEvents = await dbContext.BirdHealthEvents
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Where(x => x.CompanyId == companyId && x.Date >= monthStart && x.Date <= today)
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var medications = await dbContext.Medications
            .AsNoTracking()
            .Include(x => x.Batch)
            .Where(x => x.CompanyId == companyId && (x.Status != ScheduleStatus.Completed || x.ScheduledDate >= monthStart))
            .OrderBy(x => x.ScheduledDate)
            .Take(20)
            .ToListAsync(cancellationToken);

        var debeaking = await dbContext.DebeakingSchedules
            .AsNoTracking()
            .Include(x => x.Batch)
            .Where(x => x.CompanyId == companyId && (x.Status != ScheduleStatus.Completed || x.ScheduledDate >= monthStart))
            .OrderBy(x => x.ScheduledDate)
            .Take(12)
            .ToListAsync(cancellationToken);

        var recentEggSales = await dbContext.EggSales
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SaleDate >= monthStart && x.SaleDate <= today)
            .OrderByDescending(x => x.SaleDate)
            .Take(10)
            .ToListAsync(cancellationToken);

        var recentBirdSales = await dbContext.BirdSales
            .AsNoTracking()
            .Include(x => x.Batch)
            .Include(x => x.BatchVariant)
            .Where(x => x.CompanyId == companyId && x.SaleDate >= monthStart && x.SaleDate <= today)
            .OrderByDescending(x => x.SaleDate)
            .Take(10)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Company: {company?.Name ?? "Unknown"} ({company?.Code ?? "N/A"})");
        sb.AppendLine($"Current date: {today:yyyy-MM-dd}");
        sb.AppendLine($"Summary window: {weekStart:yyyy-MM-dd} to {today:yyyy-MM-dd}");
        sb.AppendLine($"Eggs recorded: {eggs:N0}");
        sb.AppendLine($"Feed consumed: {feedKg:N1} kg");
        sb.AppendLine($"Bird deaths: {deaths:N0}");
        sb.AppendLine($"Sick bird count: {sickBirds:N0}");
        sb.AppendLine($"Active batches: {activeBatches:N0}");
        sb.AppendLine($"Overdue medication tasks: {overdueMedication:N0}");
        sb.AppendLine($"Low feed stock items: {lowInventory:N0}");
        sb.AppendLine($"Sales value: {(eggSales + birdSales):N2}");

        sb.AppendLine();
        sb.AppendLine("Bird batches:");
        if (batches.Count == 0)
        {
            sb.AppendLine("- No batches recorded.");
        }
        else
        {
            foreach (var batch in batches)
            {
                sb.AppendLine($"- {batch.BatchNumber}: {batch.BirdType}, breed {batch.Breed}, status {batch.Status}, arrived {batch.ArrivalDate:yyyy-MM-dd}, expected sale {batch.ExpectedSaleDate:yyyy-MM-dd}, initial {batch.InitialCount:N0}, current {batch.CurrentCount:N0}, survival {batch.SurvivalRate:N1}%.");
                foreach (var variant in batch.Variants.OrderBy(x => x.Color))
                {
                    sb.AppendLine($"  - Color {variant.Color}, egg color {variant.EggColor}, initial {variant.InitialCount:N0}, current {variant.CurrentCount:N0}.");
                }
            }
        }

        AppendSection(sb, "Configured feed types", feedConfigurations, x => $"- {x.Name}, {x.BagSizeKg} kg bags.");
        AppendSection(sb, "Feed stock currently available", feedStocks, x => $"- {x.FeedName}, {x.BagSizeKg} kg: {x.Bags:N0} bags, {x.Kg:N1} kg.");
        AppendSection(sb, "Recent feed consumption records", recentFeedConsumptions, x => $"- {x.Date:yyyy-MM-dd}: batch {BatchNumber(x.Batch)} breed {Breed(x.Batch)}, color {Color(x.BatchVariant)}, {FeedName(x.FeedConfiguration, x.FeedType)}, {x.BagsUsed:N0} bags x {x.BagSizeKg} kg = {x.AmountKg:N1} kg. Notes: {Blank(x.Notes)}");
        AppendSection(sb, "Recent egg production records", recentEggProduction, x => $"- {x.Date:yyyy-MM-dd}: batch {BatchNumber(x.Batch)} breed {Breed(x.Batch)}, color {Color(x.BatchVariant)}, {x.CollectionType}, total {x.TotalEggs:N0} eggs (small {x.SmallEggs:N0}, medium {x.MediumEggs:N0}, large {x.LargeEggs:N0}, jumbo {x.ExtraLargeEggs:N0}, unsorted {x.UnsortedEggs:N0}). Notes: {Blank(x.Notes)}");
        AppendSection(sb, "Recent bird health events", recentHealthEvents, x => $"- {x.Date:yyyy-MM-dd}: batch {BatchNumber(x.Batch)} breed {Breed(x.Batch)}, color {Color(x.BatchVariant)}, {x.Status}, count {x.Count:N0}. Cause: {Blank(x.Cause)}");
        AppendSection(sb, "Medication schedule and recent medication", medications, x => $"- {x.ScheduledDate:yyyy-MM-dd}: {x.MedicationName}, purpose {Blank(x.Purpose)}, dosage {Blank(x.Dosage)}, frequency {Blank(x.Frequency)}, status {x.Status}, batch {BatchNumber(x.Batch)} breed {Breed(x.Batch)}, next due {(x.NextDueDate.HasValue ? x.NextDueDate.Value.ToString("yyyy-MM-dd") : "none")}. Notes: {Blank(x.Notes)}");
        AppendSection(sb, "Debeaking schedule", debeaking, x => $"- {x.ScheduledDate:yyyy-MM-dd}: {x.DebeakingType}, bird age {x.BirdAgeWeeks:N0} weeks, status {x.Status}, batch {BatchNumber(x.Batch)} breed {Breed(x.Batch)}, completed {(x.CompletedDate.HasValue ? x.CompletedDate.Value.ToString("yyyy-MM-dd") : "not completed")}. Notes: {Blank(x.Notes)}");
        AppendSection(sb, "Recent egg sales", recentEggSales, x => $"- {x.SaleDate:yyyy-MM-dd}: buyer {x.BuyerName}, receipt {x.ReceiptId}, amount {x.GrandTotal:N2}, {x.PaymentType}/{x.PaymentStatus}.");
        AppendSection(sb, "Recent bird sales", recentBirdSales, x => $"- {x.SaleDate:yyyy-MM-dd}: buyer {x.BuyerName}, batch {BatchNumber(x.Batch)} breed {Breed(x.Batch)}, color {Color(x.BatchVariant)}, birds {x.BirdsSold:N0}, price {x.PricePerBird:N2}, amount {x.TotalAmount:N2}.");

        return sb.ToString();
    }

    private static void AppendSection<T>(StringBuilder sb, string title, IReadOnlyCollection<T> items, Func<T, string> format)
    {
        sb.AppendLine();
        sb.AppendLine($"{title}:");
        if (items.Count == 0)
        {
            sb.AppendLine("- No records.");
            return;
        }

        foreach (var item in items)
        {
            sb.AppendLine(format(item));
        }
    }

    private static string Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();

    private static string NormalizePrompt(string prompt) =>
        prompt.Trim().ToLowerInvariant();

    private static bool MentionsEggs(string normalizedPrompt) =>
        normalizedPrompt.Contains("egg", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsInventoryIntent(string normalizedPrompt) =>
        normalizedPrompt.Contains("need", StringComparison.OrdinalIgnoreCase) ||
        normalizedPrompt.Contains("available", StringComparison.OrdinalIgnoreCase) ||
        normalizedPrompt.Contains("have", StringComparison.OrdinalIgnoreCase) ||
        normalizedPrompt.Contains("stock", StringComparison.OrdinalIgnoreCase) ||
        normalizedPrompt.Contains("inventory", StringComparison.OrdinalIgnoreCase) ||
        normalizedPrompt.Contains("how many", StringComparison.OrdinalIgnoreCase) ||
        normalizedPrompt.Contains("only", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsOnlyUnsortedEggs(string normalizedPrompt) =>
        normalizedPrompt.Contains("unsorted", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPrompt.Contains("small", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPrompt.Contains("medium", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPrompt.Contains("large", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPrompt.Contains("jumbo", StringComparison.OrdinalIgnoreCase);

    private static bool IsPureGreeting(string prompt)
    {
        var normalized = prompt
            .Trim()
            .Trim('.', '!', '?', ',', ';', ':')
            .ToLowerInvariant();

        return normalized is "hi" or "hello" or "hey" or "good morning" or "good afternoon" or "good evening" or "greetings";
    }

    private static bool IsSimpleAcknowledgement(string prompt)
    {
        var normalized = prompt
            .Trim()
            .Trim('.', '!', '?', ',', ';', ':')
            .ToLowerInvariant();

        return normalized is
            "ok" or
            "okay" or
            "alright" or
            "all right" or
            "fine" or
            "cool" or
            "great" or
            "nice" or
            "noted" or
            "thanks" or
            "thank you" or
            "thank you very much" or
            "thanks a lot" or
            "appreciate it";
    }

    private static string BatchNumber(Batch? batch) => batch?.BatchNumber ?? "Unknown batch";

    private static string Breed(Batch? batch) => string.IsNullOrWhiteSpace(batch?.Breed) ? "unknown" : batch.Breed;

    private static VariantColor Color(BatchVariant? variant) => variant?.Color ?? VariantColor.Mixed;

    private static string FeedName(FeedConfiguration? configuration, FeedType fallback) =>
        string.IsNullOrWhiteSpace(configuration?.Name) ? fallback.ToString() : configuration.Name;

    private static string BuildLocalFallback(
        string userDisplayName,
        string prompt,
        AssistantFallbackReason reason)
    {
        var lowerPrompt = prompt.Trim().ToLowerInvariant();
        if (lowerPrompt is "hi" or "hello" or "hey" or "good morning" or "good afternoon" or "good evening")
        {
            return $"Hi, {userDisplayName}. How can I help you with your poultry farm work today?";
        }

        _ = reason;

        return $"""
I cannot reach the AI service right now, {userDisplayName}. Please try again in a moment.

For farm records such as egg inventory, feed stock, sales, medication, mortality, or batch status, ask me directly and I will check the saved records I can access.
""";
    }

    private enum AssistantFallbackReason
    {
        MissingApiKey,
        RequestFailed,
        EmptyResponse
    }

    private static bool IsInsufficientQuota(string body) =>
        body.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("exceeded your current quota", StringComparison.OrdinalIgnoreCase);

    private static string BuildProviderIssueFallback(string userDisplayName, string detail) =>
        $"""
I can help, {userDisplayName}, but the external AI provider is not available right now.

{detail}
""";

    private sealed record EggStockSummary(int Small, int Medium, int Large, int ExtraLarge, int Unsorted)
    {
        public int TotalEggs => Small + Medium + Large + ExtraLarge + Unsorted;

        public EggStockSummary Subtract(EggSize size, int count)
        {
            return size switch
            {
                EggSize.Small => this with { Small = Math.Max(0, Small - count) },
                EggSize.Medium => this with { Medium = Math.Max(0, Medium - count) },
                EggSize.Large => this with { Large = Math.Max(0, Large - count) },
                EggSize.ExtraLarge => this with { ExtraLarge = Math.Max(0, ExtraLarge - count) },
                EggSize.Unsorted => this with { Unsorted = Math.Max(0, Unsorted - count) },
                _ => this
            };
        }
    }
}
