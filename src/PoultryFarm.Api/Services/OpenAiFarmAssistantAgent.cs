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

        if (IsCasualMessage(prompt))
        {
            return $"Hello {userDisplayName}. How can I help you with {companyName}?";
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

        var wantsFarmRecords = NeedsFarmData(prompt);
        var farmContext = wantsFarmRecords
            ? await BuildFarmContextAsync(companyId, cancellationToken)
            : "No farm records were requested.";

        var input = $"""
You are the operations assistant for one poultry farm. Be brief and answer only what the user asked.

A greeting or small comment gets one or two short sentences. Do not introduce yourself as the farm manager, and do not attach a status update.
Use the farm records only when the user asks about this farm's eggs, feed, birds, health, medication, sales, stock, batches, or other saved activity.
When they do ask, answer that question from the complete history below. Do not add eggs, feed, sales, sick birds, or batch counts they did not ask about.
Current sick birds is the net of every sick entry ever saved, including older dates, minus later recoveries and deaths. Quote batch, color, date, and cause when health was asked about.
If the records do not contain what was asked, say there is no saved record for it. Never invent this farm's counts, dates, doses, or sales.
You cannot save a new egg or feed entry from chat. If they ask you to record one, point them to the Egg Production or Feed page.
General poultry knowledge is fine when it does not pretend to be this farm's data.

User: {userDisplayName}
Farm: {companyName}

Farm records:
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

    public async Task<string> AdviseOnObservationAsync(Guid companyId, string userDisplayName, string category, DateOnly date, string notes, CancellationToken cancellationToken = default)
    {
        var farmContext = await BuildFarmContextAsync(companyId, cancellationToken);
        var input = $"""
You are Poultry Farm Assistant. A worker filed an end-of-day farm observation.
Write an in-app alert for the farm owner and the workers.
Write only the action the farm should take today.
Do not repeat the worker's observation and do not start with "Observation".
Mention medication, isolation, feed changes, or a vet only when the observation and farm records support it.
Do not invent this farm's numbers or a drug dose. Keep it under 120 words. No greeting.

Category: {category}
Date: {date:yyyy-MM-dd}
Recorded by: {userDisplayName}
Observation:
{notes}

Farm records:
{farmContext}
""";

        try
        {
            return await aiProviderClient.GenerateAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is AiProviderException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Observation advice could not be generated.");
            return "Check the flock, separate anything that looks abnormal, and review today's feed and medication schedule before the next round.";
        }
    }

    private async Task<string?> TryAnswerLocallyAsync(Guid companyId, string userDisplayName, string prompt, CancellationToken cancellationToken)
    {
        var normalized = NormalizePrompt(prompt);
        if (AsksAboutSickBirds(normalized))
        {
            return await DescribeCurrentSickAsync(companyId, userDisplayName, cancellationToken);
        }

        if (MentionsEggs(normalized) && MentionsInventoryIntent(normalized) && !AsksAboutSavedHistory(normalized))
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

        var produced = (await dbContext.EggProductions
            .AsNoTracking()
            .Where(x => variants.Contains(x.BatchVariantId))
            .GroupBy(_ => 1)
            .Select(g => new EggStockSummary(
                g.Sum(x => x.SmallEggs),
                g.Sum(x => x.MediumEggs),
                g.Sum(x => x.LargeEggs),
                g.Sum(x => x.ExtraLargeEggs),
                g.Sum(x => x.UnsortedEggs)))
            .ToListAsync(cancellationToken)).FirstOrDefault() ?? new EggStockSummary(0, 0, 0, 0, 0);

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

    private async Task<string> DescribeCurrentSickAsync(Guid companyId, string userDisplayName, CancellationToken cancellationToken)
    {
        var events = await dbContext.BirdHealthEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Date,
                x.Status,
                x.Count,
                x.Cause,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber,
                Color = x.BatchVariant == null ? "unknown color" : x.BatchVariant.Color.ToString()
            })
            .ToListAsync(cancellationToken);

        var openSick = events
            .Where(x => x.Status == BirdHealthStatus.Sick)
            .GroupBy(x => new { x.BatchNumber, x.Color })
            .Select(group => new
            {
                group.Key.BatchNumber,
                group.Key.Color,
                Net = group.Sum(x => x.Count),
                Since = group.Where(x => x.Count > 0).Select(x => (DateOnly?)x.Date).Min(),
                Cause = group.Where(x => x.Count > 0).OrderByDescending(x => x.Date).Select(x => x.Cause).FirstOrDefault()
            })
            .Where(x => x.Net > 0)
            .OrderBy(x => x.Since)
            .ToList();
        var deaths = events.Where(x => x.Status == BirdHealthStatus.Dead).Sum(x => x.Count);

        if (openSick.Count == 0)
        {
            return deaths > 0
                ? $"No sick birds are still open, {userDisplayName}. Deaths on record for this farm: {deaths:N0}."
                : $"No sick birds or deaths are saved for this farm, {userDisplayName}.";
        }

        var lines = openSick.Select(x =>
            $"- {x.BatchNumber} ({x.Color}): {x.Net:N0} still sick, first recorded {x.Since:yyyy-MM-dd}. Cause: {Trim(x.Cause, 160)}");
        return $"""
You currently have {openSick.Sum(x => x.Net):N0} sick birds still open, {userDisplayName}. This uses every sick record for the farm, including entries older than the last week.
{string.Join(Environment.NewLine, lines)}
Deaths on record: {deaths:N0}.
""";
    }

    private async Task<string> BuildFarmContextAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => new { x.Name, x.Code })
            .FirstOrDefaultAsync(cancellationToken);

        var batches = await dbContext.Batches
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.ArrivalDate)
            .Select(x => new
            {
                x.BatchNumber,
                x.BirdType,
                x.Breed,
                x.Status,
                x.ArrivalDate,
                x.CurrentCount,
                x.InitialCount,
                Colors = x.Variants.Select(v => new { v.Color, v.CurrentCount, v.InitialCount }).ToList()
            })
            .ToListAsync(cancellationToken);

        var health = await dbContext.BirdHealthEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Date)
            .Select(x => new
            {
                x.Date,
                x.Status,
                x.Count,
                x.Cause,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber,
                Color = x.BatchVariant == null ? "unknown color" : x.BatchVariant.Color.ToString()
            })
            .ToListAsync(cancellationToken);

        var eggs = await dbContext.EggProductions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.CollectionPeriod)
            .Select(x => new
            {
                x.Date,
                x.CollectionPeriod,
                x.CollectionType,
                x.EggColor,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber,
                Total = x.SmallEggs + x.MediumEggs + x.LargeEggs + x.ExtraLargeEggs + x.UnsortedEggs,
                x.SmallEggs,
                x.MediumEggs,
                x.LargeEggs,
                x.ExtraLargeEggs,
                x.UnsortedEggs
            })
            .ToListAsync(cancellationToken);

        var feed = await dbContext.FeedConsumptions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.CollectionPeriod)
            .Select(x => new
            {
                x.Date,
                x.CollectionPeriod,
                x.AmountKg,
                x.BagsUsed,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber,
                FeedName = x.FeedConfiguration == null ? x.FeedType.ToString() : x.FeedConfiguration.Name
            })
            .ToListAsync(cancellationToken);

        var medications = await dbContext.Medications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ScheduledDate)
            .Select(x => new
            {
                x.ScheduledDate,
                x.MedicationName,
                x.Dosage,
                x.Status,
                x.Purpose,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber
            })
            .ToListAsync(cancellationToken);

        var debeaking = await dbContext.DebeakingSchedules
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.ScheduledDate)
            .Select(x => new
            {
                x.ScheduledDate,
                x.DebeakingType,
                x.Status,
                x.BirdAgeWeeks,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber
            })
            .ToListAsync(cancellationToken);

        var eggSales = await dbContext.EggSales
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.SaleDate)
            .Select(x => new { x.SaleDate, x.BuyerName, x.GrandTotal, x.PaymentStatus })
            .ToListAsync(cancellationToken);

        var birdSales = await dbContext.BirdSales
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.SaleDate)
            .Select(x => new
            {
                x.SaleDate,
                x.BuyerName,
                x.BirdsSold,
                x.TotalAmount,
                BatchNumber = x.Batch == null ? "Unknown batch" : x.Batch.BatchNumber
            })
            .ToListAsync(cancellationToken);

        var observations = await dbContext.DailyObservations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Date)
            .Select(x => new { x.Date, x.Category, x.AuthorName, x.Notes })
            .ToListAsync(cancellationToken);

        var listings = await dbContext.MarketplaceListings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Title, x.ListingType, x.Status, x.QuantityOffered, x.UnitLabel, x.PriceAmount })
            .ToListAsync(cancellationToken);

        var posts = await dbContext.FarmActivityPosts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.CreatedAt, x.Title, x.Body, x.IsPublished })
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
            .ToListAsync(cancellationToken);

        var stock = await GetEggStockAsync(companyId, cancellationToken);
        var currentSick = Math.Max(0, health.Where(x => x.Status == BirdHealthStatus.Sick).Sum(x => x.Count));
        var deaths = health.Where(x => x.Status == BirdHealthStatus.Dead).Sum(x => x.Count);
        var producedEggs = eggs.Sum(x => x.Total);

        var sb = new StringBuilder();
        sb.AppendLine($"Farm: {company?.Name ?? "Unknown"} ({company?.Code ?? "N/A"})");
        sb.AppendLine($"Today: {today:yyyy-MM-dd}. Scope: every saved record for this farm. No recent-day limit.");
        sb.AppendLine($"Current sick birds still open: {currentSick:N0}. Total deaths: {deaths:N0}.");
        sb.AppendLine($"Eggs produced, all dates: {producedEggs:N0}. Eggs still available: small {stock.Small:N0}, medium {stock.Medium:N0}, large {stock.Large:N0}, jumbo {stock.ExtraLarge:N0}, unsorted {stock.Unsorted:N0}, total {stock.TotalEggs:N0}.");
        sb.AppendLine($"Feed used, all dates: {feed.Sum(x => x.AmountKg):N1} kg. Egg sales: {eggSales.Sum(x => x.GrandTotal):N2}. Bird sales: {birdSales.Sum(x => x.TotalAmount):N2}.");
        sb.AppendLine("If a section says none, that record type has not been saved.");

        sb.AppendLine("Batches:");
        AppendOrNone(sb, batches.Count == 0);
        foreach (var batch in batches)
        {
            var colors = batch.Colors.Count == 0
                ? "no colors"
                : string.Join(", ", batch.Colors.Select(v => $"{v.Color} {v.CurrentCount:N0}/{v.InitialCount:N0}"));
            var survival = batch.InitialCount <= 0 ? 0 : Math.Round((decimal)batch.CurrentCount / batch.InitialCount * 100m, 1);
            sb.AppendLine($"- {batch.BatchNumber}: {batch.Status} {batch.BirdType}/{batch.Breed}, arrived {batch.ArrivalDate:yyyy-MM-dd}, birds {batch.CurrentCount:N0}/{batch.InitialCount:N0}, survival {survival:N1}%. Colors: {colors}");
        }

        sb.AppendLine("Bird health, every entry:");
        AppendOrNone(sb, health.Count == 0);
        foreach (var item in health)
        {
            sb.AppendLine($"- {item.Date:yyyy-MM-dd} {item.Status} {item.Count:N0} {item.BatchNumber} {item.Color}. Cause: {Trim(item.Cause, 160)}");
        }

        sb.AppendLine("Egg production, every entry:");
        AppendOrNone(sb, eggs.Count == 0);
        foreach (var item in eggs)
        {
            sb.AppendLine($"- {item.Date:yyyy-MM-dd} {item.CollectionPeriod} {item.CollectionType} {item.EggColor} {item.BatchNumber}: {item.Total:N0} eggs (small {item.SmallEggs:N0}, medium {item.MediumEggs:N0}, large {item.LargeEggs:N0}, jumbo {item.ExtraLargeEggs:N0}, unsorted {item.UnsortedEggs:N0})");
        }

        sb.AppendLine("Feed consumption, every entry:");
        AppendOrNone(sb, feed.Count == 0);
        foreach (var item in feed)
        {
            sb.AppendLine($"- {item.Date:yyyy-MM-dd} {item.CollectionPeriod} {item.BatchNumber} {item.FeedName}: {item.AmountKg:N1} kg, {item.BagsUsed:N0} bags");
        }

        sb.AppendLine("Feed stock on hand:");
        AppendOrNone(sb, feedStocks.Count == 0);
        foreach (var item in feedStocks)
        {
            sb.AppendLine($"- {item.FeedName} {item.BagSizeKg}kg: {item.Bags:N0} bags ({item.Kg:N1} kg)");
        }

        sb.AppendLine("Medication, every entry:");
        AppendOrNone(sb, medications.Count == 0);
        foreach (var item in medications)
        {
            sb.AppendLine($"- {item.ScheduledDate:yyyy-MM-dd} {item.Status} {item.BatchNumber} {item.MedicationName}, dose {Trim(item.Dosage, 80)}, purpose {Trim(item.Purpose, 80)}");
        }

        sb.AppendLine("Debeaking, every entry:");
        AppendOrNone(sb, debeaking.Count == 0);
        foreach (var item in debeaking)
        {
            sb.AppendLine($"- {item.ScheduledDate:yyyy-MM-dd} {item.Status} {item.BatchNumber} {item.DebeakingType}, age {item.BirdAgeWeeks} weeks");
        }

        sb.AppendLine("Egg sales, every entry:");
        AppendOrNone(sb, eggSales.Count == 0);
        foreach (var item in eggSales)
        {
            sb.AppendLine($"- {item.SaleDate:yyyy-MM-dd} {item.BuyerName}: {item.GrandTotal:N2}, {item.PaymentStatus}");
        }

        sb.AppendLine("Bird sales, every entry:");
        AppendOrNone(sb, birdSales.Count == 0);
        foreach (var item in birdSales)
        {
            sb.AppendLine($"- {item.SaleDate:yyyy-MM-dd} {item.BatchNumber} {item.BuyerName}: {item.BirdsSold:N0} birds, {item.TotalAmount:N2}");
        }

        sb.AppendLine("Worker observations, every entry:");
        AppendOrNone(sb, observations.Count == 0);
        foreach (var item in observations)
        {
            sb.AppendLine($"- {item.Date:yyyy-MM-dd} {item.Category} by {item.AuthorName}: {Trim(item.Notes, 240)}");
        }

        sb.AppendLine("Marketplace listings:");
        AppendOrNone(sb, listings.Count == 0);
        foreach (var item in listings)
        {
            sb.AppendLine($"- {item.Status} {item.ListingType} {item.Title}: {item.QuantityOffered:N0} {item.UnitLabel}, price {item.PriceAmount:N2}");
        }

        sb.AppendLine("Farm activity posts:");
        AppendOrNone(sb, posts.Count == 0);
        foreach (var item in posts)
        {
            sb.AppendLine($"- {item.CreatedAt:yyyy-MM-dd} {(item.IsPublished ? "published" : "draft")} {item.Title}: {Trim(item.Body, 180)}");
        }

        return sb.ToString();
    }

    private static void AppendOrNone(StringBuilder sb, bool empty)
    {
        if (empty)
        {
            sb.AppendLine("- None saved");
        }
    }

    private static string Trim(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value.Length <= max ? value.Trim() : value.Trim()[..max] + "...";

    private static bool AsksAboutSickBirds(string normalizedPrompt) =>
        (normalizedPrompt.Contains("sick", StringComparison.Ordinal) || normalizedPrompt.Contains("ill bird", StringComparison.Ordinal)) &&
        (normalizedPrompt.Contains("how many", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("how much", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("do i have", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("do we have", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("current", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("still", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("any sick", StringComparison.Ordinal) ||
         normalizedPrompt.Contains("healthy", StringComparison.Ordinal));

    private static bool AsksAboutSavedHistory(string normalizedPrompt) =>
        normalizedPrompt.Contains("produc", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("collect", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("record", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("morning", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("afternoon", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("evening", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("yesterday", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("last week", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("last month", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("history", StringComparison.Ordinal) ||
        normalizedPrompt.Contains("feed", StringComparison.Ordinal);

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

    private static bool IsCasualMessage(string prompt)
    {
        var words = prompt
            .Trim()
            .ToLowerInvariant()
            .Split([' ', ',', '.', '!', '?', ';', ':', '\'', '"'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is 0 or > 8)
        {
            return false;
        }

        string[] allowed =
        [
            "hi", "hello", "hey", "hiya", "howdy", "yo", "good", "morning", "afternoon", "evening",
            "greetings", "what", "whats", "what's", "up", "how", "are", "you", "doing", "today",
            "friend", "buddy", "boss", "there", "pal", "mate", "sir", "dear", "my", "please"
        ];
        if (!words.All(word => allowed.Contains(word)))
        {
            return false;
        }

        return words.Any(word => word is "hi" or "hello" or "hey" or "hiya" or "howdy" or "yo" or "greetings" or "morning" or "afternoon" or "evening" or "up")
            || (words.Contains("how") && words.Contains("you"));
    }

    private static bool NeedsFarmData(string prompt)
    {
        var normalized = NormalizePrompt(prompt);
        string[] keywords =
        [
            "egg", "feed", "batch", "stock", "inventory", "sale", "sold", "medication", "medicine", "vaccine",
            "sick", "dead", "death", "mortality", "health", "healthy", "debeak", "production", "layer", "broiler",
            "bird", "flock", "how many", "how much", "available", "overdue", "schedule", "record", "report",
            "observation", "today", "yesterday", "week", "month", "status", "update", "summary", "overview",
            "my farm", "our farm", "profit", "revenue", "buyer", "listing", "activity"
        ];

        return keywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));
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
        if (IsCasualMessage(prompt))
        {
            return $"Hi, {userDisplayName}. How can I help you with your poultry farm work today?";
        }

        _ = reason;

        return $"""
I cannot reach the AI service right now, {userDisplayName}. Please try again in a moment.

For farm records such as egg production, feed use, sick birds, deaths, sales, medication, or batch status, ask me directly. I check the full saved history for this farm.
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
