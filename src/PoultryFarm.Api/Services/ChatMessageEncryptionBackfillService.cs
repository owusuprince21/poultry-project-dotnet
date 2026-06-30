using Microsoft.EntityFrameworkCore;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Services;

public sealed class ChatMessageEncryptionBackfillService(
    IServiceScopeFactory scopeFactory,
    ILogger<ChatMessageEncryptionBackfillService> logger) : BackgroundService
{
    private const string Prefix = "enc:v1:";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<IChatMessageProtector>();

            while (!stoppingToken.IsCancellationRequested)
            {
                var messages = await dbContext.ChatMessages
                    .Where(x => !x.Body.StartsWith(Prefix))
                    .OrderBy(x => x.SentAt)
                    .Take(200)
                    .ToListAsync(stoppingToken);

                if (messages.Count == 0)
                {
                    return;
                }

                foreach (var message in messages)
                {
                    message.Body = protector.Protect(message.Body);
                }

                await dbContext.SaveChangesAsync(stoppingToken);
                logger.LogInformation("Encrypted {Count} existing chat message(s).", messages.Count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Existing chat message encryption backfill failed.");
        }
    }
}
