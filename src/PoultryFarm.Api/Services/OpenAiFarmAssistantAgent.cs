using System.Text;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Domain.Common;
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

        // Only load farm records when the question likely needs them — this is the main latency win.
        var farmContext = NeedsFarmData(prompt)
            ? await BuildFarmContextAsync(companyId, cancellationToken)
            : "No farm record snapshot loaded for this question (general advice mode).";

        var input = $"""
You are Poultry Farm Assistant — a practical helper for poultry farmers.

Help with anything they need for poultry farming and farm business. Be creative and actionable.
Prefer short, clear answers (about 80-180 words) unless the problem is complex or safety-critical.
Use farm records when provided. Never invent this farm's numbers. General poultry knowledge is OK.
Bird-health accounting: sick counts do not reduce stock; deaths do. Recovered reduces sick only; died reduces sick and stock.
Answer directly — do not ask how you can help if they already asked something.
If a vet is clearly needed, say so while still giving first-line practical steps.

User: {userDisplayName}
Farm: {companyName}

Farm context:
{farmContext}

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
            .Where(x => x.CompanyId == companyId && x.Status == BatchStatus.Active)
            .OrderByDescending(x => x.ArrivalDate)
            .Take(6)
            .Select(x => new
            {
                x.BatchNumber,
                x.BirdType,
                x.Breed,
                x.CurrentCount,
                x.InitialCount,
                x.SurvivalRate
            })
            .ToListAsync(cancellationToken);

        var feedStocks = await dbContext.FeedStockLots
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BagsRemaining > 0)
            .GroupBy(x => new
            {
                FeedName = x.FeedConfiguration == null ? x.FeedType.ToString() : x.FeedConfiguration.Name,
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
            .Take(8)
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Company: {company?.Name ?? "Unknown"} ({company?.Code ?? "N/A"})");
        sb.AppendLine($"Date: {today:yyyy-MM-dd}; last 7 days summary");
        sb.AppendLine($"Eggs: {eggs:N0}; feed used: {feedKg:N1} kg; deaths: {deaths:N0}; sick: {sickBirds:N0}");
        sb.AppendLine($"Active batches: {activeBatches:N0}; overdue meds: {overdueMedication:N0}; low feed items: {lowInventory:N0}");
        sb.AppendLine($"Sales value (7d): {(eggSales + birdSales):N2}");

        sb.AppendLine("Active batches:");
        if (batches.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var batch in batches)
            {
                sb.AppendLine($"- {batch.BatchNumber}: {batch.BirdType}/{batch.Breed}, current {batch.CurrentCount:N0}/{batch.InitialCount:N0}, survival {batch.SurvivalRate:N1}%");
            }
        }

        sb.AppendLine("Feed stock:");
        if (feedStocks.Count == 0)
        {
            sb.AppendLine("- None");
        }
        else
        {
            foreach (var stock in feedStocks)
            {
                sb.AppendLine($"- {stock.FeedName} {stock.BagSizeKg}kg: {stock.Bags:N0} bags ({stock.Kg:N1} kg)");
            }
        }

        return sb.ToString();
    }

    private static bool NeedsFarmData(string prompt)
    {
        var normalized = NormalizePrompt(prompt);
        string[] keywords =
        [
            "egg", "feed", "batch", "stock", "inventory", "sale", "sold", "medication", "medicine", "vaccine",
            "sick", "dead", "death", "mortality", "debeak", "production", "layer", "broiler", "bird count",
            "how many", "available", "overdue", "schedule", "record", "report", "today", "this week",
            "my farm", "our farm", "current", "status", "profit", "revenue", "buyer", "listing"
        ];

        return keywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
    }

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
