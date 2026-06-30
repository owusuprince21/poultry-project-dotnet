using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Hubs;

[Authorize]
public sealed class ActivityHub(ApplicationDbContext dbContext) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await TouchCurrentUserAsync();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await TouchCurrentUserAsync();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinCompany(Guid companyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, CompanyGroup(companyId));
    }

    public async Task LeaveCompany(Guid companyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, CompanyGroup(companyId));
    }

    public async Task SendTyping(Guid recipientUserId, bool isTyping)
    {
        var userId = Context.UserIdentifier;
        if (!Guid.TryParse(userId, out var senderUserId) || recipientUserId == Guid.Empty)
        {
            return;
        }

        var sender = await dbContext.Users.FindAsync(senderUserId);
        if (sender is null)
        {
            return;
        }

        await Clients.User(recipientUserId.ToString())
            .SendAsync("chat.typing", new TypingSignal(sender.Id, DisplayName(sender), isTyping));
    }

    public static string CompanyGroup(Guid companyId) => $"company:{companyId:N}";

    private static string DisplayName(PoultryFarm.Infrastructure.Identity.ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "User" : name;
    }

    private async Task TouchCurrentUserAsync()
    {
        var userId = Context.UserIdentifier;
        if (!Guid.TryParse(userId, out var id))
        {
            return;
        }

        var user = await dbContext.Users.FindAsync(id);
        if (user is null)
        {
            return;
        }

        user.LastSeenAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }
}

public sealed record TypingSignal(Guid SenderUserId, string SenderDisplayName, bool IsTyping);
